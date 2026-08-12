using System.Globalization;
using System.Text;

namespace n8PDF.Tests.Support.PdfReading;

/// <summary>
/// The parts of a font resource needed to turn show-text bytes back into characters and advances.
/// </summary>
/// <remarks>
/// Both encodings that matter are handled. Word writes simple <c>/TrueType</c> fonts: one byte per
/// code, subset-remapped so the codes start at 33 and mean nothing on their own, with widths in a
/// <c>/Widths</c> array indexed from <c>/FirstChar</c>. n8PDF writes composite <c>/Type0</c> fonts
/// with Identity-H: two bytes per code, the code being the glyph index, with widths in a <c>/W</c>
/// array. In both cases the text is only recoverable through the <c>/ToUnicode</c> CMap.
/// </remarks>
public sealed class PdfFontInfo
{
    private readonly Dictionary<int, string> _toUnicode = [];
    private readonly Dictionary<int, double> _widths = [];

    public required string ResourceName { get; init; }

    public string BaseFont { get; init; } = "Unknown";

    /// <summary>Bytes per character code: 2 for Identity-H composite fonts, 1 otherwise.</summary>
    public int BytesPerCode { get; init; } = 1;

    /// <summary>Default width in text-space thousandths for codes not listed.</summary>
    public double DefaultWidth { get; init; } = 500;

    /// <summary>
    /// The font family, with any subset prefix removed. Word writes "AAAAAC+TimesNewRomanPSMT";
    /// the six-letter tag is arbitrary and differs between exports of the same document.
    /// </summary>
    public string FamilyName
    {
        get
        {
            var name = BaseFont;
            var plus = name.IndexOf('+');
            if (plus == 6) name = name[(plus + 1)..];

            // Strip the PostScript style suffixes so "TimesNewRomanPSMT" and "TimesNewRomanPS-BoldMT"
            // both reduce to something comparable.
            foreach (var suffix in new[] { "PSMT", "PS-BoldMT", "PS-ItalicMT", "PS-BoldItalicMT", "MT", "PS" })
            {
                if (name.EndsWith(suffix, StringComparison.Ordinal))
                {
                    name = name[..^suffix.Length];
                    break;
                }
            }

            return name.TrimEnd('-');
        }
    }

    /// <summary>Splits a show-text string into character codes.</summary>
    public IEnumerable<int> DecodeCodes(byte[] bytes)
    {
        if (BytesPerCode == 2)
        {
            for (var i = 0; i + 1 < bytes.Length; i += 2)
                yield return (bytes[i] << 8) | bytes[i + 1];

            yield break;
        }

        foreach (var b in bytes) yield return b;
    }

    /// <summary>Maps a code to its text, falling back to the code itself when unmapped.</summary>
    public string GetText(int code)
    {
        if (_toUnicode.TryGetValue(code, out var text)) return text;

        // Without a ToUnicode entry a single-byte code is usually its own character.
        return BytesPerCode == 1 && code is >= 32 and < 127 ? ((char)code).ToString() : string.Empty;
    }

    /// <summary>Advance width for a code, in text-space thousandths.</summary>
    public double GetWidth(int code) => _widths.GetValueOrDefault(code, DefaultWidth);

    public bool HasToUnicode => _toUnicode.Count > 0;

    /// <summary>Builds a font from its resource dictionary.</summary>
    public static PdfFontInfo Load(PdfFileReader reader, string resourceName, PdfDictValue font)
    {
        var subtype = (reader.GetEntry(font, "Subtype") as PdfNameValue)?.Name ?? string.Empty;
        var baseFont = (reader.GetEntry(font, "BaseFont") as PdfNameValue)?.Name ?? "Unknown";

        var isComposite = subtype == "Type0";
        var encoding = (reader.GetEntry(font, "Encoding") as PdfNameValue)?.Name;

        // Identity-H and Identity-V are two-byte encodings. Other composite encodings use a CMap
        // that would need parsing; none of the documents under test use one.
        var bytesPerCode = isComposite && encoding is null or "Identity-H" or "Identity-V" ? 2 : 1;

        var info = new PdfFontInfo
        {
            ResourceName = resourceName,
            BaseFont = baseFont,
            BytesPerCode = bytesPerCode,
            DefaultWidth = isComposite ? 1000 : 500
        };

        if (reader.GetEntry(font, "ToUnicode") is PdfStreamValue toUnicode)
            info.ParseToUnicode(Encoding.Latin1.GetString(reader.DecodeStream(toUnicode)));

        if (isComposite) info.LoadCompositeWidths(reader, font);
        else info.LoadSimpleWidths(reader, font);

        return info;
    }

    /// <summary>Reads /FirstChar and /Widths, the simple-font width form.</summary>
    private void LoadSimpleWidths(PdfFileReader reader, PdfDictValue font)
    {
        var firstChar = reader.GetEntry(font, "FirstChar") is PdfNumberValue f ? f.AsInt : 0;
        if (reader.GetEntry(font, "Widths") is not PdfArrayValue widths) return;

        for (var i = 0; i < widths.Count; i++)
            _widths[firstChar + i] = PdfFileReader.ToDouble(reader.Resolve(widths[i]));
    }

