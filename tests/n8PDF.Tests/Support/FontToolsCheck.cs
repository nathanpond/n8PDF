using System.Diagnostics;

namespace n8PDF.Tests.Support;

/// <summary>What an independent reader made of a font program.</summary>
/// <param name="Glyphs">How many glyphs it found.</param>
/// <param name="Drawn">The drawing operations of each glyph asked about, by glyph index.</param>
public sealed record FontReport(int Glyphs, Dictionary<int, string> Drawn)
{
    /// <summary>How many subroutines the font declares, and how many of them do nothing.</summary>
    public int Subroutines { get; init; }

    public int EmptySubroutines { get; init; }

    /// <summary>Bytes of hinting: the programs, plus the instructions inside the glyphs.</summary>
    public int Hinting { get; init; }

    /// <summary>What each character in the font's own map draws, by code point.</summary>
    public Dictionary<int, string> Characters { get; init; } = [];
}

/// <summary>
/// Reads a font program back with fontTools, as a second opinion on the ones we write.
/// </summary>
/// <remarks>
/// Subsetting rewrites a font's internals — indexes, dictionaries and the offsets between them —
/// and checking that with the same code that wrote it would only prove the two agree. fontTools
/// is an implementation with nothing in common with this one: it parses the file independently
/// and, importantly, <em>executes</em> the charstrings, so a subset whose subroutine calls no
/// longer resolve draws differently rather than merely parsing differently.
///
/// Composites are drawn through to their outlines rather than recorded as references, because a
/// reference carries the name of the glyph it points at and a subset has no names to give.
///
/// It is a developer tool rather than a dependency. When it is not installed these tests report
/// and skip, unless <c>N8PDF_REQUIRE_FONTTOOLS=1</c> turns absence into a failure.
/// </remarks>
public static class FontToolsCheck
{
    private static readonly Lazy<string?> Interpreter = new(Locate);

    public static bool IsAvailable => Interpreter.Value is not null;

    public static bool IsRequired =>
        Environment.GetEnvironmentVariable("N8PDF_REQUIRE_FONTTOOLS") == "1";

    public static string UnavailableMessage =>
        "fontTools was not found, so the font programs were not read back by anything else.\n" +
        "Install it with: python3 -m pip install fonttools\n" +
        "Set N8PDF_REQUIRE_FONTTOOLS=1 to make its absence a failure rather than a skip.";

