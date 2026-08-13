using n8PDF.Fonts;

namespace n8PDF.Tests.Support;

/// <summary>
/// Font files the test suite pins by path. Tests never resolve through the ambient system font
/// set — goldens would then depend on which machine ran them.
/// </summary>
public static class TestFonts
{
    public const string TimesNewRomanPath = "/System/Library/Fonts/Supplemental/Times New Roman.ttf";
    public const string TimesNewRomanBoldPath = "/System/Library/Fonts/Supplemental/Times New Roman Bold.ttf";
    public const string TimesNewRomanItalicPath = "/System/Library/Fonts/Supplemental/Times New Roman Italic.ttf";
    public const string ArialPath = "/System/Library/Fonts/Supplemental/Arial.ttf";

    private const string OfficeFonts = "/Applications/Microsoft Word.app/Contents/Resources/DFonts";

    public static string CalibriPath => Path.Combine(OfficeFonts, "Calibri.ttf");
    public static string CalibriBoldPath => Path.Combine(OfficeFonts, "Calibrib.ttf");

    /// <summary>
    /// The Japanese and Chinese faces, taken from Word's own font folder rather than the system's.
    /// Asked for a Japanese face macOS has and Word has not, Word draws the line in one of these
    /// instead — so these are the faces the reference exports are set in, and pinning them is what
    /// makes the two sides comparable at all.
    /// </summary>
    public static string Mincho => Path.Combine(OfficeFonts, "msmincho.ttc");

    public static string Gothic => Path.Combine(OfficeFonts, "msgothic.ttc");

    public static string Kaiti => Path.Combine(OfficeFonts, "Kaiti.ttf");

    public static string MingLiu => Path.Combine(OfficeFonts, "mingliu.ttc");

    /// <summary>A font collection, for exercising the <c>.ttc</c> path.</summary>
    public const string HelveticaCollectionPath = "/System/Library/Fonts/Helvetica.ttc";

    /// <summary>
    /// A face with Hebrew and no Latin whatever, which is what makes it worth pinning: it is the
    /// case a document meets when it asks one font for two scripts.
    /// </summary>
    public const string ArialHebrewPath = "/System/Library/Fonts/ArialHB.ttc";

    /// <summary>
    /// The faces for the scripts that are shaped rather than merely drawn. Pinned like the rest,
    /// and for a second reason as well: which face a machine happens to hold for a script it
    /// cannot otherwise write is exactly the kind of thing that would make a golden differ from
    /// one machine to the next.
    /// </summary>
    public static readonly string[] ComplexScriptPaths =
    [
        "/System/Library/Fonts/Supplemental/Devanagari Sangam MN.ttc",
        "/System/Library/Fonts/Supplemental/Tamil Sangam MN.ttc",
        "/System/Library/Fonts/Supplemental/Bangla Sangam MN.ttc",
        "/System/Library/Fonts/Supplemental/Gurmukhi Sangam MN.ttc",
        "/System/Library/Fonts/Supplemental/Gujarati Sangam MN.ttc",
        "/System/Library/Fonts/Supplemental/Oriya Sangam MN.ttc",
        "/System/Library/Fonts/Supplemental/Telugu Sangam MN.ttc",
        "/System/Library/Fonts/Supplemental/Kannada Sangam MN.ttc",
        "/System/Library/Fonts/Supplemental/Malayalam Sangam MN.ttc",
        "/System/Library/Fonts/Supplemental/Ayuthaya.ttf",
        "/System/Library/Fonts/Supplemental/Lao Sangam MN.ttf",
        "/System/Library/Fonts/Supplemental/Khmer Sangam MN.ttf",
        "/System/Library/Fonts/NotoSansMyanmar.ttc",
        "/System/Library/Fonts/Supplemental/Sinhala Sangam MN.ttc",
        "/System/Library/Fonts/Supplemental/Kailasa.ttc",
        "/System/Library/Fonts/Supplemental/NotoSansJavanese-Regular.otf",
        "/System/Library/Fonts/Supplemental/NotoSansCham-Regular.ttf",

        // And the faces that describe their shaping only in Apple's own tables.
        "/System/Library/Fonts/Supplemental/DevanagariMT.ttc",
        "/System/Library/Fonts/Supplemental/GujaratiMT.ttc",
        "/System/Library/Fonts/Supplemental/Gurmukhi.ttf",
        "/System/Library/Fonts/Supplemental/Thonburi.ttc"
    ];

    public static bool Exists(string path) => File.Exists(path);

    public static TrueTypeFont Load(string path)
    {
        Assert.True(File.Exists(path), $"Expected font file not found: {path}");
        return TrueTypeFont.Load(File.ReadAllBytes(path));
    }

    /// <summary>
    /// A library holding only the pinned faces, with system discovery disabled so that results
    /// are reproducible.
    /// </summary>
    public static FontLibrary CreatePinnedLibrary()
    {
        var library = new FontLibrary { UseSystemFonts = false };

        foreach (var path in new[]
                 {
                     TimesNewRomanPath, TimesNewRomanBoldPath, TimesNewRomanItalicPath,
                     ArialPath, CalibriPath, CalibriBoldPath, ArialHebrewPath,
                     Mincho, Gothic, Kaiti, MingLiu
                 }.Concat(ComplexScriptPaths))
        {
            if (File.Exists(path)) library.RegisterFile(path);
        }

        return library;
    }
}
