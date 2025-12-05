using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FinanceManager.Tests.Integration.ApiClient;

public class ApiClientSavingsPlansTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ApiClientSavingsPlansTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private FinanceManager.Shared.ApiClient CreateClient()
    {
        var http = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        return new FinanceManager.Shared.ApiClient(http);
    }

    private async Task EnsureAuthenticatedAsync(FinanceManager.Shared.ApiClient api)
    {
        var username = $"user_{Guid.NewGuid():N}";
        await api.Auth_RegisterAsync(new RegisterRequest(username, "Secret123", PreferredLanguage: null, TimeZoneId: null));
    }

    [Fact]
    public async Task SavingsPlans_Flow_CRUD_Analysis_Symbols()
    {
        var api = CreateClient();
        await EnsureAuthenticatedAsync(api);

        // initial list and count
        var list = await api.SavingsPlans_ListAsync();
        list.Should().NotBeNull();
        var cnt = await api.SavingsPlans_CountAsync();
        cnt.Should().BeGreaterThanOrEqualTo(0);

        // create
        var createReq = new SavingsPlanCreateRequest(
            Name: "Plan A",
            Type: SavingsPlanType.OneTime,
            TargetAmount: 100m,
            TargetDate: DateTime.UtcNow.Date.AddMonths(6),
            Interval: null,
            CategoryId: null,
            ContractNumber: null
        );
        var created = await api.SavingsPlans_CreateAsync(createReq);
        created.Should().NotBeNull();
        created!.Name.Should().Be("Plan A");

        // get
        var got = await api.SavingsPlans_GetAsync(created.Id);
        got.Should().NotBeNull();
        got!.Id.Should().Be(created.Id);

        // update
        var updateReq = new SavingsPlanCreateRequest(
            Name: "Plan B",
            Type: SavingsPlanType.Recurring,
            TargetAmount: 200m,
            TargetDate: DateTime.UtcNow.Date.AddMonths(12),
            Interval: SavingsPlanInterval.Monthly,
            CategoryId: null,
            ContractNumber: "CN-123"
        );
        var updated = await api.SavingsPlans_UpdateAsync(created.Id, updateReq);
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("Plan B");
        updated!.ContractNumber.Should().Be("CN-123");

        // analyze
        var analysis = await api.SavingsPlans_AnalyzeAsync(created.Id);
        analysis.Should().NotBeNull();
        analysis!.PlanId.Should().Be(created.Id);

        // archive then delete
        var archived = await api.SavingsPlans_ArchiveAsync(created.Id);
        archived.Should().BeTrue();
        var deleted = await api.SavingsPlans_DeleteAsync(created.Id);
        deleted.Should().BeTrue();
        var gone = await api.SavingsPlans_GetAsync(created.Id);
        gone.Should().BeNull();
    }
}
