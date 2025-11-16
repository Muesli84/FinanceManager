using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;
using FinanceManager.Domain.Notifications;
using FinanceManager.Application.Notifications;
using System.Threading;
using Microsoft.Data.Sqlite;
using FinanceManager.Infrastructure; // for AppDbContext
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using FinanceManager.Infrastructure.Auth;
using System.Collections.Generic; // added
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting; // added
using FinanceManager.Web.Services; // use ApiClient from Web project

namespace FinanceManager.Tests.Integration;

public class MetaHolidayRoutesTests : IntegrationTestBase, IClassFixture<WebApplicationFactory<Program>>
{
    public MetaHolidayRoutesTests(WebApplicationFactory<Program> factory) : base(factory) { }

    private IApiClient CreateApiClientWithMocks(Mock<IHolidaySubdivisionService> subdivisionMock)
    {
        var api = CreateApiClient(true, services =>
        {
            services.AddSingleton<IHolidaySubdivisionService>(subdivisionMock.Object);
        });
        return api;
    }

    private IApiClient CreateApiClient()
    {
        return CreateApiClient();
    }

    [Fact]
    public async Task HolidayCountries_ShouldReturn200AndList()
    {
        var api = CreateApiClient();
        var list = await api.GetHolidayCountriesAsync();
        Assert.NotNull(list);
        Assert.Contains("DE", list!);
    }

    [Fact]
    public async Task HolidayProviders_ShouldReturnEnumNames()
    {
        var api = CreateApiClient();
        var providers = await api.GetHolidayProvidersAsync();
        Assert.Contains(nameof(HolidayProviderKind.NagerDate), providers!);
    }

    [Fact]
    public async Task HolidaySubdivisions_ShouldCallService()
    {
        var mock = new Mock<IHolidaySubdivisionService>();
        mock.Setup(s => s.GetSubdivisionsAsync(HolidayProviderKind.Memory, "DE", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "BY", "BE" });

        var api = CreateApiClientWithMocks(mock);
        var list = await api.GetHolidaySubdivisionsAsync("Memory", "DE");
        Assert.Equal(new[] { "BY", "BE" }, list);
        mock.VerifyAll();
    }
}
