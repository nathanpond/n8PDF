namespace n8PDF.Fonts;

/// <summary>
/// Vertical metrics and descriptor values, all in font design units unless noted. Divide by
/// <see cref="UnitsPerEm"/> and multiply by the point size to get points.
/// </summary>
internal sealed record FontMetrics
{
    public required int UnitsPerEm { get; init; }

    /// <summary>Ascender from <c>hhea</c>. Positive, above the baseline.</summary>
    public required int Ascender { get; init; }

    /// <summary>Descender from <c>hhea</c>. Negative, below the baseline.</summary>
    public required int Descender { get; init; }

    public required int LineGap { get; init; }

    /// <summary>Typographic ascender from <c>OS/2</c>.</summary>
    public required int TypoAscender { get; init; }

    public required int TypoDescender { get; init; }

    public required int TypoLineGap { get; init; }

    /// <summary>Windows ascent from <c>OS/2</c>. Positive.</summary>
    public required int WinAscent { get; init; }

    /// <summary>Windows descent from <c>OS/2</c>. Stored positive, unlike the hhea descender.</summary>
    public required int WinDescent { get; init; }

    /// <summary>
    /// Whether <c>OS/2</c> declares one of the East Asian code pages — Japanese, either Chinese,
    /// or either Korean.
    /// </summary>
    public required bool IsEastAsian { get; init; }

    /// <summary>
    /// The <c>USE_TYPO_METRICS</c> flag (fsSelection bit 7). When set, the font author is
    /// asking consumers to prefer the typographic metrics over the win metrics for line height.
    /// </summary>
    public required bool UseTypoMetrics { get; init; }

    public required int CapHeight { get; init; }

    public required int XHeight { get; init; }

    /// <summary>Italic angle in degrees, negative for the usual forward slant.</summary>
    public required double ItalicAngle { get; init; }

    public required int WeightClass { get; init; }

    public required bool IsFixedPitch { get; init; }

    public required int BBoxMinX { get; init; }

    public required int BBoxMinY { get; init; }

    public required int BBoxMaxX { get; init; }

    public required int BBoxMaxY { get; init; }

    /// <summary>
    /// Word derives single line spacing from the hhea metrics for most fonts, but honours the
    /// typographic metrics when the font asks it to. Getting this wrong shifts every line on
    /// the page, so the choice lives in one place.
    /// </summary>
    public int DefaultLineHeight => IsEastAsian
        ? EastAsianHeight + EastAsianLeading
        : UseTypoMetrics && TypoAscender > 0
            ? TypoAscender - TypoDescender + TypoLineGap
            : Ascender - Descender + LineGap;

    /// <summary>
    /// Distance from the baseline to the top of the line box, matching the choice above.
    /// </summary>
    /// <remarks>
    /// The line gap belongs above the ascent rather than below the descent. Verified against
    /// Word: for 12pt Times New Roman it places the first baseline 11.203pt below the top margin,
    /// which is ascender + lineGap, not the ascender alone. Using the bare ascender put every
    /// first baseline about 0.59pt high.
    /// </remarks>
    public int DefaultAscent => IsEastAsian
        ? Ascender + (int)Math.Round(EastAsianHeight * 0.16)
        : UseTypoMetrics && TypoAscender > 0
            ? TypoAscender + TypoLineGap
            : Ascender + LineGap;

    /// <summary>
    /// The leading Word adds to an East Asian face, in design units: three tenths of an em.
    /// </summary>
    /// <remarks>
    /// Measured, not derived. Word gives MS Mincho, MS Gothic, KaiTi and MingLiU at 12pt a line
    /// of exactly 15.6pt — 1.3 em — although the four faces ask for 1.0, 1.0, 1.14 and 1.20 em
    /// between them, and although Core Text reads those four values back from the files just as
    /// this reader does. It does the same for a line of Latin letters set in one of them, so the
    /// height belongs to the face and not to the script written in it. See the
    /// east-asian-line-box-probe fixture, which is what these numbers were read off.
    ///
    /// Of the three tenths, sixteen hundredths go above the ascent and the rest below the
    /// descent. That split is measured too, and it is as close as Word's own vertical quantum of
    /// 1/300 inch allows: it puts every one of the four faces' baselines within one quantum.
    ///
    /// What the three tenths multiply is the **win height**, not the em. The four faces above
    /// could not tell the two apart — their win metrics sum to exactly one em — and Malgun
    /// Gothic is the face that split them: its win height is 1.33 em, and Word draws its lines
    /// at 1.727 em, which is 1.3 of the win height and nothing like 1.3 of the em (the
    /// hangul-jamo-probe fixture holds the measurement). KaiTi and MingLiU rule out the hhea
    /// height as the multiplicand the same way: theirs are 1.14 and 1.20 em, their win heights
    /// one em, and Word draws 1.3 em. A face with no OS/2 falls back to the em, which is what
    /// every measured win height was before Malgun.
    /// </remarks>
    private int EastAsianLeading => (int)Math.Round(EastAsianHeight * 0.3);

    /// <summary>The height Word's East Asian leading is proportioned to: the win metrics' sum.</summary>
    private int EastAsianHeight => WinAscent + WinDescent > 0 ? WinAscent + WinDescent : UnitsPerEm;

    /// <summary>
    /// Vertical stem width. There is no table that states this, so it is estimated from the
    /// weight class the way most PDF producers do; consumers use it only for substitution
    /// hinting when the font is missing.
    /// </summary>
    public int StemV => (int)Math.Round(50 + Math.Pow(WeightClass / 65.0, 2));

    /// <summary>Converts a value in design units to points at the given size.</summary>
    public double ToPoints(double designUnits, double fontSizePoints) =>
        designUnits * fontSizePoints / UnitsPerEm;
}
