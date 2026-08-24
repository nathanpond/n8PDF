namespace n8PDF.Layout;

/// <summary>
/// The projection Word uses for a 3-D chart when <c>rAngAx="1"</c> — an oblique projection with
/// no rotation in it at all.
/// </summary>
/// <remarks>
/// Everything here is measured, not designed, against Word's own 300 dpi raster of the plot; the
/// instrument is #106's corner finder and the laws hold corner for corner across three probes —
/// see <c>Chart3DObliqueTests</c>. The companion for <c>rAngAx="0"</c> is
/// <see cref="Chart3DProjection"/>, and the two are structurally different projections rather
/// than one with a divide added (#139).
///
/// The pipeline:
///
/// <list type="number">
/// <item>The box, in slot units, exactly as the camera has it: a category is one unit of width, a
/// series one unit of depth scaled by <c>depthPercent</c> (#116, #138), and the height is
/// <c>hPercent</c> where stated — verified here at 50 and 150 — and
/// <c>floor((categories + series)/2)</c> units taken in the plot rectangle's aspect where not
/// (#137, #109).</item>
/// <item>There is no rotation (#140). A scene point lands at
/// <c>(x + z·sin rotY, y + z·sin rotX)</c>: the width axis stays exactly horizontal, the height
/// axis exactly vertical, and only the depth axis leans — measured across both angles swept
/// separately in <c>Chart3DProjectionTests</c>, where a genuine rotation is shown to tilt the
/// width axis by amounts Word does not.</item>
/// <item>The projected scene is fitted to the plot rectangle around a margin that belongs to the
/// scene, not the rectangle: each side of the projected box is padded in <b>scene units
/// proportional to the box's width</b> — halving with two categories on the page that proves it —
/// and the two sides the depth axis leans toward carry an extra share growing with the lean:
/// <code>
/// left   = 2·hx · 0.0098
/// right  = 2·hx · (0.0099 + 0.0210·sin rotY)
/// bottom = 2·hx · 0.0121
/// top    = 2·hx · (0.0056 + 0.0259·sin rotX)
/// </code>
/// The padded extent then fills the rectangle exactly on whichever side binds — the scale is
/// <c>min(rectW/extentX, rectH/extentY)</c>, uniform — and is centred on the other. Word lets the
/// scene overflow nothing here, unlike the camera's overflow behaviour: both constraints are
/// margins-in, not fill-out.</item>
/// </list>
///
/// **The constants are bounded rather than pinned**, and each uses the middle of its interval,
/// the way the cell margin and <c>WrappedLegendInset</c> do: refitting under different margin
/// shapes and page subsets moves the pads within ±0.0006 and the lean shares within ±0.002. The
/// residual against Word is 0.31pt at worst over the probe (median 0.24pt) with the linear-in-sine
/// shares; <c>tan(rot/2)</c> and <c>1−cos</c> shapes land within a hundredth of a point of the
/// same floor, so the data cannot tell the families apart — sine is used as the same function the
/// lean itself follows. The floor is consistent with Word rasterising the scene to its own
/// 1/300 inch pixel grid, which moves a corner up to 0.17pt before any law is asked about it.
///
/// **The verified domain** is rotX 5..60, rotY 5..65, both positive, depth 20..500 percent,
/// counts to three, and hPercent 50..150. Outside it the formulas extrapolate continuously.
/// </remarks>
internal sealed class Chart3DObliqueProjection : IChart3DProjection
{
    private const double LeftPad = 0.0098;
    private const double RightPad = 0.0099;
    private const double RightLean = 0.0210;
    private const double BottomPad = 0.0121;
    private const double TopPad = 0.0056;
    private const double TopLean = 0.0259;

