using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FinanceManager.Web.Services;
using FinanceManager.Shared.Dtos;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace FinanceManager.Tests.Integration;

public class DividendsReportsTests : IntegrationTestBase, IClassFixture<WebApplicationFactory<Program>>
{
    public DividendsReportsTests(WebApplicationFactory<Program> factory) : base(factory) { }

    [Fact]
    public async Task SecuritiesDividends_ShouldReturnPoints()
    {
        var client = CreateHttpClient();
        var api = new ApiClient(client);

        // No authentication/user context manipulation here; expectation is that endpoint returns data for test DB.
        var res = await api.GetSecuritiesDividendsAsync();
        Assert.NotNull(res);
    }

    [Fact]
    public async Task SecuritiesDividends_ApiMethod_MockedService()
    {
        var mock = new Mock<FinanceManager.Application.Reports.IPostingTimeSeriesService>();
        var points = new List<FinanceManager.Application.Reports.AggregatePointDto>
        {
            new FinanceManager.Application.Reports.AggregatePointDto(new DateTime(2024,1,1), 123m)
        };

        // Ensure the mock returns a realistic (non-null) result for GetDividendsAsync.
        mock.Setup(s => s.GetDividendsAsync(It.IsAny<Guid>(), It.IsAny<FinanceManager.Domain.Postings.AggregatePeriod>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(points as IReadOnlyList<FinanceManager.Application.Reports.AggregatePointDto>);

        // We expect controller to query DB directly for securities and postings, not via IPostingTimeSeriesService.
        // But we still verify that calling the client method returns expected shape from API.

        var api = CreateApiClient(true, services =>
        {
            services.AddSingleton(mock.Object);
        });

        var res = await api.GetSecuritiesDividendsAsync();
        Assert.NotNull(res);
        Assert.Single(res);
        Assert.Equal(123m, res![0].Amount);
    }
}
