namespace n8PDF.Layout;

/// <summary>
/// The projection Word uses for a 3-D chart when <c>rAngAx="0"</c> — a camera with a genuinely
/// finite eye, honouring <c>c:perspective</c>.
/// </summary>
/// <remarks>
/// Everything here is measured, not designed. The measurements are #98's and its instrument is
/// #106's corner finder; the laws below reproduce Word's silhouettes corner for corner, on pages
/// fitted and on pages held back, to a quarter of a point — see <c>Chart3DCameraTests</c>.
///
/// The pipeline:
///
/// <list type="number">
/// <item>The box, in slot units: a category is one unit of width, a series one unit of depth
/// (#116, scaled by <c>depthPercent</c>, #138), and the height is <c>hPercent</c> where stated
/// (#109) and <c>floor((categories + series)/2)</c> units taken in the plot rectangle's aspect
/// where not (#137).</item>
/// <item>Turn about the vertical by <c>rotY</c>, then about the horizontal by minus <c>rotX</c>
/// (#140 — read at <c>perspective</c> nought, where the divide vanishes).</item>
/// <item><c>c:perspective</c> is a field of view in half-degrees: the camera's vertical
/// half-angle is <c>perspective/4</c> degrees. The eye backs away until the scene fills 0.9702
/// of the frustum — measured through the rotated box's world extents, height against the
/// frustum's height and width against its width, whichever asks for the greater distance. The
/// 0.9702 is the same fill #116 measured for the scene in its rectangle, and the frustum's
/// aspect is the plot rectangle's. At strong perspective a third constraint takes over and the
/// eye follows <c>D = 1.0306·cosA·((floor depth extent) + hy·cot(θ))</c> — the frustum's
/// half-height at the near floor edge held at the box's half-height, the whole foreshortened by
/// the tilt <c>cosA</c> (#141).</item>
/// <item>The eye does not sit on the axis. Its offsets are, with <c>A = rotX</c>,
/// <c>B = rotY</c> and <c>W/H</c> the plot rectangle's aspect:
/// <code>
/// ex = tan(θ) · (W/H) · cosA · (hx·sinB − hz·cosB)
/// ey = −tan(θ) · 1.0306 · (hx·sinB·cosA + 0.9702·hz·cosB·cosA − hy·sinA)
/// </code>
/// both verified on held-back pages to a third of a percent.</item>
/// <item>The frustum is the plot rectangle: a projected point lands at
/// <c>rectCentre + s·(q − eye)</c> with <c>s = (rectHeight/2)/(D·tanθ)</c>. The vanishing
/// centre sits at the rectangle's centre on every page measured.</item>
/// </list>
///
/// **The frustum-fit regime is verified to a quarter point.** That covers every perspective at
/// Word's defaults up to about 60, and all perspectives for scenes whose extents keep the frustum
/// constraints binding. Where the near-floor constraint takes over instead (deep perspective on a
/// mild scene) the eye leaves the frustum-branch offset laws, and #141 measured what it does
/// instead: for rotX and rotY both inside 45° — every deep scene Word's UI can reach, its
/// perspective capping at 100 — the deep offset laws below take over and bring the corners to
/// well under a point (a fraction of a point at low rotX, ~0.4pt at 15/20 perspective 80). What
/// still keeps that regime off the quarter-point bar is the eye distance: the floor law is within
/// a tenth of a percent at rotX 15 and 22, but its slope biases by rotX 30, and past perspective
/// 200 the ex law turns slightly concave — both open on #141. Beyond 45° a third regime begins
/// whose offsets are still open, so there the eye stays clamped at the branch boundary, stable and
/// close rather than exact.
/// </remarks>
internal sealed class Chart3DProjection : IChart3DProjection
{
    private const double Fill = 0.9702;
    private const double FloorScale = 1.0306;

    // The width bound fills slightly less of the frustum than the height bound does: the
    // effective width fraction is Fill·WidthFill. Settled by #141 — the width-bound probe pages
    // (single- and two-category) agree on this to ±0.13% once #106's corner finder is current;
    // the wider ±1% the pages once showed was that instrument's older vintage, not real scene
    // disagreement. A three-category box wants ~0.7% less again (a wide-box effect held on #141).
    private const double WidthFill = 0.9862;

    // Deep-regime eye offsets (#141): the verified part is rotX and rotY both under this many
    // degrees — beyond it a third regime opens, still clamped.
    private const double DeepRegimeLimit = 45;

    private readonly double _cosA, _sinA, _cosB, _sinB;
    private readonly double _hx, _hy, _hz;
    private readonly double _frustum, _tan, _ex, _ey;
    private readonly double _rectCentreX, _rectCentreY, _scale;

