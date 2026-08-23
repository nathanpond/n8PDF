using System.Xml.Linq;
using n8PDF.Ooxml;

namespace n8PDF.Tests;

/// <summary>
/// Tests that the scene of a chart drawn in three dimensions is read as Word reads it.
/// </summary>
/// <remarks>
/// This is the one part of the three-dimensional work whose correctness can be asserted exactly
/// rather than to a grid step, because it is parsing rather than geometry. Everything after it is
/// measured against a raster — Word puts the whole plot on the page as one 300 dpi bitmap — so the
/// scene is the last thing that can be pinned to the number.
///
/// **The defaults were measured, not read off the schema**, over four rounds against Word: pages
/// stating nothing beside pages stating candidates, exported and compared as rendered pictures.
/// What that turned up is the reason these tests exist at all — there are two sets of defaults, and
/// which applies turns on whether <c>c:view3D</c> is present rather than on which of its children
/// is missing. A chart stating nothing and a chart stating an empty <c>c:view3D</c> are different
/// pictures in Word, by 37% of their ink.
///
/// Each default is therefore asserted from both sides: what it is, and that the other set's value
/// is not what comes back. A test that only checked the absent-element case would pass with the
/// two sets confused.
/// </remarks>
public class Chart3DSceneTests
{
    private const string C = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    /// <summary>A chart part of the given plot element, with the given <c>c:view3D</c> or none.</summary>
    private static XDocument Part(string element, string view3D = "", string plotExtra = "") =>
        XDocument.Parse($"""
            <c:chartSpace xmlns:c="{C}"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                {view3D}
                <c:plotArea><c:layout/>
                  <c:{element}>
                    <c:barDir val="col"/><c:grouping val="clustered"/>
                    {plotExtra}
                    <c:ser><c:idx val="0"/><c:order val="0"/>
                      <c:val><c:numRef><c:numCache><c:ptCount val="1"/>
                        <c:pt idx="0"><c:v>1</c:v></c:pt></c:numCache></c:numRef></c:val>
                    </c:ser>
                  </c:{element}>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

    private static ChartScene Scene(string element, string view3D = "") =>
        Assert.IsType<ChartScene>(ChartReader.Parse(Part(element, view3D))?.Scene);

    /// <summary>
    /// A chart carrying no <c>c:view3D</c> takes Word's own scene, not the schema's.
    /// </summary>
    /// <remarks>
    /// The page stating nothing came out pixel for pixel identical to one stating 15, 20 and false,
    /// and differed from 20 for rotX by 38% of its ink, from 30 for rotY by 62%, from 0 for
    /// perspective by 41% and from 50 for depthPercent by 35%. Every one of those five is asserted
    /// here, so a change to any of them fails rather than only the two that happen to be non-zero.
    /// </remarks>
    [Theory]
    [InlineData("bar3DChart")]
    [InlineData("line3DChart")]
    [InlineData("pie3DChart")]
    [InlineData("area3DChart")]
    [InlineData("surface3DChart")]
    [InlineData("surfaceChart")]
    public void A_chart_with_no_view3D_takes_words_own_scene(string element)
    {
        var scene = Scene(element);

        Assert.Equal(15, scene.RotationX);
        Assert.Equal(20, scene.RotationY);
        Assert.Equal(100, scene.DepthPercent);
        Assert.False(scene.RightAngleAxes);
        Assert.Equal(30, scene.Perspective);

        // The same for every kind. The issue this was built for claimed a pie took a scene of its
        // own — 30 and 0 rather than 15 and 20 — and Word says otherwise: the pie page stating
        // nothing matched 15 and 20 exactly, and differed from 20 for rotX by 30% of its ink.
        Assert.Equal(ChartScene.Unstated, scene);
    }

    /// <summary>
    /// A <c>c:view3D</c> that is present but says nothing takes the schema's scene instead, which
    /// is a different picture.
    /// </summary>
    /// <remarks>
    /// The heart of it. An empty <c>c:view3D</c> came out identical to one stating 0, 0 and
    /// **true**, and differed from 0, 0 and false by 37% of its ink — so the rotations fall to
    /// nought while the right-angle flag goes the other way from the absent-element case. Nothing
    /// about that is guessable, and a reader that treated the two cases alike would be wrong in
    /// both directions at once.
    /// </remarks>
    [Fact]
    public void A_view3D_that_states_nothing_takes_the_schemas_scene()
    {
        var scene = Scene("bar3DChart", "<c:view3D/>");

        Assert.Equal(0, scene.RotationX);
        Assert.Equal(0, scene.RotationY);
        Assert.True(scene.RightAngleAxes);

        // These two do not move between the sets, which is worth pinning: it would be tidier if all
        // five differed, and they do not.
        Assert.Equal(100, scene.DepthPercent);
        Assert.Equal(30, scene.Perspective);

        // And it is genuinely a different scene from the absent case, in both directions.
        Assert.NotEqual(ChartScene.Unstated, scene);
        Assert.NotEqual(ChartScene.Unstated.RightAngleAxes, scene.RightAngleAxes);
    }

    /// <summary>Every child of a stated <c>c:view3D</c> is read.</summary>
    [Fact]
    public void A_view3D_that_states_everything_is_read_whole()
    {
        var scene = Scene("bar3DChart",
            """
            <c:view3D>
              <c:rotX val="35"/><c:rotY val="130"/><c:depthPercent val="250"/>
              <c:rAngAx val="0"/><c:perspective val="45"/>
            </c:view3D>
            """);

        Assert.Equal(new ChartScene(35, 130, 250, false, 45), scene);
    }

    /// <summary>
    /// The right-angle flag is read from what the attribute says rather than from whether it is
    /// there.
    /// </summary>
    /// <remarks>
    /// Both spellings of both values, because the format allows either and a reader comparing
    /// against "1" alone would take "true" for false — which, given the flag defaults to true when
    /// absent, would be invisible in the common case and wrong in the stated one.
    /// </remarks>
    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    public void The_right_angle_flag_is_read_from_its_value(string stated, bool expected)
    {
        Assert.Equal(expected,
            Scene("bar3DChart", $"<c:view3D><c:rAngAx val=\"{stated}\"/></c:view3D>").RightAngleAxes);
    }

    /// <summary>
    /// A flat chart has no scene at all, which is what tells the two apart everywhere downstream.
    /// </summary>
    [Theory]
    [InlineData("barChart")]
    [InlineData("lineChart")]
    [InlineData("pieChart")]
    [InlineData("areaChart")]
    public void A_flat_chart_has_no_scene(string element)
    {
        // Even where the document carries a c:view3D, which Word writes into some flat charts and
        // ignores. A scene on a flat chart would send it down the three-dimensional path.
        Assert.Null(ChartReader.Parse(Part(element, "<c:view3D><c:rotX val=\"15\"/></c:view3D>"))?.Scene);
    }

    /// <summary>
    /// The three-dimensional plot elements are recognised, and carry the nearest flat kind.
    /// </summary>
    /// <remarks>
    /// Before this they were recognised by nothing at all: the lookup fell through to null and the
    /// whole chart was dropped, taking its frame with it — see #95, which is what that costs.
    /// </remarks>
    [Theory]
    // The kind is named rather than passed, since ChartKind is internal and a public test method
    // may not take one.
    [InlineData("bar3DChart", "Column")]
    [InlineData("line3DChart", "Line")]
    [InlineData("pie3DChart", "Pie")]
    [InlineData("area3DChart", "Area")]
    [InlineData("surface3DChart", "Line")]
    [InlineData("surfaceChart", "Line")]
    public void A_three_dimensional_plot_is_recognised_and_carries_the_nearest_flat_kind(
        string element, string kind)
    {
        var chart = ChartReader.Parse(Part(element));

        Assert.NotNull(chart);
        Assert.Equal(kind, chart.Kind.ToString());
        Assert.NotNull(chart.Scene);
    }

    /// <summary>What only a three-dimensional plot carries.</summary>
    [Fact]
    public void The_depth_gap_and_the_bar_shape_are_read()
    {
        var stated = ChartReader.Parse(Part("bar3DChart",
            plotExtra: "<c:gapDepth val=\"400\"/><c:shape val=\"cylinder\"/>"));

        Assert.NotNull(stated);
        Assert.Equal(400, stated.GapDepth);
        Assert.Equal("cylinder", stated.Shape);

        var silent = ChartReader.Parse(Part("bar3DChart"));

        Assert.NotNull(silent);
        Assert.Equal(150, silent.GapDepth);
        Assert.Equal("box", silent.Shape);
    }

    /// <summary>The depth axis is read, and only a chart that has one gets one.</summary>
    [Fact]
    public void The_depth_axis_is_read_where_there_is_one()
    {
        var part = XDocument.Parse($"""
            <c:chartSpace xmlns:c="{C}">
              <c:chart><c:plotArea><c:layout/>
                <c:bar3DChart><c:barDir val="col"/>
                  <c:ser><c:idx val="0"/><c:order val="0"/>
                    <c:val><c:numRef><c:numCache><c:ptCount val="1"/>
                      <c:pt idx="0"><c:v>1</c:v></c:pt></c:numCache></c:numRef></c:val>
                  </c:ser>
                </c:bar3DChart>
                <c:catAx><c:axId val="1"/><c:scaling><c:orientation val="minMax"/></c:scaling>
                  <c:delete val="0"/><c:axPos val="b"/><c:crossAx val="2"/></c:catAx>
                <c:valAx><c:axId val="2"/><c:scaling><c:orientation val="minMax"/></c:scaling>
                  <c:delete val="0"/><c:axPos val="l"/><c:crossAx val="1"/></c:valAx>
                <c:serAx><c:axId val="3"/><c:scaling><c:orientation val="minMax"/></c:scaling>
                  <c:delete val="0"/><c:axPos val="b"/><c:crossAx val="2"/></c:serAx>
              </c:plotArea></c:chart>
            </c:chartSpace>
            """);

        var chart = ChartReader.Parse(part);

        Assert.NotNull(chart);
        Assert.NotNull(chart.DepthAxis);
        Assert.NotNull(chart.CategoryAxis);
        Assert.NotNull(chart.ValueAxis);

        // A depth axis is not a value axis, and reading it as one would scale the series names.
        Assert.False(chart.DepthAxis.IsValueAxis);

        // And a flat chart has none, so nothing downstream has to ask whether it applies.
        Assert.Null(ChartReader.Parse(Part("barChart"))?.DepthAxis);
    }

    /// <summary>
    /// <c>c:hPercent</c> is read, and its absence is kept as an absence.
    /// </summary>
    /// <remarks>
    /// The one child of <c>c:view3D</c> with no numeric default at all — see #109 and
    /// <see cref="ChartScene.HeightOverWidth"/>. Where the document says nothing, Word makes the box
    /// as tall relative to its width as the plot area is, which no constant reproduces because the
    /// plot area can be any shape. So it is kept as null rather than defaulted, and the two cases
    /// are asserted apart here as well as through the drawing.
    ///
    /// A hundred is the schema's default and the obvious wrong answer: it draws a box a third
    /// narrower than Word's on <c>chart-3d-height-probe</c>'s baseline.
    /// </remarks>
    [Fact]
    public void The_height_percentage_is_read_and_its_absence_is_kept_as_one()
    {
        Assert.Equal(250, Scene("bar3DChart",
            "<c:view3D><c:hPercent val=\"250\"/></c:view3D>").HeightPercent);

        // Absent in both of the ways a chart can be silent about it, and null in both.
        Assert.Null(Scene("bar3DChart").HeightPercent);
        Assert.Null(Scene("bar3DChart", "<c:view3D/>").HeightPercent);
        Assert.Null(ChartScene.Unstated.HeightPercent);
    }

    /// <summary>
    /// What the box's shape comes out as, stated and unstated.
    /// </summary>
    /// <remarks>
    /// Stated, it is the percentage and the plot area has no say. Unstated, it is the plot area's
    /// own shape and nothing else — which is the whole of why the absence cannot be written as a
    /// number.
    /// </remarks>
    [Theory]
    // Stated: the same answer whatever the plot area is.
    [InlineData(200.0, 216, 118.8, 2.0)]
    [InlineData(200.0, 216, 172.8, 2.0)]
    [InlineData(50.0,  216, 118.8, 0.5)]
    // Unstated: the plot area's shape, and it moves with it.
    [InlineData(null,  216, 118.8, 0.55)]
    [InlineData(null,  216, 172.8, 0.80)]
    [InlineData(null,  288, 118.8, 0.4125)]
    public void The_boxs_shape_follows_the_element_where_there_is_one_and_the_plot_area_where_not(
        double? stated, double plotWidth, double plotHeight, double expected)
    {
        var scene = ChartScene.Unstated with { HeightPercent = stated };

        Assert.Equal(expected, scene.HeightOverWidth(plotWidth, plotHeight), 6);
    }
}
