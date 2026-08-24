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
/// eye follows <c>D = 1.0306·(floor depth extent) + hy·cot(θ)</c> — the frustum's half-height
/// at the floor's near edge held at the box's half-height.</item>
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
/// **The verified domain is the frustum-fit regime** — wherever one of the two frustum-fill
/// constraints sets the eye distance, which covers every perspective at Word's defaults up to
/// about 60, and all perspectives for scenes whose extents keep the frustum constraints binding.
/// Where the near-floor constraint takes over instead (deep perspective on a mild scene), the
/// eye distance is measured to about three percent but the eye offsets leave these laws;
/// the follow-up issue holds the measurements. In that regime this class clamps the offsets at
/// their values where the branch changed, which keeps the picture stable and close rather
/// than exact.
/// </remarks>
internal sealed class Chart3DProjection
{
    private const double Fill = 0.9702;
    private const double FloorScale = 1.0306;

    private readonly double _cosA, _sinA, _cosB, _sinB;
    private readonly double _hx, _hy, _hz;
    private readonly double _tan, _eyeTan;
    private readonly double _distance, _ex, _ey;
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
        int categories, int series,
        double rectLeft, double rectTop, double rectWidth, double rectHeight)
    {
        var a = rotX * Math.PI / 180;
        var b = rotY * Math.PI / 180;
        (_cosA, _sinA, _cosB, _sinB) = (Math.Cos(a), Math.Sin(a), Math.Cos(b), Math.Sin(b));

        // The box, in category-slot units (#116, #137, #109, #138).
        _hx = categories / 2.0;
        _hz = series * depthPercent / 100 / 2;
        _hy = hPercent is { } stated
            ? categories * stated / 100 / 2
            : Math.Floor((categories + series) / 2.0) * (rectHeight / rectWidth) / 2;

        _tan = Math.Tan(perspective / 4 * Math.PI / 180);
        var aspect = rectWidth / rectHeight;

        // World extents of the rotated box.
        var extentY = _hx * Math.Abs(_sinA * _sinB) + _hy * _cosA + _hz * Math.Abs(_sinA * _cosB);
        var extentX = _hx * Math.Abs(_cosB) + _hz * Math.Abs(_sinB);

        // The eye backs away until the scene fills 0.9702 of the frustum, height against height
        // and width against width; at strong perspective the frustum's height at the floor's
        // near edge binds first.
        var byHeight = extentY / (Fill * _tan);
        var byWidth = extentX / (Fill * 0.9862 * aspect * _tan);
        var byFloor = FloorScale * (_hx * Math.Abs(_sinB * _cosA) + _hz * Math.Abs(_cosB * _cosA))
                      + _hy / _tan;
        _distance = Math.Max(byHeight, Math.Max(byWidth, byFloor));

        // The eye offsets follow the frustum branches; where the floor constraint has taken
        // over they are clamped at the change of branch — the known gap, held by the follow-up.
        _eyeTan = _tan;
        if (byFloor > byHeight && byFloor > byWidth)
        {
            // The tangent at which the binding frustum constraint would have met the floor one.
            var frustum = Math.Max(extentY / Fill, extentX / (Fill * 0.9862 * aspect));
            var floorPart = FloorScale * (_hx * Math.Abs(_sinB * _cosA) + _hz * Math.Abs(_cosB * _cosA));
            _eyeTan = (frustum - _hy) / floorPart;
        }

        _ex = _eyeTan * aspect * _cosA * (_hx * _sinB - _hz * _cosB);
        _ey = -_eyeTan * FloorScale *
              (_hx * _sinB * _cosA + Fill * _hz * _cosB * _cosA - _hy * _sinA);

        _rectCentreX = rectLeft + rectWidth / 2;
        _rectCentreY = rectTop + rectHeight / 2;
        _scale = rectHeight / 2 / (_distance * _tan);
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

        // The divide, from the eye at (ex, ey, -D); then the frustum is the plot rectangle.
        var towards = _distance / (sz + _distance);
        var qx = _ex + (sx - _ex) * towards;
        var qy = _ey + (sy - _ey) * towards;

        return (_rectCentreX + _scale * (qx - _ex), _rectCentreY - _scale * (qy - _ey));
    }
}