    /// <summary>
    /// Reads the descendant font's /W array, which has two forms: "c [w1 w2 …]" listing
    /// consecutive codes, and "cFirst cLast w" giving one width to a whole range.
    /// </summary>
    private void LoadCompositeWidths(PdfFileReader reader, PdfDictValue font)
    {
        if (reader.GetEntry(font, "DescendantFonts") is not PdfArrayValue descendants || descendants.Count == 0)
            return;

        if (reader.Resolve(descendants[0]) is not PdfDictValue descendant) return;

        if (reader.GetEntry(descendant, "W") is not PdfArrayValue w) return;

        var index = 0;
        while (index < w.Count)
        {
            if (reader.Resolve(w[index]) is not PdfNumberValue first) break;
            if (index + 1 >= w.Count) break;

            var next = reader.Resolve(w[index + 1]);

            if (next is PdfArrayValue list)
            {
                for (var i = 0; i < list.Count; i++)
                    _widths[first.AsInt + i] = PdfFileReader.ToDouble(reader.Resolve(list[i]));

                index += 2;
                continue;
            }

            if (next is PdfNumberValue last && index + 2 < w.Count &&
                reader.Resolve(w[index + 2]) is PdfNumberValue width)
            {
                // A range can legitimately be large; cap the expansion so a malformed file cannot
                // allocate without bound.
                var count = Math.Min(last.AsInt - first.AsInt, 65535);
                for (var i = 0; i <= count; i++)
                    _widths[first.AsInt + i] = width.Value;

                index += 3;
                continue;
            }

            index++;
        }
    }

    /// <summary>
    /// Parses a ToUnicode CMap. Only the two sections that carry mappings are interpreted:
    /// <c>beginbfchar</c> for individual codes and <c>beginbfrange</c> for runs.
    /// </summary>
    private void ParseToUnicode(string cmap)
    {
        var tokens = Tokenize(cmap);

        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i] == "beginbfchar")
            {
                i++;
                while (i + 1 < tokens.Count && tokens[i] != "endbfchar")
                {
                    if (TryHex(tokens[i], out var code) && IsHex(tokens[i + 1]))
                        _toUnicode[code] = HexToText(tokens[i + 1]);

                    i += 2;
                }

                continue;
            }

            if (tokens[i] != "beginbfrange") continue;

            i++;
            while (i + 2 < tokens.Count && tokens[i] != "endbfrange")
            {
                if (!TryHex(tokens[i], out var low) || !TryHex(tokens[i + 1], out var high))
                {
                    i += 3;
                    continue;
                }

                var destination = tokens[i + 2];

                if (destination.StartsWith('['))
                {
                    // The array form gives an explicit value per code in the range.
                    var values = destination.Trim('[', ']').Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    for (var c = low; c <= high && c - low < values.Length; c++)
                        _toUnicode[c] = HexToText(values[c - low]);
                }
                else if (IsHex(destination))
                {
                    // The scalar form gives the first value, incrementing across the range.
                    var text = HexToText(destination);
                    if (text.Length > 0)
                    {
                        var start = char.ConvertToUtf32(text, 0);
                        var count = Math.Min(high - low, 65535);
                        for (var offset = 0; offset <= count; offset++)
                        {
                            var scalar = start + offset;
                            if (scalar is >= 0 and <= 0x10ffff && (scalar < 0xd800 || scalar > 0xdfff))
                                _toUnicode[low + offset] = char.ConvertFromUtf32(scalar);
                        }
                    }
                }

                i += 3;
            }
        }
    }

    /// <summary>Splits CMap source into hex strings, arrays and bare keywords.</summary>
    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var i = 0;

        while (i < text.Length)
        {
            var ch = text[i];

            if (char.IsWhiteSpace(ch))
            {
                i++;
                continue;
            }

            if (ch == '<')
            {
                var end = text.IndexOf('>', i);
                if (end < 0) break;

                tokens.Add(text[i..(end + 1)]);
                i = end + 1;
                continue;
            }

            if (ch == '[')
            {
                var end = text.IndexOf(']', i);
                if (end < 0) break;

                // Normalise the array's contents to space-separated hex strings.
                var inner = text[(i + 1)..end]
                    .Replace("<", " ").Replace(">", " ");
                tokens.Add("[" + string.Join(' ',
                    inner.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(v => "<" + v + ">")) + "]");
                i = end + 1;
                continue;
            }

            var start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '<' && text[i] != '[') i++;

            if (i == start) i++;
            else tokens.Add(text[start..i]);
        }

        return tokens;
    }

    private static bool IsHex(string token) => token.StartsWith('<') && token.EndsWith('>');

    private static bool TryHex(string token, out int value)
    {
        value = 0;
        if (!IsHex(token)) return false;

        var inner = token[1..^1];
        return int.TryParse(inner, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Converts a hex CMap value, which is UTF-16BE, into text.</summary>
    private static string HexToText(string token)
    {
        if (!IsHex(token)) return string.Empty;

        var inner = token[1..^1];
        if (inner.Length % 4 != 0 && inner.Length % 2 == 0 && inner.Length < 4)
        {
            // A single byte value; treat it as Latin-1.
            return int.TryParse(inner, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var single)
                ? ((char)single).ToString()
                : string.Empty;
        }

        var sb = new StringBuilder();
        for (var i = 0; i + 3 < inner.Length; i += 4)
        {
            if (int.TryParse(inner.AsSpan(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var unit))
                sb.Append((char)unit);
        }

        return sb.ToString();
    }
}
