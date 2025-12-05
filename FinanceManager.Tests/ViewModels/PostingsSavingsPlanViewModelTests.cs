using FinanceManager.Application;
using FinanceManager.Shared;
using FinanceManager.Shared.Dtos.Postings;
using FinanceManager.Web.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace FinanceManager.Tests.ViewModels;

public sealed class PostingsSavingsPlanViewModelTests
{
    private sealed class TestCurrentUserService : ICurrentUserService
    {
        public Guid UserId { get; set; } = Guid.NewGuid();
        public string? PreferredLanguage { get; set; }
        public bool IsAuthenticated { get; set; } = true;
        public bool IsAdmin { get; set; }
    }

    private static (PostingsSavingsPlanViewModel vm, Mock<IApiClient> apiMock) CreateVm(bool authenticated = true)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUserService>(new TestCurrentUserService { IsAuthenticated = authenticated });
        var apiMock = new Mock<IApiClient>();
        services.AddSingleton(apiMock.Object);
        var sp = services.BuildServiceProvider();
        var vm = new PostingsSavingsPlanViewModel(sp);
        return (vm, apiMock);
    }

    private static List<PostingServiceDto> CreatePostings(int count)
    {
        return Enumerable.Range(0, count)
            .Select(i => new PostingServiceDto(
                Id: Guid.NewGuid(),
                BookingDate: DateTime.UtcNow.Date.AddDays(-i),
                ValutaDate: DateTime.UtcNow.Date.AddDays(-i),
                Amount: 100 + i,
                Kind: PostingKind.SavingsPlan,
                AccountId: Guid.NewGuid(),
                ContactId: null,
                SavingsPlanId: Guid.NewGuid(),
                SecurityId: null,
                SourceId: Guid.NewGuid(),
                Subject: $"S{i}",
                RecipientName: null,
                Description: $"D{i}",
                SecuritySubType: null,
                Quantity: null,
                GroupId: Guid.NewGuid(),
                LinkedPostingId: null,
                LinkedPostingKind: null,
                LinkedPostingAccountId: null,
                LinkedPostingAccountSymbolAttachmentId: null,
                LinkedPostingAccountName: null,
                BankPostingAccountId: null,
                BankPostingAccountSymbolAttachmentId: null,
                BankPostingAccountName: null))
            .ToList();
    }

    [Fact]
    public async Task Initialize_LoadsFirstPage_SetsItemsAndFlags()
    {
        var (vm, apiMock) = CreateVm();
        apiMock.Setup(a => a.Postings_GetSavingsPlanAsync(It.IsAny<Guid>(), 0, 50, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePostings(15));

        vm.Configure(Guid.NewGuid());
        await vm.InitializeAsync();

        Assert.False(vm.Loading);
        Assert.Equal(15, vm.Items.Count);
        Assert.True(vm.CanLoadMore);
    }

    [Fact]
    public async Task LoadMore_AppendsItems_StopsWhenBelowPageSize()
    {
        var (vm, apiMock) = CreateVm();
        int call = 0;
        apiMock.Setup(a => a.Postings_GetSavingsPlanAsync(It.IsAny<Guid>(), It.IsAny<int>(), 50, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                call++;
                return CreatePostings(call == 1 ? 50 : 1);
            });

        vm.Configure(Guid.NewGuid());
        await vm.InitializeAsync();
        Assert.Equal(50, vm.Items.Count);
        Assert.True(vm.CanLoadMore);

        await vm.LoadMoreAsync();
        Assert.Equal(51, vm.Items.Count);
        Assert.False(vm.CanLoadMore);
    }

    [Fact]
    public void GetExportUrl_ComposesQuery()
    {
        var (vm, _) = CreateVm();
        vm.Configure(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        vm.SetSearch("abc def");

        var url = vm.GetExportUrl("csv");

        Assert.StartsWith("/api/postings/savings-plan/11111111-1111-1111-1111-111111111111/export", url);
        Assert.Contains("format=csv", url);
        Assert.Contains("q=abc%20def", url);
    }
}
