using FinanceManager.Application;
using FinanceManager.Shared;
using FinanceManager.Shared.Dtos.Postings;
using FinanceManager.Web.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace FinanceManager.Tests.ViewModels;

public sealed class PostingDetailViewModelTests
{
    private sealed class TestCurrentUserService : ICurrentUserService
    {
        public Guid UserId { get; set; } = Guid.NewGuid();
        public string? PreferredLanguage { get; set; }
        public bool IsAuthenticated { get; set; } = true;
        public bool IsAdmin { get; set; }
    }

    private static (PostingDetailViewModel vm, Mock<IApiClient> apiMock) CreateVm(bool authenticated = true)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUserService>(new TestCurrentUserService { IsAuthenticated = authenticated });
        var apiMock = new Mock<IApiClient>();
        services.AddSingleton(apiMock.Object);
        var sp = services.BuildServiceProvider();
        var vm = new PostingDetailViewModel(sp);
        return (vm, apiMock);
    }

    [Fact]
    public async Task Initialize_LoadsDetail_ResolvesLinks()
    {
        var (vm, apiMock) = CreateVm();
        var id = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var linkedAccountId = Guid.NewGuid();

        var posting = new PostingServiceDto(
            Id: id,
            BookingDate: DateTime.UtcNow.Date,
            ValutaDate: DateTime.UtcNow.Date,
            Amount: 123.45m,
            Kind: PostingKind.Bank,
            AccountId: null,
            ContactId: null,
            SavingsPlanId: null,
            SecurityId: null,
            SourceId: Guid.NewGuid(),
            Subject: "Subj",
            RecipientName: "Rec",
            Description: "Desc",
            SecuritySubType: null,
            Quantity: null,
            GroupId: groupId,
            LinkedPostingId: null,
            LinkedPostingKind: null,
            LinkedPostingAccountId: null,
            LinkedPostingAccountSymbolAttachmentId: null,
            LinkedPostingAccountName: null,
            BankPostingAccountId: null,
            BankPostingAccountSymbolAttachmentId: null,
            BankPostingAccountName: null);

        var links = new GroupLinksDto(linkedAccountId, null, null, null);

        apiMock.Setup(a => a.Postings_GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(posting);
        apiMock.Setup(a => a.Postings_GetGroupLinksAsync(groupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(links);

        vm.Configure(id);
        await vm.InitializeAsync();

        Assert.False(vm.Loading);
        Assert.NotNull(vm.Detail);
        Assert.Equal(linkedAccountId, vm.LinkedAccountId);
        Assert.False(vm.LinksLoading);
    }
}