    /// <summary>
    /// How far short of the box's top a bar reaching the value-axis maximum stops, as a share of
    /// the box's height.
    /// </summary>
    /// <remarks>
    /// Measured over bars of 30, 60, 90 and 100 on an axis of 100: the bar's height is
    /// <c>(value/max − 0.0061)</c> of the box on every page, a constant share of the <b>box</b>
    /// rather than of the value — 30 reads 0.294, 60 reads 0.594, 100 reads 0.994, all
    /// ±0.002. Whoever draws the boxes (#101) applies it to the bar's top and to nothing else;
    /// the box itself, and the walls with it, stand at the full height.
    /// </remarks>
    public const double BarTopShortfall = 0.0061;

    private readonly double _sinA, _sinB;
    private readonly double _hx, _hy, _hz;
    private readonly double _scale, _cx, _cy;

    /// <param name="rotX">The stated tilt, in degrees.</param>
    /// <param name="rotY">The stated turn, in degrees.</param>
    /// <param name="depthPercent">The stated <c>c:depthPercent</c>.</param>
    /// <param name="hPercent">The stated <c>c:hPercent</c>, or null for the absence (#109).</param>
    /// <param name="categories">The box's width in slot units — the category count, until a
    /// grouping says otherwise (see <c>Chart3DArrangement</c>).</param>
    /// <param name="series">Its depth in slot units, before <paramref name="depthPercent"/>.</param>
    /// <param name="heightUnits">
    /// The box's height in units where the document states no <c>hPercent</c>, or null for the
    /// standard rule, <c>floor((width + depth)/2)</c>. A grouping that collapses the rows can
    /// make the height count something other than what stands in the scene (#100).
    /// </param>
    /// <param name="rectLeft">The plot rectangle, in page points.</param>
    /// <param name="rectTop">Its top.</param>
    /// <param name="rectWidth">Its width.</param>
    /// <param name="rectHeight">Its height.</param>
    public Chart3DObliqueProjection(
        double rotX, double rotY, double depthPercent, double? hPercent,
        double categories, double series,
        double rectLeft, double rectTop, double rectWidth, double rectHeight,
        double? heightUnits = null, double? marginUnits = null)
    {
        _sinA = Math.Sin(rotX * Math.PI / 180);
        _sinB = Math.Sin(rotY * Math.PI / 180);

        _hx = categories / 2.0;
        _hz = series * depthPercent / 100 / 2;
        _hy = hPercent is { } stated
            ? categories * stated / 100 / 2
            : (heightUnits ?? Math.Floor((categories + series) / 2.0)) * (rectHeight / rectWidth) / 2;

        // The pads are proportional to the category direction's units — the box's width when
        // the bars stand, and still the category count when they lie (#101's lying pages).
        var pad = marginUnits ?? categories;

        var xMin = -_hx - _hz * _sinB - pad * LeftPad;
        var xMax = _hx + _hz * _sinB + pad * (RightPad + RightLean * _sinB);
        var yMin = -_hy - _hz * _sinA - pad * BottomPad;
        var yMax = _hy + _hz * _sinA + pad * (TopPad + TopLean * _sinA);

        _scale = Math.Min(rectWidth / (xMax - xMin), rectHeight / (yMax - yMin));
        _cx = rectLeft + rectWidth / 2 - _scale * (xMin + xMax) / 2;
        _cy = rectTop + rectHeight / 2 + _scale * (yMin + yMax) / 2;
    }

    /// <summary>
    /// Where a scene point lands on the page, in points from the page's top-left.
    /// </summary>
    /// <param name="x">Across the box, 0 at its left face and 1 at its right.</param>
    /// <param name="y">Up the box, 0 at its floor and 1 at its top.</param>
    /// <param name="z">Into the box, 0 at its front face and 1 at its back.</param>
    public (double X, double Y) Project(double x, double y, double z)
    {
        var sx = (x - 0.5) * 2 * _hx;
        var sy = (y - 0.5) * 2 * _hy;
        var sz = (z - 0.5) * 2 * _hz;

        return (_cx + _scale * (sx + sz * _sinB), _cy - _scale * (sy + sz * _sinA));
    }
}