    /// <param name="rotX">The stated tilt, in degrees.</param>
    /// <param name="rotY">The stated turn, in degrees.</param>
    /// <param name="perspective">The stated <c>c:perspective</c>, in half-degrees of field of view.</param>
    /// <param name="depthPercent">The stated <c>c:depthPercent</c>.</param>
    /// <param name="hPercent">The stated <c>c:hPercent</c>, or null for the absence (#109).</param>
    /// <param name="categories">How many categories the plot draws.</param>
    /// <param name="series">How many series.</param>
    /// <param name="rectLeft">The plot rectangle, in page points.</param>
    /// <param name="rectTop">Its top.</param>
    /// <param name="rectWidth">Its width.</param>
    /// <param name="rectHeight">Its height.</param>
    public Chart3DProjection(
        double rotX, double rotY, double perspective, double depthPercent, double? hPercent,
        double categories, double series,
        double rectLeft, double rectTop, double rectWidth, double rectHeight,
        double? heightUnits = null, double? marginUnits = null)
    {
        _ = marginUnits;

        var a = rotX * Math.PI / 180;
        var b = rotY * Math.PI / 180;
        (_cosA, _sinA, _cosB, _sinB) = (Math.Cos(a), Math.Sin(a), Math.Cos(b), Math.Sin(b));

        // The box, in category-slot units (#116, #137, #109, #138).
        _hx = categories / 2.0;
        _hz = series * depthPercent / 100 / 2;
        _hy = hPercent is { } stated
            ? categories * stated / 100 / 2
            : (heightUnits ?? Math.Floor((categories + series) / 2.0)) * (rectHeight / rectWidth) / 2;

        _tan = Math.Tan(perspective / 4 * Math.PI / 180);
        var aspect = rectWidth / rectHeight;

        // World extents of the rotated box.
        var extentY = _hx * Math.Abs(_sinA * _sinB) + _hy * _cosA + _hz * Math.Abs(_sinA * _cosB);
        var extentX = _hx * Math.Abs(_cosB) + _hz * Math.Abs(_sinB);

        // The eye backs away until the scene fills 0.9702 of the frustum, height against height
        // and width against width; at strong perspective the frustum's height at the floor's
        // near edge binds first. Everything is carried as the frustum's half-height at the box's
        // centre, D·tan(θ), which stays finite at perspective nought — there the divide vanishes
        // and the projection becomes the parallel one #140 measured, with the same fill.
        var floorPart = FloorScale * (_hx * Math.Abs(_sinB * _cosA) + _hz * Math.Abs(_cosB * _cosA));
        var byHeight = extentY / Fill;
        var byWidth = extentX / (Fill * WidthFill * aspect);
        // The whole floor value carries FloorScale·cosA — including the hy the near edge is held at,
        // which the box's tilt foreshortens by cosA (#141). With the bare hy the floor law ran a
        // quarter to two and a half percent high with rotX; the foreshortened intercept brings it
        // to within a tenth of a percent at rotX 15 and 22, leaving the rotX-30 slope open.
        var byFloor = floorPart * _tan + FloorScale * _cosA * _hy;
        _frustum = Math.Max(byHeight, Math.Max(byWidth, byFloor));

        // The eye offsets. In the frustum regime they follow the branch laws below. Where the
        // near-floor constraint has taken over (deep perspective on a mild scene) those laws no
        // longer hold; #141 measured what the offsets do instead, and for the verified part of
        // that regime — rotX and rotY both inside 45°, which is every deep scene Word's UI can
        // reach (perspective caps at 100) — this uses those measured laws. Beyond 45° a third
        // regime begins whose offsets are still open (#141), so there the eye stays clamped at
        // the branch boundary, which keeps the picture stable and close rather than exact.
        var floorBound = byFloor > byHeight && byFloor > byWidth;
        if (floorBound && rotX < DeepRegimeLimit && rotY < DeepRegimeLimit)
        {
            // The deep-regime offset laws (#141), measured corner-by-corner off Word's silhouettes.
            // ex is the frustum law's first term carried on tan θ, less its −hz·sinB intercept:
            // it runs from −hz·sinB at perspective nought toward the frustum edge. ey's intercept
            // is minus the near floor edge's own reach, (hx·sinB + hz·cosB) — the same extent the
            // floor distance is built from — so it foreshortens with the depth as tan θ climbs. A
            // depth sweep pins this: an earlier −0.709·cos(rotY−45) was only its hx=hz=½ case
            // (0.709 ≈ ½√2), right at the default depth and wrong away from it.
            _ex = _sinB * (_hx * aspect * _cosA * _tan - _hz);
            _ey = _sinA * (FloorScale * _hy * _tan - (_hx * _sinB + _hz * _cosB));
        }
        else
        {
            var eyeTan = floorBound ? (Math.Max(byHeight, byWidth) - _hy) / floorPart : _tan;
            _ex = eyeTan * aspect * _cosA * (_hx * _sinB - _hz * _cosB);
            _ey = -eyeTan * FloorScale *
                  (_hx * _sinB * _cosA + Fill * _hz * _cosB * _cosA - _hy * _sinA);
        }

        _rectCentreX = rectLeft + rectWidth / 2;
        _rectCentreY = rectTop + rectHeight / 2;
        _scale = rectHeight / 2 / _frustum;
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

        // About the vertical by rotY, then about the horizontal by minus rotX (#140).
        (sx, sz) = (sx * _cosB + sz * _sinB, -sx * _sinB + sz * _cosB);
        (sy, sz) = (sy * _cosA + sz * _sinA, -sy * _sinA + sz * _cosA);

        // The divide, from the eye at (ex, ey, -D), written through D·tan(θ) so that
        // perspective nought divides by one everywhere; then the frustum is the plot rectangle.
        var towards = _frustum / (sz * _tan + _frustum);
        var qx = _ex + (sx - _ex) * towards;
        var qy = _ey + (sy - _ey) * towards;

        return (_rectCentreX + _scale * (qx - _ex), _rectCentreY - _scale * (qy - _ey));
    }
}
