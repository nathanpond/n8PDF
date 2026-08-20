namespace n8PDF.Fonts;

/// <summary>
/// The numbers a face states for setting mathematics: how far a superscript rises, how thick a
/// fraction's bar is, how much room a radical leaves above what is under it.
/// </summary>
/// <remarks>
/// All of them are in the font's own design units, as every other metric is, and all of them come
/// from the font rather than from anywhere here. A face meant for mathematics carries a
/// <c>MATH</c> table saying what its own proportions are; Cambria Math has one, and it is what
/// Word sets equations by. The alternative would be fitting a few dozen constants to measurements
/// of Word's output, which would be guessing at numbers the font states outright.
///
/// The rest of the table is read elsewhere in this file: how far each glyph leans and how far a
/// script tucks into each of its corners, and the taller shapes a bracket grows into. What is not read is the recipe for building a bracket taller than
/// the tallest shape the face keeps, out of a top, a bottom and as many middles as it takes;
/// nothing an equation asks for at the sizes a document is set at has reached the end of the
/// shapes yet. See <see cref="Layout.MathComposer"/>.
/// </remarks>
/// <param name="SuperscriptShiftUpCramped">
/// What a superscript is raised by where what it is on is itself under something — inside a
/// radical, or under a fraction's bar. Word uses it: the square of the b under the root of the
/// quadratic formula is raised by this and not by the other.
/// </param>
internal sealed record MathConstants(
    double ScriptPercentScaleDown,
    double ScriptScriptPercentScaleDown,
    double MathLeading,
    double AxisHeight,
    double SubscriptShiftDown,
    double SubscriptTopMax,
    double SubscriptBaselineDropMin,
    double SuperscriptShiftUp,
    double SuperscriptShiftUpCramped,
    double SuperscriptBottomMin,
    double SuperscriptBaselineDropMax,
    double SubSuperscriptGapMin,
    double SpaceAfterScript,
    double UpperLimitGapMin,
    double UpperLimitBaselineRiseMin,
    double LowerLimitGapMin,
    double LowerLimitBaselineDropMin,
    double FractionNumeratorShiftUp,
    double FractionNumeratorDisplayStyleShiftUp,
    double FractionDenominatorShiftDown,
    double FractionDenominatorDisplayStyleShiftDown,
    double FractionNumeratorGapMin,
    double FractionNumDisplayStyleGapMin,
    double FractionRuleThickness,
    double FractionDenominatorGapMin,
    double FractionDenomDisplayStyleGapMin,
    double SkewedFractionHorizontalGap,
    double SkewedFractionVerticalGap,
    double OverbarVerticalGap,
    double OverbarRuleThickness,
    double OverbarExtraAscender,
    double RadicalVerticalGap,
    double RadicalDisplayStyleVerticalGap,
    double RadicalRuleThickness,
    double RadicalExtraAscender,
    double RadicalKernBeforeDegree,
    double RadicalKernAfterDegree,
    double RadicalDegreeBottomRaisePercent)
{
    /// <summary>
    /// What a face without a <c>MATH</c> table gets: the proportions of Cambria Math, as fractions
    /// of an em rather than of its own design units.
    /// </summary>
    /// <remarks>
    /// A document can set an equation in any face it likes and most faces say nothing about
    /// mathematics. Rather than refuse to draw one, the shape of a face that does is borrowed and
    /// scaled to the one in hand — which is what a reader without the numbers can do, and closer
    /// than nothing.
    /// </remarks>
    public static MathConstants Fallback(double unitsPerEm)
    {
        var em = unitsPerEm / 2048.0;

        return new MathConstants(
            ScriptPercentScaleDown: 73,
            ScriptScriptPercentScaleDown: 60,
            MathLeading: 154 * em,
            AxisHeight: 585 * em,
            SubscriptShiftDown: 350 * em,
            SubscriptTopMax: 819 * em,
            SubscriptBaselineDropMin: 96 * em,
            SuperscriptShiftUp: 860 * em,
            SuperscriptShiftUpCramped: 700 * em,
            SuperscriptBottomMin: 250 * em,
            SuperscriptBaselineDropMax: 819 * em,
            SubSuperscriptGapMin: 528 * em,
            SpaceAfterScript: 82 * em,
            UpperLimitGapMin: 274 * em,
            UpperLimitBaselineRiseMin: 0,
            LowerLimitGapMin: 274 * em,
            LowerLimitBaselineDropMin: 600 * em,
            FractionNumeratorShiftUp: 780 * em,
            FractionNumeratorDisplayStyleShiftUp: 1229 * em,
            FractionDenominatorShiftDown: 780 * em,
            FractionDenominatorDisplayStyleShiftDown: 1229 * em,
            FractionNumeratorGapMin: 132 * em,
            FractionNumDisplayStyleGapMin: 396 * em,
            FractionRuleThickness: 132 * em,
            FractionDenominatorGapMin: 132 * em,
            FractionDenomDisplayStyleGapMin: 396 * em,
            SkewedFractionHorizontalGap: 700 * em,
            SkewedFractionVerticalGap: 0,
            OverbarVerticalGap: 396 * em,
            OverbarRuleThickness: 132 * em,
            OverbarExtraAscender: 132 * em,
            RadicalVerticalGap: 164 * em,
            RadicalDisplayStyleVerticalGap: 462 * em,
            RadicalRuleThickness: 132 * em,
            RadicalExtraAscender: 132 * em,
            RadicalKernBeforeDegree: 556 * em,
            RadicalKernAfterDegree: -1000 * em,
            RadicalDegreeBottomRaisePercent: 65);
    }

    /// <summary>
    /// Reads the constants a <c>MATH</c> table opens with.
    /// </summary>
    /// <remarks>
    /// The table is a header of three offsets followed by a fixed run of values: two percentages,
    /// two minimum heights, and then fifty-one records of a value and an optional device table,
    /// in the order the specification lists them. Only the value of each is wanted — a device
    /// table says how to adjust it for a particular pixel size, which is a screen's problem.
    /// </remarks>
    public static MathConstants? Read(byte[] data, int offset, double unitsPerEm)
    {
        if (offset <= 0 || offset + 10 > data.Length) return null;

        var constants = offset + ReadUShort(data, offset + 4);
        if (constants + 4 + 51 * 4 > data.Length) return null;

        var scriptPercent = ReadShort(data, constants);
        var scriptScriptPercent = ReadShort(data, constants + 2);

        // The two percentages and the two minimum heights come first, and only then the records.
        var at = constants + 8;
        var index = 0;

        double Value(int which)
        {
            // Each record is a value and an offset; the values are in order, so this only has to
            // be handed the right one.
            var position = at + which * 4;
            return position + 2 <= data.Length ? ReadShort(data, position) : 0;
        }

        double Next() => Value(index++);

        var leading = Next();

        var axisHeight = Next();
        var accentBaseHeight = Next();
        var flattenedAccentBaseHeight = Next();
        _ = (accentBaseHeight, flattenedAccentBaseHeight);

        var subscriptShiftDown = Next();
        var subscriptTopMax = Next();
        var subscriptBaselineDropMin = Next();
        var superscriptShiftUp = Next();
        var superscriptShiftUpCramped = Next();

        var superscriptBottomMin = Next();
        var superscriptBaselineDropMax = Next();
        var subSuperscriptGapMin = Next();
        var superscriptBottomMaxWithSubscript = Next();
        _ = superscriptBottomMaxWithSubscript;

        var spaceAfterScript = Next();
        var upperLimitGapMin = Next();
        var upperLimitBaselineRiseMin = Next();
        var lowerLimitGapMin = Next();
        var lowerLimitBaselineDropMin = Next();

        // The stack constants, which are what a fraction without a bar is set by, and the four
        // that go with a stack under something stretched over it. Ten records, none of them read.
        for (var i = 0; i < 10; i++) Next();

        var fractionNumeratorShiftUp = Next();
        var fractionNumeratorDisplayStyleShiftUp = Next();
        var fractionDenominatorShiftDown = Next();
        var fractionDenominatorDisplayStyleShiftDown = Next();
        var fractionNumeratorGapMin = Next();
        var fractionNumDisplayStyleGapMin = Next();
        var fractionRuleThickness = Next();
        var fractionDenominatorGapMin = Next();
        var fractionDenomDisplayStyleGapMin = Next();
        var skewedFractionHorizontalGap = Next();
        var skewedFractionVerticalGap = Next();
        var overbarVerticalGap = Next();
        var overbarRuleThickness = Next();
        var overbarExtraAscender = Next();

        // The underbar constants, which nothing here draws.
        for (var i = 0; i < 3; i++) Next();

        var radicalVerticalGap = Next();
        var radicalDisplayStyleVerticalGap = Next();
        var radicalRuleThickness = Next();
        var radicalExtraAscender = Next();
        var radicalKernBeforeDegree = Next();
        var radicalKernAfterDegree = Next();

        // The last one is a percentage rather than a record, and it follows all fifty-one of them,
        // so it is read from where they end rather than from wherever the reading got to.
        var raisePercent = ReadShort(data, at + 51 * 4);

        _ = unitsPerEm;

        return new MathConstants(
            scriptPercent, scriptScriptPercent, leading, axisHeight,
            subscriptShiftDown, subscriptTopMax, subscriptBaselineDropMin,
            superscriptShiftUp, superscriptShiftUpCramped,
            superscriptBottomMin, superscriptBaselineDropMax,
            subSuperscriptGapMin, spaceAfterScript,
            upperLimitGapMin, upperLimitBaselineRiseMin,
            lowerLimitGapMin, lowerLimitBaselineDropMin,
            fractionNumeratorShiftUp, fractionNumeratorDisplayStyleShiftUp,
            fractionDenominatorShiftDown, fractionDenominatorDisplayStyleShiftDown,
            fractionNumeratorGapMin, fractionNumDisplayStyleGapMin,
            fractionRuleThickness, fractionDenominatorGapMin, fractionDenomDisplayStyleGapMin,
            skewedFractionHorizontalGap, skewedFractionVerticalGap,
            overbarVerticalGap, overbarRuleThickness, overbarExtraAscender,
            radicalVerticalGap, radicalDisplayStyleVerticalGap, radicalRuleThickness,
            radicalExtraAscender, radicalKernBeforeDegree, radicalKernAfterDegree,
            raisePercent);
    }

    /// <summary>
    /// How much room a sloped glyph wants after it, glyph by glyph.
    /// </summary>
    /// <remarks>
    /// A sloped letter leans out over the space that follows it, so what comes next has to be
    /// moved along or it collides with the overhang. The face states the amount for every glyph
    /// that needs one, and Word uses it: it sets <c>x+y</c> with 0.36pt more after the x than the
    /// space between them accounts for, which is exactly what Cambria Math says the x's correction
    /// is at twelve point.
    /// </remarks>
    public static IReadOnlyDictionary<ushort, short> ReadItalics(byte[] data, int offset)
    {
        var corrections = new Dictionary<ushort, short>();

        if (offset <= 0 || offset + 10 > data.Length) return corrections;

        // Three tables deep: the header points at the glyph information, that points at the
        // corrections, and those point at the glyphs they are for.
        var info = ReadUShort(data, offset + 6);
        if (info == 0) return corrections;

        var glyphInfo = offset + info;
        if (glyphInfo + 2 > data.Length) return corrections;

        var italics = glyphInfo + ReadUShort(data, glyphInfo);
        if (italics + 4 > data.Length) return corrections;

        var coverage = italics + ReadUShort(data, italics);
        var count = ReadUShort(data, italics + 2);

        var glyphs = Covered(data, coverage);

        for (var i = 0; i < count && i < glyphs.Count; i++)
        {
            var at = italics + 4 + i * 4;
            if (at + 2 > data.Length) break;

            corrections[glyphs[i]] = ReadShort(data, at);
        }

        return corrections;
    }

    /// <summary>
    /// The larger shapes a face keeps for a bracket, a radical or anything else that grows to fit
    /// what it holds, in the order it offers them.
    /// </summary>
    /// <remarks>
    /// A bracket round a fraction is not the ordinary bracket drawn larger: it is a different
    /// glyph, drawn at the same size, with the same weight of stroke and a taller bowl. A face
    /// meant for mathematics keeps a series of them and says how tall each one is, and this is
    /// that series. Scaling the ordinary one instead gives a bracket whose strokes thicken with
    /// its height, which is not what a bracket does.
    /// </remarks>
    public static IReadOnlyDictionary<ushort, IReadOnlyList<(ushort Glyph, int Height)>> ReadVariants(
        byte[] data, int offset)
    {
        var variants = new Dictionary<ushort, IReadOnlyList<(ushort, int)>>();

        if (offset <= 0 || offset + 10 > data.Length) return variants;

        var table = offset + ReadUShort(data, offset + 8);
        if (table + 10 > data.Length) return variants;

        var coverage = table + ReadUShort(data, table + 2);
        var count = ReadUShort(data, table + 6);

        var glyphs = Covered(data, coverage);

        for (var i = 0; i < count && i < glyphs.Count; i++)
        {
            var at = table + 10 + i * 2;
            if (at + 2 > data.Length) break;

            var construction = table + ReadUShort(data, at);
            if (construction + 4 > data.Length) continue;

            var records = ReadUShort(data, construction + 2);
            var offered = new List<(ushort, int)>(records);

            for (var r = 0; r < records; r++)
            {
                var record = construction + 4 + r * 4;
                if (record + 4 > data.Length) break;

                offered.Add(((ushort)ReadUShort(data, record), ReadUShort(data, record + 2)));
            }

            if (offered.Count > 0) variants[glyphs[i]] = offered;
        }

        return variants;
    }

    /// <summary>The glyphs a coverage table lists, in the order it lists them.</summary>
    private static List<ushort> Covered(byte[] data, int offset)
    {
        var glyphs = new List<ushort>();
        if (offset + 4 > data.Length) return glyphs;

        var format = ReadUShort(data, offset);

        if (format == 1)
        {
            var count = ReadUShort(data, offset + 2);

            for (var i = 0; i < count && offset + 4 + i * 2 + 2 <= data.Length; i++)
                glyphs.Add((ushort)ReadUShort(data, offset + 4 + i * 2));

            return glyphs;
        }

        if (format != 2) return glyphs;

        var ranges = ReadUShort(data, offset + 2);

        for (var i = 0; i < ranges; i++)
        {
            var at = offset + 4 + i * 6;
            if (at + 6 > data.Length) break;

            var first = ReadUShort(data, at);
            var last = ReadUShort(data, at + 2);

            for (var glyph = first; glyph <= last && glyph <= ushort.MaxValue; glyph++)
                glyphs.Add((ushort)glyph);
        }

        return glyphs;
    }

    /// <summary>
    /// What a face says about tucking a script into the corner of the letter it sits on, glyph by
    /// glyph.
    /// </summary>
    /// <remarks>
    /// A script sits closer to some letters than to others: the two of an <c>f²</c> can come in
    /// over the f's hook, and the two of an <c>A²</c> has to stand off the A's slope. The face
    /// states the amount for each of the four corners of each glyph, and states it as a staircase
    /// — a set of heights and a value for the space between each pair — so that the amount can
    /// depend on how far up the corner the script sits.
    ///
    /// Which height Word reads the staircase at is measured in math-kern-probe: see
    /// <see cref="Layout.MathComposer"/>, where the reading is done.
    /// </remarks>
    public static IReadOnlyDictionary<ushort, MathKerns> ReadKerns(byte[] data, int offset)
    {
        var kerns = new Dictionary<ushort, MathKerns>();

        if (offset <= 0 || offset + 8 > data.Length) return kerns;

        // The third table of the glyph information, after the italic corrections and the accent
        // attachments.
        var info = ReadUShort(data, offset + 6);
        if (info == 0) return kerns;

        var glyphInfo = offset + info;
        if (glyphInfo + 8 > data.Length) return kerns;

        var table = ReadUShort(data, glyphInfo + 6);
        if (table == 0) return kerns;

        var kernInfo = glyphInfo + table;
        if (kernInfo + 4 > data.Length) return kerns;

        var coverage = kernInfo + ReadUShort(data, kernInfo);
        var glyphs = Covered(data, coverage);
        var count = ReadUShort(data, kernInfo + 2);

        for (var i = 0; i < count && i < glyphs.Count; i++)
        {
            var record = kernInfo + 4 + i * 8;
            if (record + 8 > data.Length) break;

            kerns[glyphs[i]] = new MathKerns(
                Staircase(data, kernInfo, ReadUShort(data, record)),
                Staircase(data, kernInfo, ReadUShort(data, record + 2)),
                Staircase(data, kernInfo, ReadUShort(data, record + 4)),
                Staircase(data, kernInfo, ReadUShort(data, record + 6)));
        }

        return kerns;
    }

    /// <summary>
    /// One corner's staircase: the heights it turns at, and the value between each pair of them.
    /// There is always one more value than there are heights.
    /// </summary>
    private static MathStaircase? Staircase(byte[] data, int kernInfo, int offset)
    {
        if (offset == 0 || kernInfo + offset + 2 > data.Length) return null;

        var at = kernInfo + offset;
        var steps = ReadUShort(data, at);

        if (at + 2 + steps * 8 + 4 > data.Length) return null;

        var heights = new short[steps];
        var values = new short[steps + 1];

        for (var i = 0; i < steps; i++) heights[i] = ReadShort(data, at + 2 + i * 4);
        for (var i = 0; i <= steps; i++) values[i] = ReadShort(data, at + 2 + steps * 4 + i * 4);

        return new MathStaircase(heights, values);
    }

    private static int ReadUShort(byte[] data, int offset) =>
        (data[offset] << 8) | data[offset + 1];

    private static short ReadShort(byte[] data, int offset) =>
        (short)((data[offset] << 8) | data[offset + 1]);
}

/// <summary>
/// A run of kern values by height: the value up to the first height, then between each pair, then
/// above the last.
/// </summary>
internal sealed record MathStaircase(short[] Heights, short[] Values)
{
    /// <summary>What the face states at a given height, in design units.</summary>
    public short At(double height)
    {
        for (var i = 0; i < Heights.Length; i++)
        {
            if (height < Heights[i]) return Values[i];
        }

        return Values[^1];
    }
}

/// <summary>The four corners of a glyph, as far as the face states anything about them.</summary>
internal sealed record MathKerns(
    MathStaircase? TopRight, MathStaircase? TopLeft,
    MathStaircase? BottomRight, MathStaircase? BottomLeft);
