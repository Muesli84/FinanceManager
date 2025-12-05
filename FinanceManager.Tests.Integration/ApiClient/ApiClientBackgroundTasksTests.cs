using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FinanceManager.Tests.Integration.ApiClient;

public class ApiClientBackgroundTasksTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ApiClientBackgroundTasksTests(TestWebApplicationFactory factory)
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

    [Fact]
    public async Task Enqueue_RebuildAggregates_ShouldReturnTaskInfo_AndStatusEndpointsWork()
    {
        var api = CreateClient();
        var username = $"user_{Guid.NewGuid():N}";
        // authenticate by registering
        await api.Auth_RegisterAsync(new RegisterRequest(username, "Secret123", null, null));

        // Enqueue via generic endpoint
        var info = await api.BackgroundTasks_EnqueueAsync(BackgroundTaskType.RebuildAggregates, allowDuplicate: false);
        info.Should().NotBeNull();
        info.Type.Should().Be(BackgroundTaskType.RebuildAggregates);
        info.Status.Should().BeOneOf(BackgroundTaskStatus.Queued, BackgroundTaskStatus.Running);

        // Active list should contain our task
        var active = await api.BackgroundTasks_GetActiveAsync();
        active.Should().NotBeNull();
        active.Should().Contain(x => x.Id == info.Id);

        // Detail should return the same
        var detail = await api.BackgroundTasks_GetDetailAsync(info.Id);
        detail.Should().NotBeNull();
        detail!.Id.Should().Be(info.Id);

        // Aggregates specialized endpoint should return Accepted status with DTO
        var agg = await api.Aggregates_RebuildAsync(allowDuplicate: false);
        agg.Running.Should().BeTrue();

        // Status endpoint should return running true or false depending on timing
        var status = await api.Aggregates_GetRebuildStatusAsync();
        status.Should().NotBeNull();
    }

    [Fact]
    public async Task CancelOrRemove_ShouldReturnNoContentOrFalse()
    {
        var api = CreateClient();
        var username = $"user_{Guid.NewGuid():N}";
        await api.Auth_RegisterAsync(new RegisterRequest(username, "Secret123", null, null));

        var info = await api.BackgroundTasks_EnqueueAsync(BackgroundTaskType.RebuildAggregates);
        info.Should().NotBeNull();

        // Try cancel/remove depending on status
        var ok = await api.BackgroundTasks_CancelOrRemoveAsync(info.Id);
        // Controller returns 204 for success, 400/404 for failure -> client maps to false on failure
        ok.Should().BeTrue();

        // Subsequent detail may be null if removed while queued
        var after = await api.BackgroundTasks_GetDetailAsync(info.Id);
        // Allow both outcomes depending on race (null if removed, info if transitioned to running quickly)
        after.Should().BeNull();
    }
}
