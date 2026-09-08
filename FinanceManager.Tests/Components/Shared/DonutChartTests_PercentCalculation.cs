using Bunit;
using FinanceManager.Tests.TestHelpers;
using FinanceManager.Web.Components.Shared;
using static FinanceManager.Tests.TestHelpers.CultureTestHelper;

namespace FinanceManager.Tests.Components.Shared;

/// <summary>
/// Tests for <see cref="DonutChart"/>'s percent calculation performed in <c>OnParametersSet</c>,
/// including how zero-value and all-zero-total slices are handled.
/// </summary>
public sealed class DonutChartTests_PercentCalculation : BunitContext
{
    /// <summary>
    /// With three positive slices, each slice's share of the total is rendered in the legend as a percentage,
    /// and one donut segment is drawn per slice.
    /// </summary>
    [Fact]
    public void OnParametersSet_ThreePositiveSlices_ComputesProportionalPercentagesAndRendersSegments()
    {
        WithInvariantCulture(() =>
        {
            var slices = new List<DonutChart.DonutChartSlice>
            {
                new("A", 50m),
                new("B", 30m),
                new("C", 20m)
            };

            var cut = Render<DonutChart>(builder => builder.Add(c => c.Slices, slices));

            var legendValues = cut.FindAll(".donut-legend-value").Select(e => e.TextContent).ToList();
            Assert.Equal(["50.0%", "30.0%", "20.0%"], legendValues);
            Assert.Equal(3, cut.FindAll(".donut-segment").Count);
        });
    }

    /// <summary>
    /// A slice with a zero value contributes 0% and is listed in the legend but does not get a rendered
    /// donut segment (segments are only drawn for entries with a positive percent).
    /// </summary>
    [Fact]
    public void OnParametersSet_SliceWithZeroValue_RendersZeroPercentWithoutSegment()
    {
        WithInvariantCulture(() =>
        {
            var slices = new List<DonutChart.DonutChartSlice>
            {
                new("A", 100m),
                new("Zero", 0m)
            };

            var cut = Render<DonutChart>(builder => builder.Add(c => c.Slices, slices));

            var legendValues = cut.FindAll(".donut-legend-value").Select(e => e.TextContent).ToList();
            Assert.Equal(["100.0%", "0.0%"], legendValues);
            Assert.Single(cut.FindAll(".donut-segment"));
        });
    }

    /// <summary>
    /// Optional preformatted legend values and details do not change percentage geometry for existing callers.
    /// </summary>
    [Fact]
    public void OnParametersSet_OptionalLegendValue_RendersCustomLegendWithoutChangingSegments()
    {
        WithInvariantCulture(() =>
        {
            var slices = new List<DonutChart.DonutChartSlice>
            {
                new("A", 75m, "$75.00 · 75.0%", ["Positive: $75.00"], "A aria", "a"),
                new("B", 25m)
            };

            var cut = Render<DonutChart>(builder => builder.Add(c => c.Slices, slices));

            var legendValues = cut.FindAll(".donut-legend-value").Select(e => e.TextContent).ToList();
            Assert.Equal(["$75.00 · 75.0%", "25.0%"], legendValues);
            Assert.Contains("Positive: $75.00", cut.Markup);
            Assert.Equal(2, cut.FindAll(".donut-segment").Count);
            Assert.Equal("a", cut.Find("[data-statistics-group-key=\"a\"]").GetAttribute("data-statistics-group-key"));
        });
    }

    /// <summary>
    /// When every slice has a zero value, the total is zero; the component must not divide by zero and
    /// instead render every slice at 0% without throwing and without any donut segment.
    /// </summary>
    [Fact]
    public void OnParametersSet_AllSlicesZero_RendersAllZeroPercentagesWithoutThrowing()
    {
        WithInvariantCulture(() =>
        {
            var slices = new List<DonutChart.DonutChartSlice>
            {
                new("A", 0m),
                new("B", 0m)
            };

            var cut = Render<DonutChart>(builder => builder.Add(c => c.Slices, slices));

            var legendValues = cut.FindAll(".donut-legend-value").Select(e => e.TextContent).ToList();
            Assert.Equal(["0.0%", "0.0%"], legendValues);
            Assert.Empty(cut.FindAll(".donut-segment"));
        });
    }

    /// <summary>
    /// With no slices at all, the legend list is omitted entirely rather than rendered empty.
    /// </summary>
    [Fact]
    public void OnParametersSet_NoSlices_DoesNotRenderLegend()
    {
        var cut = Render<DonutChart>(builder => builder.Add(c => c.Slices, Array.Empty<DonutChart.DonutChartSlice>()));

        Assert.Empty(cut.FindAll(".donut-legend"));
    }
}
