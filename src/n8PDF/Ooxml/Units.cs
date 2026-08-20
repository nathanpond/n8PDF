namespace n8PDF.Ooxml;

/// <summary>
/// Conversions between the measurement units WordprocessingML uses and the points that PDF user
/// space is defined in.
/// </summary>
/// <remarks>
/// A single document mixes at least four units — twips for page and paragraph geometry,
/// half-points for font sizes, eighths of a point for border widths, and EMUs for drawings.
/// They are all plain integers in the XML with nothing to distinguish them, so confusing two is
/// silent and produces output that is subtly and consistently wrong. Every conversion in the
/// library goes through this type rather than through an inline literal.
/// </remarks>
internal static class Units
{
    /// <summary>Twips (twentieths of a point) per point.</summary>
    public const double TwipsPerPoint = 20.0;

    /// <summary>Twips per inch.</summary>
    public const double TwipsPerInch = 1440.0;

    /// <summary>English Metric Units per point.</summary>
    public const double EmuPerPoint = 12700.0;

    /// <summary>English Metric Units per inch.</summary>
    public const double EmuPerInch = 914400.0;

    /// <summary>PDF user-space points per inch.</summary>
    public const double PointsPerInch = 72.0;

    /// <summary>Converts twips to points. Used for page size, margins, indents and spacing.</summary>
    public static double TwipsToPoints(double twips) => twips / TwipsPerPoint;

    public static double PointsToTwips(double points) => points * TwipsPerPoint;

    /// <summary>Converts half-points to points. Font sizes (<c>w:sz</c>) are in half-points.</summary>
    public static double HalfPointsToPoints(double halfPoints) => halfPoints / 2.0;

    public static double PointsToHalfPoints(double points) => points * 2.0;

    /// <summary>
    /// Converts eighths of a point to points. Border widths (<c>w:sz</c> on a border element)
    /// use this unit — the same attribute name as font size, but a different scale.
    /// </summary>
    public static double EighthPointsToPoints(double eighthPoints) => eighthPoints / 8.0;

    /// <summary>Converts English Metric Units to points. Drawing extents are in EMUs.</summary>
    public static double EmuToPoints(double emu) => emu / EmuPerPoint;

    public static double PointsToEmu(double points) => points * EmuPerPoint;

    public static double InchesToPoints(double inches) => inches * PointsPerInch;

    /// <summary>
    /// Converts a fiftieth of a percent to a fraction. Table widths given as <c>pct</c> and
    /// several other percentage attributes use this unit.
    /// </summary>
    public static double FiftiethsOfPercentToFraction(double value) => value / 5000.0;
}
