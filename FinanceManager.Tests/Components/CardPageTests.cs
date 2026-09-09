using Bunit;
using DocumentFormat.OpenXml.Office2016.Drawing.ChartDrawing;
using FinanceManager.Application;
using FinanceManager.Shared;
using FinanceManager.Shared.Dtos.Contacts;
using FinanceManager.Web;
using FinanceManager.Web.Components.Pages;
using msTools.Web.Blazor;
using FinanceManager.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Moq;
using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace FinanceManager.Tests.Components
{
    /// <summary>
    /// Tests for the generic <see cref="FinanceManager.Web.Components.Pages.CardPage"/>, exercised
    /// through its "contacts" kind: editing a field and saving forwards the change to the update
    /// API and reflects it back in the input, opening the Attachments ribbon action renders the
    /// attachments overlay, and a contact loaded with non-default values (category, description,
    /// payment-intermediary flag, symbol image) is fully reflected in the rendered card fields.
    /// </summary>
    public sealed class CardPageTests : BunitContext
    {
        /// <summary>
        /// Fixes the thread culture to en-US (so formatted values are deterministic across machines)
        /// and stubs the loading-bar JS interop calls the card page invokes during render.
        /// </summary>
        public CardPageTests()
        {
            CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("en-US");
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            CultureInfo.CurrentUICulture = new CultureInfo("en-US");
            Services.AddScoped<LoadingBarService>();
            Services.AddSingleton<IConfirmationService>(Mock.Of<IConfirmationService>());
            JSInterop.SetupVoid("financeManager.loadingBar.start").SetVoidResult();
            JSInterop.SetupVoid("financeManager.loadingBar.stop").SetVoidResult();
        }

        /// <summary>
        /// Verifies the core edit-and-save flow: typing into the Name input and clicking the ribbon
        /// Save button calls <c>Contacts_UpdateAsync</c> with the edited value and the input reflects
        /// the saved name afterward - confirming the card's two-way binding actually reaches the API
        /// rather than only updating local view-model state.
        /// </summary>
        [Fact]
        public async Task ContactCard_EditFields_And_Save_CallsApiUpdate()
        {
            // Arrange
            var apiMock = new Mock<IApiClient>();
            var id = Guid.NewGuid();
            var original = new ContactDto(id, "Old Name", ContactType.Person, null, "desc", false, null);
            apiMock.Setup(a => a.Contacts_GetAsync(id, It.IsAny<System.Threading.CancellationToken>())).ReturnsAsync(original);

            ContactDto? updatedDto = null;
            apiMock.Setup(a => a.Contacts_UpdateAsync(id, It.IsAny<ContactUpdateRequest>(), It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync((Guid i, ContactUpdateRequest req, System.Threading.CancellationToken ct) =>
                {
                    updatedDto = new ContactDto(id, req.Name, req.Type, req.CategoryId, req.Description, req.IsPaymentIntermediary ?? false, null);
                    return updatedDto;
                });

            Services.AddSingleton(apiMock.Object);
            Services.AddLocalization(options => options.ResourcesPath = "Resources");
            Services.AddSingleton(typeof(IStringLocalizer<Pages>), new PagesStringLocalizer());
            Services.AddSingleton<ICurrentUserService>(new TestCurrentUserService { IsAuthenticated = false });

            // Render CardPage for contacts
            var cut = Render<FinanceManager.Web.Components.Pages.CardPage>(parameters => parameters
                .Add(p => p.Kind, "contacts")
                .Add(p => p.Id, id)
            );

            // Wait until provider initialized and first input appears
            cut.WaitForState(() => cut.FindAll("input.card-input").Count > 0);

            // Act: change the first text input (assumed to be Name) and click Save
            var nameInput = cut.FindAll("input.card-input").First();
            // The component wires to 'oninput' (input event); use Input() to trigger that
            nameInput.Input("New Name");

            // Click Save button in ribbon (id == "Save")
            var saveBtn = cut.FindAll("button").FirstOrDefault(b => b.Id == "Save");
            Assert.NotNull(saveBtn);
            saveBtn!.Click();

            // Wait for API update to be called and UI to update
            cut.WaitForAssertion(() => apiMock.Verify(a => a.Contacts_UpdateAsync(id, It.IsAny<ContactUpdateRequest>(), It.IsAny<System.Threading.CancellationToken>()), Times.Once));
            cut.WaitForAssertion(() => Assert.Equal("New Name", cut.FindAll("input.card-input").First().GetAttribute("value")));

            // Also ensure our mocked update returned expected name
            Assert.NotNull(updatedDto);
            Assert.Equal("New Name", updatedDto!.Name);
        }

        /// <summary>
        /// Verifies that clicking the ribbon's Attachments button opens the attachments overlay
        /// (indicated by the presence of the drag-and-drop upload area), confirming the ribbon action
        /// is wired to actually toggle the attachments panel rather than being a dead button.
        /// </summary>
        [Fact]
        public async Task ContactCard_OpenAttachments_RendersAttachmentsPanelOverlay()
        {
            // Arrange
            var apiMock = new Mock<IApiClient>();
            var id = Guid.NewGuid();
            var original = new ContactDto(id, "Name", ContactType.Person, null, "desc", false, null);
            apiMock.Setup(a => a.Contacts_GetAsync(id, It.IsAny<System.Threading.CancellationToken>())).ReturnsAsync(original);

            // attachments panel expectations: empty categories and empty page
            apiMock.Setup(a => a.Attachments_ListCategoriesAsync(It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(Array.Empty<AttachmentCategoryDto>());
            apiMock.Setup(a => a.Attachments_ListAsync(It.IsAny<short>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Guid?>(), It.IsAny<bool?>(), It.IsAny<string?>(), It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(new PageResult<AttachmentDto> { Items = new System.Collections.Generic.List<AttachmentDto>(), HasMore = false, Total = 0 });

            Services.AddSingleton(apiMock.Object);
            Services.AddLocalization(options => options.ResourcesPath = "Resources");
            Services.AddSingleton(typeof(IStringLocalizer<Pages>), new PagesStringLocalizer());
            Services.AddSingleton<ICurrentUserService>(new TestCurrentUserService { IsAuthenticated = false });

            var cut = Render<FinanceManager.Web.Components.Pages.CardPage>(parameters => parameters
                .Add(p => p.Kind, "contacts")
                .Add(p => p.Id, id)
            );

            cut.WaitForState(() => cut.FindAll("button").Any(b => b.Id == "Attachments"));

            // Act: click Attachments ribbon button
            var attachBtn = cut.FindAll("button").First(b => b.Id == "Attachments");
            attachBtn.Click();

            // Assert: overlay with drop-upload exists
            cut.WaitForAssertion(() => Assert.True(cut.FindAll(".drop-upload").Count > 0));
        }

        /// <summary>
        /// Verifies that a contact loaded with a full set of non-default values - name, category,
        /// description, the "payment intermediary" flag, and a symbol image - is rendered completely
        /// and in the expected field order, including resolving the category id to its display name
        /// and rendering the symbol as an image pointing at the attachment download endpoint.
        /// </summary>
        [Fact]
        public async Task ContactCard_DisplaysAllFields_WhenContactHasNonDefaultValues()
        {
            // Arrange
            var apiMock = new Mock<IApiClient>();
            var id = Guid.NewGuid();
            var catId = Guid.NewGuid();
            var symbolId = Guid.NewGuid();
            var contact = new ContactDto(id, "Full Name", ContactType.Person, catId, "Some description", true, symbolId);

            apiMock.Setup(a => a.Contacts_GetAsync(id, It.IsAny<System.Threading.CancellationToken>())).ReturnsAsync(contact);
            // return category so ViewModel can resolve category name for display
            apiMock.Setup(a => a.ContactCategories_ListAsync(It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(new System.Collections.Generic.List<ContactCategoryDto> { new ContactCategoryDto(catId, "MyCategory", null) });

            Services.AddSingleton(apiMock.Object);
            Services.AddLocalization(options => options.ResourcesPath = "Resources");
            Services.AddSingleton(typeof(IStringLocalizer<Pages>), new PagesStringLocalizer());
            Services.AddSingleton<ICurrentUserService>(new TestCurrentUserService { IsAuthenticated = false });

            // Act: render CardPage for contact
            var cut = Render<FinanceManager.Web.Components.Pages.CardPage>(parameters => parameters
                .Add(p => p.Kind, "contacts")
                .Add(p => p.Id, id)
            );

            // Wait until inputs are rendered
            cut.WaitForState(() => cut.FindAll(".card-input").Count > 0);

            var inputs = cut.FindAll(".card-input");
            // Expect at least: Name(input), Type(select), Category(input), Description(input), IsPaymentIntermediary(select)
            Assert.True(inputs.Count >= 5, "Expected at least 5 card input elements");

            // Name
            Assert.Equal("Full Name", inputs[0].GetAttribute("value"));

            // Type (select) - value should contain 'Person' (localizer may return resource key)
            Assert.Contains("Person", inputs[1].GetAttribute("value"));

            // Category (should be the 3rd input)
            Assert.Equal("MyCategory", inputs[2].GetAttribute("value"));

            // Description (should be the 4th input according to field order)
            Assert.Equal("Some description", inputs[3].GetAttribute("value"));

            // IsPaymentIntermediary (last select) - should contain 'True' (localizer may return resource key)
            Assert.Contains("Yes", inputs.Last().GetAttribute("value"));

            // Symbol image rendered
            var imgs = cut.FindAll("img");
            Assert.Contains(imgs, img => img.GetAttribute("src")?.Contains($"/api/attachments/{symbolId}/download") == true);
        }

        private sealed class TestCurrentUserService : ICurrentUserService
        {
            public Guid UserId { get; set; } = Guid.NewGuid();
            public string? PreferredLanguage { get; set; }
            public bool IsAuthenticated { get; set; }
            public bool IsAdmin { get; set; }
        }
    }
}
