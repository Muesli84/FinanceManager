using FinanceManager.Application.Notifications;
using FinanceManager.Application.Reports;
using FinanceManager.Domain;
using FinanceManager.Domain.Postings;
using FinanceManager.Infrastructure.Auth;
using FinanceManager.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using Xunit;

namespace FinanceManager.Tests.Integration;

public class AccountsAllReportsTests : IntegrationTestBase, IClassFixture<WebApplicationFactory<Program>>
{
    public AccountsAllReportsTests(WebApplicationFactory<Program> factory) : base(factory) { }

    /// <summary>
    /// Helper that creates an <see cref="IApiClient"/> backed by a test <see cref="HttpClient"/>
    /// where the <see cref="IPostingTimeSeriesService"/> is replaced with the provided mock.
    /// </summary>
    /// <param name="seriesMock">Mock instance to register as <see cref="IPostingTimeSeriesService"/>.</param>
    /// <returns>Api client instance using the test host.</returns>
    private IApiClient CreateApiClientWithMock(Mock<IPostingTimeSeriesService> seriesMock)
    {
        return CreateApiClient(false, services =>
        {
            // Replace posting time series service with our mock
            services.AddSingleton<IPostingTimeSeriesService>(seriesMock.Object);
        });
    }

    /// <summary>
    /// Verifies that the accounts aggregates endpoint returns a sequence of
    /// time series points using the default query parameters (Period="Month", Take=36).
    /// The underlying <see cref="IPostingTimeSeriesService"/> is mocked so this test
    /// validates routing, authentication and JSON serialization of the controller response.
    /// </summary>
    [Fact]
    public async System.Threading.Tasks.Task GetAccountsAggregatesAll_DefaultParams_ShouldReturnPoints()
    {
        var mock = new Mock<IPostingTimeSeriesService>();
        var points = new List<AggregatePointDto>
        {
            new AggregatePointDto(new DateTime(2024,1,1), 100m),
            new AggregatePointDto(new DateTime(2024,2,1), 200m)
        };

        mock.Setup(s => s.GetAllAsync(It.IsAny<Guid>(), PostingKind.Bank, AggregatePeriod.Month, 36, null, It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(points as IReadOnlyList<AggregatePointDto>);

        var api = CreateApiClientWithMock(mock);
        var result = await api.GetAccountsAggregatesAllAsync();

        Assert.NotNull(result);
        Assert.Equal(2, result!.Length);
        Assert.Equal(new DateTime(2024,1,1), result[0].PeriodStart);
        Assert.Equal(100m, result[0].Amount);

        mock.VerifyAll();
    }

    /// <summary>
    /// Verifies that the maxYearsBack query parameter is passed through to the
    /// posting time series service and that the endpoint returns the expected points
    /// when non-default query parameters are provided.
    /// </summary>
    [Fact]
    public async System.Threading.Tasks.Task GetAccountsAggregatesAll_WithMaxYearsBack_ShouldPassParameter()
    {
        var mock = new Mock<IPostingTimeSeriesService>();
        var points = new List<AggregatePointDto>
        {
            new AggregatePointDto(new DateTime(2023,1,1), 50m)
        };

        mock.Setup(s => s.GetAllAsync(It.IsAny<Guid>(), PostingKind.Bank, AggregatePeriod.Month, 12, 3, It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(points as IReadOnlyList<AggregatePointDto>);

        var api = CreateApiClientWithMock(mock);
        var result = await api.GetAccountsAggregatesAllAsync("Month", 12, 3);

        Assert.NotNull(result);
        Assert.Single(result!);
        Assert.Equal(50m, result![0].Amount);

        mock.VerifyAll();
    }
}
