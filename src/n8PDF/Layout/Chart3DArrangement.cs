using n8PDF.Ooxml;

namespace n8PDF.Layout;

/// <summary>
/// How a three-dimensional plot arranges its bars: the box the scene stands in, and where each
/// bar sits inside it — decided by the grouping, which is the largest behavioural difference
/// from the flat plot.
/// </summary>
/// <remarks>
/// Everything here is measured against Word — the slot probes for the receding case, and
/// <c>chart-3d-depth-axis-probe</c> for the groupings (see <c>Chart3DDepthAxisTests</c>):
///
/// <list type="bullet">
/// <item><b>Only <c>standard</c> puts series in depth</b> (#114, #116): each series is a row of
/// one unit, and a bar fills <c>slot/(1 + gapDepth/100)</c> of its row, centred. <c>clustered</c>
/// puts them side by side across, exactly as the flat plot does; <c>stacked</c> and
/// <c>percentStacked</c> pile them in one row.</item>
/// <item><b>The clustered cluster keeps the flat chart's rule</b>: the bars abut, and together
/// they fill <c>n/(n + gapWidth/100)</c> of their category's slot, centred — confirmed exactly on
/// one, two and three series once the depth lean is subtracted from the union's span.</item>
/// <item><b>The stacked box is the single-row box</b>: one unit wide per category, one deep, and
/// as tall as a one-series chart — <c>floor((categories + 1)/2)</c> units, not the series
/// count's rule. Its pile reaches the sum of the values.</item>
/// <item><b>The clustered box's width is <c>categories·(series + 1)/2</c> units against a
/// one-unit depth.</b> The one-category rule is confirmed to under a per cent against Word by the
/// red bars — the earlier five-per-cent narrowness (1.90 read against 2) was the grey floor
/// outline lying, not the rule: the outline cannot be told from the wall gridlines it shares a
/// colour with. Multi-category boxes read a few per cent wide, but that is not a width-rule error
/// and no change to this value moves it: the oblique fit is <c>min(rectW/extentX, rectH/extentY)</c>,
/// so a wide box is width-bound and fills the plot whatever its <c>WidthUnits</c> — this sets the
/// box's aspect and which side binds, not its rendered width. The residual is confounded between
/// that binding and the bars' own placement, and the one thing that could separate them, the box
/// floor, is unreadable from the raster (#163).</item>
/// </list>
/// </remarks>
internal sealed record Chart3DArrangement(
    double WidthUnits, double DepthUnits, double HeightUnits, int Rows)
{
    /// <summary>The arrangement a chart's grouping asks for.</summary>
    public static Chart3DArrangement For(ChartDefinition chart)
    {
        var categories = Math.Max(1, chart.Categories.Count);
        var series = Math.Max(1, chart.Series.Count);

        // The box's height counts what stands on it. Bars and lines receding count their
        // series (#116, and the two-series line page); an area's receding ribbons add no
        // height — floor((categories+1)/2), read off the two-series area page — but a pile
        // counts what it piles: a stacked ribbon takes floor((categories+series)/2), and a
        // stacked bar the single-series rule.
        var ribbon = chart.Kind is ChartKind.Line or ChartKind.Area;

        return chart.Grouping switch
        {
            ChartGrouping.Standard => new Chart3DArrangement(
                categories, series,
                Math.Floor((categories + (chart.Kind == ChartKind.Area ? 1 : series)) / 2.0),
                series),

            ChartGrouping.Stacked or ChartGrouping.PercentStacked => new Chart3DArrangement(
                categories, 1,
                Math.Floor((categories + (ribbon ? series : 1)) / 2.0), 1),

            // Clustered: one row, the series side by side. The width rule is provisional — see
            // the remarks above — and the height takes (series + 1)/2 unfloored, which the
            // two-series page separates from every floored candidate: its box is 1.5 units tall.
            _ => new Chart3DArrangement(
                categories * (series + 1) / 2.0, 1, (series + 1) / 2.0, 1),
        };
    }

    /// <summary>
    /// Where a bar sits across the box, as fractions of its width: the category's slot, narrowed
    /// by the gap — and under a clustered grouping, this series' share of the cluster.
    /// </summary>
    public (double From, double To) Across(
        ChartDefinition chart, int category, int series)
    {
        var categories = Math.Max(1, chart.Categories.Count);
        var count = Math.Max(1, chart.Series.Count);

        var slotFrom = (double)category / categories;
        var slotWidth = 1.0 / categories;

        if (Rows == 1 && count > 1 && chart.Grouping == ChartGrouping.Clustered)
        {
            var cluster = count / (count + chart.GapWidth / 100.0) * slotWidth;
            var from = slotFrom + (slotWidth - cluster) / 2;

            return (from + cluster * series / count, from + cluster * (series + 1) / count);
        }

        var fills = slotWidth / (1 + chart.GapWidth / 100.0);
        var inset = (slotWidth - fills) / 2;

        return (slotFrom + inset, slotFrom + inset + fills);
    }

    /// <summary>
    /// Where a bar sits into the box, as fractions of its depth: its row's slot, narrowed by
    /// <c>gapDepth</c> — one shared row unless the series recede.
    /// </summary>
    public (double From, double To) Depth(ChartDefinition chart, int series)
    {
        var row = Rows > 1 ? series : 0;
        var slotFrom = (double)row / Rows;
        var slotDepth = 1.0 / Rows;

        var fills = slotDepth / (1 + chart.GapDepth / 100.0);
        var inset = (slotDepth - fills) / 2;

        return (slotFrom + inset, slotFrom + inset + fills);
    }
}
