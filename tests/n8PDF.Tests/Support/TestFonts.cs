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

    /// <summary>A font collection, for exercising the <c>.ttc</c> path.</summary>
    public const string HelveticaCollectionPath = "/System/Library/Fonts/Helvetica.ttc";

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
                     ArialPath, CalibriPath, CalibriBoldPath
                 })
        {
            if (File.Exists(path)) library.RegisterFile(path);
        }

        return library;
    }
}
