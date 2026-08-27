namespace n8PDF.Fonts;

/// <summary>The face chosen for a requested family and style.</summary>
/// <param name="Font">The face to measure and embed.</param>
/// <param name="SyntheticBold">
/// True when no bold face was available and weight must be faked by stroking the glyphs.
/// </param>
/// <param name="SyntheticItalic">
/// True when no italic face was available and slant must be faked by shearing the text matrix.
/// </param>
internal sealed record FontSelection(TrueTypeFont Font, bool SyntheticBold, bool SyntheticItalic)
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
    /// <summary>
    /// One face the library knows of: what it is called and how it is styled, and where to read
    /// it from when something actually wants it.
    /// </summary>
    /// <remarks>
    /// A face has to be named and styled to be chosen between, and nothing more: which of a
    /// family's faces a document wants is decided by the weight and the slant alone. Reading the
    /// rest of it — the outlines, the tables that shape a script, the kerning — costs what the
    /// file costs, and the platform's font directories on this machine hold 1.3GB of them. So a
    /// face is indexed by its name and read by its use, and a document that sets two families
    /// reads two files rather than six hundred.
    /// </remarks>
    private sealed class Face
    {
        private readonly Lazy<TrueTypeFont> _font;

        /// <summary>A face already in hand, which is what registering one outright gives.</summary>
        public Face(TrueTypeFont font)
        {
            _font = new Lazy<TrueTypeFont>(font);

            Family = font.FamilyName;
            IsBold = font.IsBold;
            IsItalic = font.IsItalic;
            WeightClass = font.Metrics.WeightClass;
        }

        /// <summary>A face known of but not yet read.</summary>
        public Face(string path, int index, TrueTypeFont identity)
        {
            _font = new Lazy<TrueTypeFont>(
                () => TrueTypeFont.Load(File.ReadAllBytes(path), index),
                LazyThreadSafetyMode.ExecutionAndPublication);

            Family = identity.FamilyName;
            IsBold = identity.IsBold;
            IsItalic = identity.IsItalic;
            WeightClass = identity.Metrics.WeightClass;
        }

        public string Family { get; }

        public bool IsBold { get; }

        public bool IsItalic { get; }

        public int WeightClass { get; }

        public TrueTypeFont Font => _font.Value;

        /// <summary>
        /// True for a face the document itself carried (#62). An embedded face outranks an
        /// installed face of the same name: the author embedded it precisely so that this
        /// machine's own fonts would not be consulted.
        /// </summary>
        public bool Embedded { get; init; }

        /// <summary>Whether the face has been read from its file yet, rather than merely known of.</summary>
        public bool IsRead => _font.IsValueCreated;
    }

    private readonly Dictionary<string, List<Face>> _byFamily = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Face> _all = [];
    private readonly HashSet<string> _loadedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private bool _systemFontsLoaded;

    /// <summary>
    /// Every face the platform's font directories hold, read once for the whole process.
    /// </summary>
    /// <remarks>
    /// The scan itself is the expensive part — six hundred files and a second of reading on the
    /// machine this was measured on — and it gives the same answer every time. Sharing the result
    /// is what makes a conversion that says nothing about fonts cost a millisecond rather than
    /// half a second, and what stops a hundred conversions reading the same 1.3GB a hundred times.
    ///
    /// What is shared is the index, and the faces in it read themselves on demand — so two
    /// libraries that both want Calibri end up with the same face rather than two of it, and a
    /// library that wants nothing reads nothing.
    /// </remarks>
    private static readonly Lazy<IReadOnlyList<Face>> SystemFaces =
        new(ScanSystemFonts, LazyThreadSafetyMode.ExecutionAndPublication);

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

    /// <summary>
    /// How many of this library's faces have been read from their files, rather than merely known
    /// of by name.
    /// </summary>
    /// <remarks>
    /// A face indexed from a directory learns its name at the scan and reads the rest of itself
    /// only when something asks for the face, so this is nought after a scan and one after
    /// resolving one family. It is what FontLibraryCacheTests asserts: the saving that makes a
    /// conversion setting two families read two files rather than six hundred is exactly the
    /// difference between this number and the face count.
    /// </remarks>
    internal int FacesRead
    {
        get
        {
            lock (_gate) return _all.Count(face => face.IsRead);
        }
    }

    public IReadOnlyCollection<string> RegisteredFamilies
    {
        get
        {
            lock (_gate) return _byFamily.Keys.ToArray();
        }
    }

    /// <summary>
    /// A copy for one conversion of a document that carries its own faces (#62): the embedded
    /// faces are registered on the copy, so the caller's library is not permanently taught the
    /// fonts of one document it converted.
    /// </summary>
    internal FontLibrary(FontLibrary source)
    {
        lock (source._gate)
        {
            _all.AddRange(source._all);
            foreach (var (family, faces) in source._byFamily)
                _byFamily[family] = [.. faces];
            _loadedPaths.UnionWith(source._loadedPaths);
            _systemFontsLoaded = source._systemFontsLoaded;
        }

        UseSystemFonts = source.UseSystemFonts;
        FallbackFamilies.Clear();
        FallbackFamilies.AddRange(source.FallbackFamilies);
    }

    public FontLibrary()
    {
    }

    /// <summary>Registers every face in a font the document itself carried (#62).</summary>
    internal void RegisterEmbedded(byte[] data)
    {
        foreach (var face in TrueTypeFont.LoadAll(data))
            Add(new Face(face) { Embedded = true });
    }

    /// <summary>Registers every face in the given font file data.</summary>
    public void Register(byte[] data)
    {
        try
        {
            foreach (var face in TrueTypeFont.LoadAll(data))
                Add(new Face(face));
        }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentException
                                      or OverflowException or DivideByZeroException
                                      or InvalidDataException or InvalidOperationException)
        {
            // A crafted font can drive the table parsers past their own checks into a raw runtime
            // exception — a cmap offset whose top bit is set overflows the (int) cast under the
            // library's checked arithmetic, for one (#282). Register's contract is to validate a
            // face and report a malformed one as a FontFormatException, so the net turns those into
            // it, the same defence in depth the image reader keeps behind its decoders (#48).
            throw new FontFormatException($"The font data is malformed: {e.Message}");
        }
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
    internal FontSelection Resolve(string familyName, bool bold = false, bool italic = false)
    {
        if (TryResolve(familyName, bold, italic, out var selection))
            return selection;

        throw new FontFormatException(
            $"No font could be resolved for '{familyName}'. Register a font file, or enable {nameof(UseSystemFonts)}.");
    }

    internal bool TryResolve(string familyName, bool bold, bool italic, out FontSelection selection)
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
                selection = Select([.. _all], bold, italic);
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
    internal FontSelection? ResolveForCharacter(int codePoint, FontSelection preferred, bool bold, bool italic)
    {
        if (Covers(preferred.Font, codePoint)) return preferred;

        if (UseSystemFonts) EnsureSystemFontsLoaded();

        foreach (var family in FallbackFamilies)
        {
            if (!TryResolveExactFamily(family, bold, italic, out var candidate)) continue;
            if (Covers(candidate.Font, codePoint)) return candidate;
        }

        // Every face there is, in a fixed order, until one of them can draw the character. This
        // is the one path that reads faces it will not use, and it is only reached by a character
        // nothing in the substitution chain covers.
        List<Face> everything;
        lock (_gate) everything = [.. _all];

        var ordered = everything
            .OrderBy(face => face.Family, StringComparer.OrdinalIgnoreCase)
            .Where(face => Covers(face.Font, codePoint))
            .ToList();

        if (ordered.Count > 0) return Select(ordered, bold, italic);

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

    private List<Face>? FindFamily(string familyName)
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
    private static FontSelection Select(List<Face> candidates, bool bold, bool italic)
    {
        Face? best = null;
        var bestScore = int.MinValue;

        foreach (var candidate in candidates)
        {
            // Matching italic matters more than matching weight: a synthetic slant is far more
            // visible than a synthetic bold.
            var score = 0;
            if (candidate.IsItalic == italic) score += 4;
            if (candidate.IsBold == bold) score += 2;

            // A face the document carried beats any installed face, whatever the style match:
            // Word embeds each face a document actually uses, so a missing style is synthesised
            // from the embedded family rather than borrowed from the machine's (#62).
            if (candidate.Embedded) score += 16;

            // Prefer the nearest weight among faces that all claim the same bold flag.
            var targetWeight = bold ? 700 : 400;
            score -= Math.Abs(candidate.WeightClass - targetWeight) / 100;

            if (score <= bestScore) continue;
            bestScore = score;
            best = candidate;
        }

        best ??= candidates[0];
        return new FontSelection(best.Font, bold && !best.IsBold, italic && !best.IsItalic);
    }

    private void Add(Face face)
    {
        lock (_gate)
        {
            _all.Add(face);
            AddToFamily(face.Family, face);

            var normalized = Normalize(face.Family);
            if (!string.Equals(normalized, face.Family, StringComparison.OrdinalIgnoreCase))
                AddToFamily(normalized, face);
        }
    }

    private void AddToFamily(string family, Face face)
    {
        if (!_byFamily.TryGetValue(family, out var list))
        {
            list = [];
            _byFamily[family] = list;
        }

        list.Add(face);
    }

    private void EnsureSystemFontsLoaded()
    {
        lock (_gate)
        {
            if (_systemFontsLoaded) return;
            _systemFontsLoaded = true;
        }

        foreach (var face in SystemFaces.Value) Add(face);
    }

    /// <summary>
    /// Every face in the platform's font directories, named and styled but not read.
    /// </summary>
    /// <remarks>
    /// A file is read here to be named — there is nowhere else the name is written — and let go of
    /// again, so that what is kept is the index rather than the fonts. A directory of fonts is a
    /// shared space that routinely holds files that cannot be parsed or read, and one of those
    /// must not take out the whole scan.
    /// </remarks>
    private static IReadOnlyList<Face> ScanSystemFonts() => Scan(GetSystemFontDirectories());

    /// <summary>
    /// Indexes every font file in a directory without holding what it read: each face learns its
    /// name, its style and where it lives, and reads the rest of itself only when something asks
    /// for the face. That is what the platform's own directories are taken in by, and the
    /// difference from <see cref="RegisterDirectory"/>, which reads a directory outright, is the
    /// 1.3GB those directories hold on the machine this was measured on.
    /// </summary>
    /// <returns>How many faces were indexed.</returns>
    internal int IndexDirectory(string path)
    {
        var faces = Scan([path]);

        foreach (var face in faces) Add(face);

        return faces.Count;
    }

    private static IReadOnlyList<Face> Scan(IReadOnlyList<string> directories)
    {
        var faces = new List<Face>();

        foreach (var directory in directories)
        {
            if (!Directory.Exists(directory)) continue;

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                if (!IsFontExtension(Path.GetExtension(file))) continue;

                try
                {
                    var data = File.ReadAllBytes(file);

                    for (var index = 0; index < TrueTypeFont.GetFaceCount(data); index++)
                    {
                        try
                        {
                            faces.Add(new Face(file, index, TrueTypeFont.Load(data, index)));
                        }
                        catch (FontFormatException)
                        {
                            // One malformed face in a collection leaves the rest usable.
                        }
                    }
                }
                catch (Exception e) when (e is FontFormatException or IOException or UnauthorizedAccessException)
                {
                }
            }
        }

        return faces;
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
