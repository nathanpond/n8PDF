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
/// <item><b>The clustered box's proportions are bounded, not pinned.</b> The best fit puts its
/// width at <c>categories·(series + 1)/2</c> units against a one-unit depth — exact on one
/// category with three series (1.90 measured), three per cent narrow on two series (1.42
/// against 1.5), and ten per cent narrow on two categories (3.61 against 4). That spread is
/// real and unexplained; the follow-up issue holds the measurements, and this rule is the
/// middle of what they allow.</item>
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

        return chart.Grouping switch
        {
            ChartGrouping.Standard => new Chart3DArrangement(
                categories, series, Math.Floor((categories + series) / 2.0), series),

            ChartGrouping.Stacked or ChartGrouping.PercentStacked => new Chart3DArrangement(
                categories, 1, Math.Floor((categories + 1) / 2.0), 1),

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