    /// <summary>
    /// Reads a font program, reporting its glyph count and what the given glyphs draw.
    /// </summary>
    public static FontReport? Read(byte[] program, IEnumerable<int> glyphs)
    {
        if (Interpreter.Value is not { } python) return null;

        var path = Path.Combine(Path.GetTempPath(), $"n8pdf-{Guid.NewGuid():N}.otf");
        File.WriteAllBytes(path, program);

        try
        {
            // Written without interpolation so the script's own braces need no escaping, and
            // reporting one glyph a line so that reading it back needs no parser worth the name.
            var script = """
                import sys, hashlib
                from fontTools.ttLib import TTFont
                from fontTools.pens.recordingPen import DecomposingRecordingPen

                font = TTFont(sys.argv[1])
                order = font.getGlyphOrder()
                glyphs = font.getGlyphSet()

                hinting = 0
                for tag in ("cvt ", "fpgm", "prep"):
                    if tag in font:
                        hinting += len(font.reader[tag])

                if "glyf" in font:
                    glyf = font["glyf"]
                    for name in order:
                        glyph = glyf[name]
                        program = getattr(glyph, "program", None)
                        if program is not None:
                            hinting += len(program.getBytecode())

                print("hinting\t%d" % hinting)

                # What the font's own character map reaches, which is the whole chain: the map,
                # the numbering it points into, and the outline found there.
                for code, name in sorted(font.getBestCmap().items()):
                    pen = DecomposingRecordingPen(glyphs)
                    glyphs[name].draw(pen)
                    drawing = repr(pen.value).encode("utf-8")
                    print("char\t%d\t%d\t%s" % (code, len(pen.value), hashlib.sha256(drawing).hexdigest()))

                if "CFF " not in font:
                    print("subrs\t0\t0")
                    print("glyphs\t%d" % len(order))
                    for index in [GLYPHS]:
                        if index >= len(order):
                            continue
                        pen = DecomposingRecordingPen(glyphs)
                        glyphs[order[index]].draw(pen)
                        drawing = repr(pen.value).encode("utf-8")
                        print("%d\t%d\t%s" % (index, len(pen.value), hashlib.sha256(drawing).hexdigest()))
                    sys.exit(0)

                cff = font["CFF "].cff
                top = cff[cff.fontNames[0]]

                # A subroutine that does nothing is a single return, which is what this writes in
                # place of the ones no glyph reaches.
                subrs = list(top.GlobalSubrs)
                privates = [top.Private] if hasattr(top, "Private") else []
                if hasattr(top, "FDArray"):
                    privates = [fd.Private for fd in top.FDArray]

                for private in privates:
                    subrs += list(getattr(private, "Subrs", []))

                empty = sum(1 for s in subrs if s.bytecode == b"\x0b")

                print("subrs\t%d\t%d" % (len(subrs), empty))
                print("glyphs\t%d" % len(order))
                for index in [GLYPHS]:
                    if index >= len(order):
                        continue
                    pen = DecomposingRecordingPen(glyphs)
                    glyphs[order[index]].draw(pen)
                    drawing = repr(pen.value).encode("utf-8")
                    print("%d\t%d\t%s" % (index, len(pen.value), hashlib.sha256(drawing).hexdigest()))
                """.Replace("GLYPHS", string.Join(", ", glyphs));

            var result = Run(python, ["-c", script, path]);
            if (result is null) return null;

            return Parse(result);
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>
    /// Reads the report: a line giving the glyph count, then one line per glyph asked about with
    /// how many operations it drew and a digest of them.
    /// </summary>
    private static FontReport? Parse(string output)
    {
        var count = 0;
        var subroutines = 0;
        var empty = 0;
        var hinting = 0;
        var drawn = new Dictionary<int, string>();
        var characters = new Dictionary<int, string>();

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Trim().Split('\t');

            if (fields.Length == 2 && fields[0] == "glyphs")
            {
                if (!int.TryParse(fields[1], out count)) return null;
                continue;
            }

            if (fields.Length == 2 && fields[0] == "hinting")
            {
                int.TryParse(fields[1], out hinting);
                continue;
            }

            if (fields.Length == 4 && fields[0] == "char")
            {
                if (int.TryParse(fields[1], out var code))
                    characters[code] = fields[2] == "0" ? "nothing" : fields[3];

                continue;
            }

            if (fields.Length == 3 && fields[0] == "subrs")
            {
                int.TryParse(fields[1], out subroutines);
                int.TryParse(fields[2], out empty);
                continue;
            }

            if (fields.Length != 3 || !int.TryParse(fields[0], out var glyph)) continue;

            // The operation count is what says a glyph drew nothing; the digest is what says two
            // glyphs drew the same thing.
            drawn[glyph] = fields[1] == "0" ? "nothing" : fields[2];
        }

        return count > 0
            ? new FontReport(count, drawn)
            {
                Subroutines = subroutines,
                EmptySubroutines = empty,
                Hinting = hinting,
                Characters = characters
            }
            : null;
    }

    private static string? Locate()
    {
        foreach (var candidate in new[] { "python3", "python" })
        {
            var probe = Run(candidate, ["-c", "import fontTools; print(fontTools.version)"]);
            if (probe is not null) return candidate;
        }

        return null;
    }

    private static string? Run(string executable, string[] arguments)
    {
        try
        {
            var start = new ProcessStartInfo(executable)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            foreach (var argument in arguments) start.ArgumentList.Add(argument);

            using var process = Process.Start(start);
            if (process is null) return null;

            var output = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();

            if (!process.WaitForExit(60_000)) return null;

            return process.ExitCode == 0 ? output : null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException
                                     or IOException or PlatformNotSupportedException)
        {
            return null;
        }
    }
}
