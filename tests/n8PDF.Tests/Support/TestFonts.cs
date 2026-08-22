using n8PDF.Diagnostics;
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

    /// <summary>
    /// The face Word sets equations in, which travels as the second face of Cambria's collection
    /// rather than as a file of its own.
    /// </summary>
    public static string CambriaMathPath => Path.Combine(OfficeFonts, "Cambria.ttc");
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

    /// <summary>
    /// The faces a <c>w:sym</c> names, which keep their glyphs in the private-use block rather
    /// than at the characters they look like. These are macOS's own; Symbol travels with Word
    /// instead, and so goes with the rest of Word's faces rather than here.
    /// </summary>
    public static readonly string[] SymbolPaths =
    [
        "/System/Library/Fonts/Supplemental/Wingdings.ttf",
        "/System/Library/Fonts/Supplemental/Wingdings 2.ttf",
        "/System/Library/Fonts/Supplemental/Wingdings 3.ttf",
        "/System/Library/Fonts/Supplemental/Webdings.ttf"
    ];

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

    /// <summary>
    /// The faces that travel with Microsoft Word rather than with macOS, by the names a document
    /// asks for them by. A machine without Word has none of them, and neither does a hosted CI
    /// runner.
    /// </summary>
    private static readonly string[] OfficeFamilies =
        ["Calibri", "Cambria", "MS Mincho", "MS Gothic", "KaiTi", "MingLiU"];

    public static bool Exists(string path) => File.Exists(path);

    /// <summary>Whether the faces that come with Word are on this machine.</summary>
    public static bool OfficeFontsAvailable { get; } = File.Exists(Path.Combine(OfficeFonts, "Calibri.ttf"));

    public static bool OfficeFontsRequired =>
        Environment.GetEnvironmentVariable("N8PDF_REQUIRE_OFFICE_FONTS") == "1";

    public static string OfficeFontsUnavailableMessage =>
        $"The faces that come with Microsoft Word were not found in {OfficeFonts}, so every\n" +
        "fixture that asks for one of them — Calibri, Cambria, MS Mincho, MS Gothic, KaiTi,\n" +
        "MingLiU — is being skipped. Word's own reference PDFs are set in those faces, so\n" +
        "there is nothing to compare against without them.\n" +
        "Set N8PDF_REQUIRE_OFFICE_FONTS=1 to make their absence a failure rather than a skip.";

    /// <summary>
    /// The documents — fixtures and the real ones Word wrote — that are set, somewhere in them, in
    /// a face Word brings with it, and so cannot be rendered as Word rendered them on a machine
    /// that has not got Word.
    /// </summary>
    /// <remarks>
    /// Measured rather than declared: a fixture is on this list when converting it with those
    /// faces and without them gives different files. It cannot be worked out from the package,
    /// which is what a first attempt tried — every fixture here carries a theme naming Calibri
    /// whether a word of it is set in Calibri or not, and that attempt put all hundred and ten on
    /// the list. Nor can it be read off the laid-out document, which records the face an equation
    /// is set in nowhere its runs can be asked for it.
    ///
    /// A list can go stale, so it is regenerated and checked by
    /// <c>OfficeFontTests.The_list_of_fixtures_needing_word_s_faces_is_current</c> wherever the
    /// faces are present — which is every machine that can meaningfully add a fixture.
    /// </remarks>
    private static readonly HashSet<string> WrittenInOfficeFaces =
    [
        "chart-area-scatter", "chart-axis-probe", "chart-bar-scale-probe", "chart-bar-stacked",
        "chart-doughnut-bubble", "chart-kinds-probe", "chart-kinds-probe-two",
        "chart-legend-key-probe", "chart-radar-stock", "merged-indent-probe",
        "cell-direction-probe", "adjacent-tables-probe", "checkbox-probe", "chart-column", "chart-layout-probe", "chart-line-pie", "chart-scale-probe",
        "chart-title-legend-label", "column-order-probe", "columns-uneven", "content-controls", "east-asian-line-box-probe",
        "exact-line-probe", "endnote-restart-section", "endnote-section-end", "endnotes", "equations", "font-fallback",
        "footnote-beneath-text", "footnote-carry-probe", "footnote-columns", "footnote-overrun-probe",
        "footnote-restart-page", "footnote-restart-section", "footnote-separator-probe",
        "floating-table-break-probe", "floating-table-probe", "floating-table-wrap-probe",
        "footnote-split-probe", "footnotes", "hyphenation-probe", "images", "images-formats", "index",
        "inline-picture-line-probe", "kerning",
        "line-ascent-probe", "line-grid-probe", "line-number-probe", "math-bracket-probe", "math-kern-probe",
        "math-line-box-probe", "math-nary-probe", "math-structure-probe", "notes-mixed",
        "numbering", "page-border-probe", "page-numbering-restart", "smartart",
        "ruby-probe", "smartart-lines",
        "superscript-shift-probe", "symbols", "tab-bars", "table-inset-weights-probe",
        "table-heading-probe", "table-vertical-merge", "toc", "vml-stroke-stack-probe",
        "watermark",
        "watermark-fit-probe", "watermark-picture",
        "watermark-washout-probe", "wrapping"
    ];

    /// <summary>Whether a fixture is written in a face that comes with Word.</summary>
    public static bool NeedsOfficeFonts(string fixture) => WrittenInOfficeFaces.Contains(fixture);

    /// <summary>
    /// The documents in Fixtures/Real, which Word wrote and which are compared the same way. They
    /// are set in whatever Word's own defaults are, so they need Word's faces almost by
    /// definition — but the answer is measured with the rest rather than assumed.
    /// </summary>
    public static IEnumerable<(string Name, byte[] Docx)> RealDocuments()
    {
        if (!Directory.Exists(TestPaths.RealFixtures)) yield break;

        foreach (var path in Directory.GetFiles(TestPaths.RealFixtures, "*.docx").OrderBy(p => p))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (!name.StartsWith("~$", StringComparison.Ordinal)) yield return (name, File.ReadAllBytes(path));
        }
    }

    /// <summary>
    /// The same question asked of the documents themselves, by laying each one out twice and
    /// converting it twice. Slow, and only answerable where the faces are installed, which is why
    /// the answer is kept as a list.
    /// </summary>
    /// <remarks>
    /// Both the trace and the file are compared, because they do not always disagree together:
    /// three fixtures — index, watermark-picture and columns-uneven — lay out differently without
    /// Word's faces and still write the same PDF, the difference being in text the file does not
    /// carry.
    /// </remarks>
    public static IEnumerable<string> MeasureWhichNeedOfficeFonts()
    {
        IEnumerable<(string Name, byte[] Docx)> everything =
            [.. Fixtures.All.Keys.Order().Select(name => (name, Fixtures.Build(name))), .. RealDocuments()];

        foreach (var (name, docx) in everything)
        {
            static ConversionOptions Options(bool office) => new()
            {
                Fonts = CreatePinnedLibrary(office), CreationDate = DateTimeOffset.UnixEpoch
            };

            static string Trace(byte[] docx, bool office)
            {
                using var stream = new MemoryStream(docx);
                return LayoutTrace.Write(Converter.LayoutDocument(stream, Options(office)));
            }

            if (Trace(docx, true) != Trace(docx, false) ||
                !Converter.Convert(docx, Options(true)).SequenceEqual(Converter.Convert(docx, Options(false))))
            {
                yield return name;
            }
        }
    }

    /// <summary>
    /// Whether to leave a test alone for want of a face that comes with Word — for the tests that
    /// ask the face itself a question rather than laying a fixture out.
    /// </summary>
    public static bool SkipForMissingFaces()
    {
        if (OfficeFontsAvailable) return false;

        Assert.False(OfficeFontsRequired, OfficeFontsUnavailableMessage);
        return true;
    }

    /// <summary>
    /// Whether to leave a fixture alone for want of the faces it is written in. Absence is a skip
    /// unless the environment says it should be a failure, which is what CI says.
    /// </summary>
    public static bool SkipForMissingFonts(string fixture)
    {
        if (OfficeFontsAvailable || !NeedsOfficeFonts(fixture)) return false;

        Assert.False(OfficeFontsRequired, $"'{fixture}' needs a face Word brings.\n{OfficeFontsUnavailableMessage}");
        return true;
    }

    internal static TrueTypeFont Load(string path)
    {
        Assert.True(File.Exists(path), $"Expected font file not found: {path}");
        return TrueTypeFont.Load(File.ReadAllBytes(path));
    }

    /// <summary>
    /// A library holding only the pinned faces, with system discovery disabled so that results
    /// are reproducible.
    /// </summary>
    public static FontLibrary CreatePinnedLibrary(bool withOfficeFaces = true)
    {
        var library = new FontLibrary { UseSystemFonts = false };

        string[] office =
        [
            CalibriPath, CalibriBoldPath, CambriaMathPath, Mincho, Gothic, Kaiti, MingLiu,
            Path.Combine(OfficeFonts, "symbol.ttf")
        ];

        IEnumerable<string> paths =
        [
            TimesNewRomanPath, TimesNewRomanBoldPath, TimesNewRomanItalicPath, ArialPath, ArialHebrewPath,
            .. withOfficeFaces ? office : [],
            .. SymbolPaths,
            .. ComplexScriptPaths
        ];

        foreach (var path in paths)
        {
            if (File.Exists(path)) library.RegisterFile(path);
        }

        return library;
    }
}
