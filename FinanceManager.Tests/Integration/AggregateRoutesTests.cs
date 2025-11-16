using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using FinanceManager.Web.Services;
using FinanceManager.Application.Reports;
using FinanceManager.Domain.Postings;
using FinanceManager.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace FinanceManager.Tests.Integration;

public class AggregateRoutesTests : IntegrationTestBase, IClassFixture<WebApplicationFactory<Program>>
{
    public AggregateRoutesTests(WebApplicationFactory<Program> factory) : base(factory) { }

    private IApiClient CreateApiClientWithSeriesMock(Mock<IPostingTimeSeriesService> mock)
    {
        return CreateApiClient(false, services =>
        {
            services.AddSingleton<IPostingTimeSeriesService>(mock.Object);
        });
    }

    [Fact]
    public async Task AccountAggregates_ShouldCallService()
    {
        var mock = new Mock<IPostingTimeSeriesService>();
        var points = new List<AggregatePointDto> { new AggregatePointDto(new DateTime(2024,1,1), 10m) };
        mock.Setup(s => s.GetAsync(It.IsAny<Guid>(), PostingKind.Bank, It.IsAny<Guid>(), AggregatePeriod.Month, 36, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid u, PostingKind k, Guid id, AggregatePeriod p, int t, int? y, CancellationToken ct) => points);

        var api = CreateApiClientWithSeriesMock(mock);
        var accountId = Guid.NewGuid();
        var res = await api.GetAccountAggregatesAsync(accountId);
        Assert.NotNull(res);
        Assert.Single(res);
        Assert.Equal(10m, res![0].Amount);
        mock.VerifyAll();
    }

    [Fact]
    public async Task ContactAggregates_ShouldCallService()
    {
        var mock = new Mock<IPostingTimeSeriesService>();
        var points = new List<AggregatePointDto> { new AggregatePointDto(new DateTime(2024,2,1), 20m) };
        mock.Setup(s => s.GetAsync(It.IsAny<Guid>(), PostingKind.Contact, It.IsAny<Guid>(), AggregatePeriod.Month, 36, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(points);

        var api = CreateApiClientWithSeriesMock(mock);
        var id = Guid.NewGuid();
        var res = await api.GetContactAggregatesAsync(id);
        Assert.NotNull(res);
        Assert.Single(res);
        Assert.Equal(20m, res![0].Amount);
        mock.VerifyAll();
    }

    [Fact]
    public async Task SavingsPlanSingleAggregates_ShouldCallService()
    {
        var mock = new Mock<IPostingTimeSeriesService>();
        var points = new List<AggregatePointDto> { new AggregatePointDto(new DateTime(2024,3,1), 30m) };
        mock.Setup(s => s.GetAsync(It.IsAny<Guid>(), PostingKind.SavingsPlan, It.IsAny<Guid>(), AggregatePeriod.Month, 36, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(points);

        var api = CreateApiClientWithSeriesMock(mock);
        var id = Guid.NewGuid();
        var res = await api.GetSavingsPlanAggregatesAsync(id);
        Assert.NotNull(res);
        Assert.Single(res);
        Assert.Equal(30m, res![0].Amount);
        mock.VerifyAll();
    }

    [Fact]
    public async Task SavingsPlansAllAggregates_ShouldCallService()
    {
        var mock = new Mock<IPostingTimeSeriesService>();
        var points = new List<AggregatePointDto> { new AggregatePointDto(new DateTime(2024,4,1), 40m) };
        mock.Setup(s => s.GetAllAsync(It.IsAny<Guid>(), PostingKind.SavingsPlan, AggregatePeriod.Month, 36, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(points);

        var api = CreateApiClientWithSeriesMock(mock);
        var res = await api.GetSavingsPlansAggregatesAllAsync();
        Assert.NotNull(res);
        Assert.Single(res);
        Assert.Equal(40m, res![0].Amount);
        mock.VerifyAll();
    }

    [Fact]
    public async Task SecurityAggregates_ShouldCallService()
    {
        var mock = new Mock<IPostingTimeSeriesService>();
        var points = new List<AggregatePointDto> { new AggregatePointDto(new DateTime(2024,5,1), 50m) };
        mock.Setup(s => s.GetAsync(It.IsAny<Guid>(), PostingKind.Security, It.IsAny<Guid>(), AggregatePeriod.Month, 36, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(points);

        var api = CreateApiClientWithSeriesMock(mock);
        var id = Guid.NewGuid();
        var res = await api.GetSecurityAggregatesAsync(id);
        Assert.NotNull(res);
        Assert.Single(res);
        Assert.Equal(50m, res![0].Amount);
        mock.VerifyAll();
    }
}
