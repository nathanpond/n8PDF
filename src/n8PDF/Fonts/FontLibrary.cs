namespace n8PDF.Fonts;

/// <summary>The face chosen for a requested family and style.</summary>
/// <param name="Font">The face to measure and embed.</param>
/// <param name="SyntheticBold">
/// True when no bold face was available and weight must be faked by stroking the glyphs.
/// </param>
/// <param name="SyntheticItalic">
/// True when no italic face was available and slant must be faked by shearing the text matrix.
/// </param>
public sealed record FontSelection(TrueTypeFont Font, bool SyntheticBold, bool SyntheticItalic)
{
    /// <summary>True when the face is exactly what was asked for.</summary>
    public bool IsExact => !SyntheticBold && !SyntheticItalic;
}

/// <summary>
/// Resolves the font names appearing in a document to concrete faces. Fonts can be registered
/// explicitly, which is how tests pin exact files and get identical output on any machine, or
/// discovered from the usual system directories.
/// </summary>
public sealed class FontLibrary
{
    private readonly Dictionary<string, List<TrueTypeFont>> _byFamily = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<TrueTypeFont> _all = [];
    private readonly HashSet<string> _loadedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private bool _systemFontsLoaded;

    /// <summary>
    /// When true, a lookup that finds nothing registered falls back to scanning the platform's
    /// font directories. Turn this off for byte-reproducible output.
    /// </summary>
    public bool UseSystemFonts { get; set; } = true;

    /// <summary>
    /// Families tried in order when the requested one is not installed. Word substitutes fonts
    /// silently too, so some chain is unavoidable; these are the safest general-purpose stand-ins.
    /// </summary>
    public List<string> FallbackFamilies { get; } = ["Calibri", "Carlito", "Arial", "Helvetica", "Times New Roman", "Liberation Serif", "DejaVu Sans"];

    public int RegisteredFaceCount
    {
        get
        {
            lock (_gate) return _all.Count;
        }
    }

    public IReadOnlyCollection<string> RegisteredFamilies
    {
        get
        {
            lock (_gate) return _byFamily.Keys.ToArray();
        }
    }

    /// <summary>Registers every face in the given font file data.</summary>
    public void Register(byte[] data)
    {
        foreach (var face in TrueTypeFont.LoadAll(data))
            Add(face);
    }

    public void RegisterFile(string path)
    {
        lock (_gate)
        {
            if (!_loadedPaths.Add(path)) return;
        }

        Register(File.ReadAllBytes(path));
    }

    /// <summary>Registers every font file in a directory. Unreadable files are skipped.</summary>
    public int RegisterDirectory(string path, bool recursive = false)
    {
        if (!Directory.Exists(path)) return 0;

        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var added = 0;

        foreach (var file in Directory.EnumerateFiles(path, "*", option))
        {
            var extension = Path.GetExtension(file);
            if (!IsFontExtension(extension)) continue;

            try
            {
                RegisterFile(file);
                added++;
            }
            catch (Exception e) when (e is FontFormatException or IOException or UnauthorizedAccessException)
            {
                // A font directory is a shared space that routinely contains files we cannot
                // parse or read. One bad file must not take out the whole scan.
            }
        }

        return added;
    }

    /// <summary>
    /// Resolves a family name and style to a face, falling back through the substitution chain
    /// and finally to any registered font rather than failing.
    /// </summary>
    public FontSelection Resolve(string familyName, bool bold = false, bool italic = false)
    {
        if (TryResolve(familyName, bold, italic, out var selection))
            return selection;

        throw new FontFormatException(
            $"No font could be resolved for '{familyName}'. Register a font file, or enable {nameof(UseSystemFonts)}.");
    }

    public bool TryResolve(string familyName, bool bold, bool italic, out FontSelection selection)
    {
        if (TryResolveExactFamily(familyName, bold, italic, out selection))
            return true;

        // The requested family is not present, so walk the substitution chain.
        foreach (var fallback in FallbackFamilies)
        {
            if (!string.Equals(fallback, familyName, StringComparison.OrdinalIgnoreCase) &&
                TryResolveExactFamily(fallback, bold, italic, out selection))
                return true;
        }

        // Last resort: anything at all beats producing no text.
        lock (_gate)
        {
            if (_all.Count > 0)
            {
                selection = Select(_all, bold, italic);
                return true;
            }
        }

        selection = null!;
        return false;
    }

