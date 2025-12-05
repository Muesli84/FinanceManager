using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace FinanceManager.Tests.Integration.ApiClient;

public class ApiClientBackupsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ApiClientBackupsTests(TestWebApplicationFactory factory)
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
    public async Task List_InitiallyEmpty_Create_AddsEntry()
    {
        var api = CreateClient();
        await EnsureAuthenticatedAsync(api);

        var list1 = await api.Backups_ListAsync();
        list1.Should().NotBeNull();
        list1.Should().BeEmpty();

        var created = await api.Backups_CreateAsync();
        created.Should().NotBeNull();
        created.FileName.Should().NotBeNullOrEmpty();

        var list2 = await api.Backups_ListAsync();
        list2.Should().NotBeNull();
        list2.Should().NotBeEmpty();
        list2.Any(b => b.Id == created.Id).Should().BeTrue();
    }

    [Fact]
    public async Task Upload_AllowsZip_AndNdjson()
    {
        var api = CreateClient();
        await EnsureAuthenticatedAsync(api);
        // upload NDJSON content (auto-wrapped to zip)
        await using var ndjson = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{\"Type\":\"Backup\",\"Version\":3}\n{}"));
        var up1 = await api.Backups_UploadAsync(ndjson, "backup.ndjson");
        up1.Should().NotBeNull();
        up1.FileName.Should().EndWith(".zip");

        // upload zip content
        await using var zipStream = new MemoryStream(new byte[] { 0x50, 0x4B, 0x03, 0x04 });
        var up2 = await api.Backups_UploadAsync(zipStream, "custom.zip");
        up2.Should().NotBeNull();
        up2.FileName.Should().Be("custom.zip");

        var list = await api.Backups_ListAsync();
        list.Should().HaveCountGreaterOrEqualTo(2);
    }

    [Fact]
    public async Task Download_ReturnsStream_AndDelete_Removes()
    {
        var api = CreateClient();
        await EnsureAuthenticatedAsync(api);
        var created = await api.Backups_CreateAsync();
        var stream = await api.Backups_DownloadAsync(created.Id);
        stream.Should().NotBeNull();
        stream!.Length.Should().BeGreaterThan(0);

        var deleted = await api.Backups_DeleteAsync(created.Id);
        deleted.Should().BeTrue();

        var streamMissing = await api.Backups_DownloadAsync(created.Id);
        streamMissing.Should().BeNull();
    }

    [Fact]
    public async Task StartApply_Status_Cancel_Flow()
    {
        var api = CreateClient();
        await EnsureAuthenticatedAsync(api);
        var created = await api.Backups_CreateAsync();

        var status = await api.Backups_StartApplyAsync(created.Id);
        status.Running.Should().BeTrue();

        var polled = await api.Backups_GetStatusAsync();
        polled.Should().NotBeNull();

        var canceled = await api.Backups_CancelAsync();
        canceled.Should().BeTrue();
    }
}