    /// <summary>
    /// A face that can draw a character, preferring the one already chosen for the run.
    /// </summary>
    /// <remarks>
    /// A font is not obliged to hold every character, and most hold very few: Arial Hebrew has no
    /// Latin letters at all, and Times New Roman has no Chinese. Asked to set a character its own
    /// face has no glyph for, a converter that does nothing about it draws the empty box every
    /// font keeps at glyph zero — or, worse, nothing at all — and the document quietly loses text
    /// it plainly holds. So another face is found for that character and the run is set in two.
    ///
    /// Which face is a matter of taste rather than of correctness, and the taste here is the
    /// document's: the substitution chain a missing family already walks is walked again, so a
    /// document whose text is Calibri and whose Hebrew is not borrows the Hebrew from whatever
    /// stands next in that chain. Only where none of them can draw it is everything else tried,
    /// and then in a fixed order, so that the same document does not come out differently on two
    /// machines with the same fonts installed in a different order.
    /// </remarks>
    public FontSelection? ResolveForCharacter(int codePoint, FontSelection preferred, bool bold, bool italic)
    {
        if (Covers(preferred.Font, codePoint)) return preferred;

        if (UseSystemFonts) EnsureSystemFontsLoaded();

        foreach (var family in FallbackFamilies)
        {
            if (!TryResolveExactFamily(family, bold, italic, out var candidate)) continue;
            if (Covers(candidate.Font, codePoint)) return candidate;
        }

        lock (_gate)
        {
            var ordered = _all
                .Where(font => Covers(font, codePoint))
                .OrderBy(font => font.FamilyName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (ordered.Count > 0) return Select(ordered, bold, italic);
        }

        return null;
    }

    /// <summary>Whether a face has a glyph of its own for a character.</summary>
    private static bool Covers(TrueTypeFont font, int codePoint) => font.GetGlyphIndex(codePoint) != 0;

    private bool TryResolveExactFamily(string familyName, bool bold, bool italic, out FontSelection selection)
    {
        selection = null!;
        if (string.IsNullOrWhiteSpace(familyName)) return false;

        var candidates = FindFamily(familyName);
        if (candidates is null && UseSystemFonts)
        {
            EnsureSystemFontsLoaded();
            candidates = FindFamily(familyName);
        }

        if (candidates is null) return false;

        selection = Select(candidates, bold, italic);
        return true;
    }

    private List<TrueTypeFont>? FindFamily(string familyName)
    {
        lock (_gate)
        {
            if (_byFamily.TryGetValue(familyName, out var exact))
                return [.. exact];

            // Word writes names like "Arial Narrow" as a family, while the font's own family may
            // be "Arial" with subfamily "Narrow". Normalising both sides catches that.
            var normalized = Normalize(familyName);
            return _byFamily.TryGetValue(normalized, out var normalizedMatch) ? [.. normalizedMatch] : null;
        }
    }

    /// <summary>
    /// Picks the best face for the requested style. An exact match wins; otherwise the closest
    /// face is taken and the missing attributes are flagged for synthesis.
    /// </summary>
    private static FontSelection Select(List<TrueTypeFont> candidates, bool bold, bool italic)
    {
        TrueTypeFont? best = null;
        var bestScore = int.MinValue;

        foreach (var candidate in candidates)
        {
            // Matching italic matters more than matching weight: a synthetic slant is far more
            // visible than a synthetic bold.
            var score = 0;
            if (candidate.IsItalic == italic) score += 4;
            if (candidate.IsBold == bold) score += 2;

            // Prefer the nearest weight among faces that all claim the same bold flag.
            var targetWeight = bold ? 700 : 400;
            score -= Math.Abs(candidate.Metrics.WeightClass - targetWeight) / 100;

            if (score <= bestScore) continue;
            bestScore = score;
            best = candidate;
        }

        best ??= candidates[0];
        return new FontSelection(best, bold && !best.IsBold, italic && !best.IsItalic);
    }

    private void Add(TrueTypeFont font)
    {
        lock (_gate)
        {
            _all.Add(font);
            AddToFamily(font.FamilyName, font);

            var normalized = Normalize(font.FamilyName);
            if (!string.Equals(normalized, font.FamilyName, StringComparison.OrdinalIgnoreCase))
                AddToFamily(normalized, font);
        }
    }

    private void AddToFamily(string family, TrueTypeFont font)
    {
        if (!_byFamily.TryGetValue(family, out var list))
        {
            list = [];
            _byFamily[family] = list;
        }

        list.Add(font);
    }

    private void EnsureSystemFontsLoaded()
    {
        lock (_gate)
        {
            if (_systemFontsLoaded) return;
            _systemFontsLoaded = true;
        }

        foreach (var directory in GetSystemFontDirectories())
            RegisterDirectory(directory);
    }

    /// <summary>
    /// Platform font directories, most specific first. On macOS this deliberately includes the
    /// fonts Office installs inside the Word bundle: Calibri and Cambria are the default fonts
    /// of most Word documents and live nowhere else on the system.
    /// </summary>
    public static IReadOnlyList<string> GetSystemFontDirectories()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var directories = new List<string>();

        if (OperatingSystem.IsMacOS())
        {
            directories.AddRange([
                Path.Combine(home, "Library", "Fonts"),
                "/Library/Fonts",
                "/System/Library/Fonts",
                "/System/Library/Fonts/Supplemental",
                "/Applications/Microsoft Word.app/Contents/Resources/DFonts",
                "/Library/Application Support/Microsoft/Office365/User Content.localized/Fonts"
            ]);
        }
        else if (OperatingSystem.IsWindows())
        {
            directories.AddRange([
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts")
            ]);
        }
        else
        {
            directories.AddRange([
                Path.Combine(home, ".fonts"),
                Path.Combine(home, ".local", "share", "fonts"),
                "/usr/local/share/fonts",
                "/usr/share/fonts"
            ]);
        }

        return directories;
    }

    private static bool IsFontExtension(string extension) =>
        extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".otf", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".ttc", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".otc", StringComparison.OrdinalIgnoreCase);

    private static readonly string[] StyleSuffixes =
        ["regular", "bold", "italic", "oblique", "light", "medium", "semibold", "black", "thin"];

    /// <summary>
    /// Strips trailing style words so that "Arial Bold" and "Arial" index to the same family.
    /// </summary>
    private static string Normalize(string familyName)
    {
        var words = familyName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var end = words.Length;

        while (end > 1 && Array.Exists(StyleSuffixes, s => s.Equals(words[end - 1], StringComparison.OrdinalIgnoreCase)))
            end--;

        return string.Join(' ', words, 0, end);
    }
}
