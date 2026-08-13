using n8PDF.Fonts.OpenType;
using n8PDF.Text;

namespace n8PDF.Fonts;

/// <summary>
/// The plan for Khmer, which stacks its consonants and writes some of its vowels to the left.
/// </summary>
/// <remarks>
/// Khmer descends from the same writing as the Indic scripts and reorders for the same reasons,
/// but it says so more plainly. A consonant with no vowel of its own is written under the one
/// before it, and it is marked as such by a character of its own — the coeng — rather than by the
/// absence of a vowel; and where the letter under is an r, it is not written under at all but
/// before the whole cluster. A vowel written to the left is moved to the front of the cluster in
/// the same way. Both moves are made here, before the font is asked for anything.
/// </remarks>
internal sealed class KhmerPlan : ShapingPlan
{
    private const uint Pref = 1u << 1;
    private const uint Blwf = 1u << 2;
    private const uint Abvf = 1u << 3;
    private const uint Pstf = 1u << 4;
    private const uint Cfar = 1u << 5;

    public static readonly KhmerPlan Instance = new();

    protected override string[] ScriptTags => ["khmr"];

    public override void Substitute(TrueTypeFont font, string text, List<ShapeItem> buffer)
    {
        var substitutor = font.Substitutor;
        if (substitutor is null) return;

        substitutor.SelectScript(ScriptTags);
        substitutor.WithinSyllables = true;

        {
            foreach (var item in buffer)
            {
                item.Category = (byte)IndicSyllables.CategoryOf(item.CodePoint);
                item.Position = (byte)IndicSyllables.PositionOf(item.CodePoint);
            }

            var syllables = Divide(buffer);

            substitutor.Apply(buffer, "locl");
            substitutor.Apply(buffer, "ccmp");

            foreach (var (start, end, kind) in syllables)
            {
                if (kind != SyllableKind.NotIndic) Reorder(buffer, start, end);
            }

            substitutor.Apply(buffer, "pref", Pref);
            substitutor.Apply(buffer, "blwf", Blwf);
            substitutor.Apply(buffer, "abvf", Abvf);
            substitutor.Apply(buffer, "pstf", Pstf);
            substitutor.Apply(buffer, "cfar", Cfar);

            substitutor.WithinSyllables = false;

            substitutor.Apply(buffer, "pres");
            substitutor.Apply(buffer, "abvs");
            substitutor.Apply(buffer, "blws");
            substitutor.Apply(buffer, "psts");
            substitutor.Apply(buffer, "clig");
        }
    }

    /// <summary>
    /// Divides a run into syllables: a consonant, then any number of consonants each written
    /// under the last, then the vowels and signs.
    /// </summary>
    private static List<(int Start, int End, SyllableKind Kind)> Divide(List<ShapeItem> buffer)
    {
        var syllables = new List<(int, int, SyllableKind)>();
        var at = 0;

        IndicCategory Category(int index) => (IndicCategory)buffer[index].Category;

        while (at < buffer.Count)
        {
            var start = at;

            if (IndicSyllables.IsConsonant(Category(at)) ||
                Category(at) is IndicCategory.Placeholder or IndicCategory.DottedCircle)
            {
                at++;

                if (at < buffer.Count && IndicSyllables.IsJoiner(Category(at)) &&
                    at + 1 < buffer.Count && Category(at + 1) == IndicCategory.Robatic)
                {
                    at += 2;
                }
                else if (at < buffer.Count && Category(at) == IndicCategory.Robatic)
                {
                    at++;
                }

                // Each coeng takes the consonant after it under the one before.
                while (at + 1 < buffer.Count && Category(at) == IndicCategory.Halant &&
                       IndicSyllables.IsConsonant(Category(at + 1)))
                {
                    at += 2;
                }

                at = Tail(buffer, at);
            }
            else
            {
                at++;
            }

            if (at == start) at++;

            for (var i = start; i < at; i++) buffer[i].Syllable = syllables.Count;

            syllables.Add((start, at, SyllableKind.Consonant));
        }

        return syllables;
    }

    /// <summary>The vowels and signs that follow the consonants of a Khmer syllable.</summary>
    private static int Tail(List<ShapeItem> buffer, int at)
    {
        IndicCategory Category(int index) => (IndicCategory)buffer[index].Category;

        bool Group(IndicCategory category) =>
            category is IndicCategory.XGroup or IndicCategory.YGroup;

        while (at < buffer.Count &&
               (Group(Category(at)) || IndicSyllables.IsJoiner(Category(at)) ||
                Category(at) is IndicCategory.VowelPre or IndicCategory.VowelBelow
                    or IndicCategory.VowelAbove or IndicCategory.VowelPost))
        {
            at++;
        }

        // A consonant joined on the end of the syllable, which Khmer allows after its vowels.
        if (at + 1 < buffer.Count && Category(at) == IndicCategory.Halant &&
            IndicSyllables.IsConsonant(Category(at + 1)))
        {
            at += 2;
        }

        return at;
    }

    /// <summary>
    /// Moves what is written before the cluster to the front of it: the r written under a
    /// consonant, which is drawn before the whole thing, and any vowel written to the left.
    /// </summary>
    private static void Reorder(List<ShapeItem> buffer, int start, int end)
    {
        for (var i = start + 1; i < end; i++) buffer[i].Mask |= Blwf | Abvf | Pstf;

        var stacked = 0;

        for (var i = start + 1; i < end; i++)
        {
            var category = (IndicCategory)buffer[i].Category;

            if (category == IndicCategory.Halant && stacked <= 2 && i + 1 < end)
            {
                stacked++;

                if ((IndicCategory)buffer[i + 1].Category != IndicCategory.Ra) continue;

                buffer[i].Mask |= Pref;
                buffer[i + 1].Mask |= Pref;

                // The coeng and its r go to the front of the syllable.
                var coeng = buffer[i];
                var ra = buffer[i + 1];

                buffer.RemoveRange(i, 2);
                buffer.InsertRange(start, [coeng, ra]);

                // What follows is drawn after them, and the font is told so.
                for (var j = i + 2; j < end; j++) buffer[j].Mask |= Cfar;

                stacked = 2;
            }
            else if (category == IndicCategory.VowelPre)
            {
                var vowel = buffer[i];

                buffer.RemoveAt(i);
                buffer.Insert(start, vowel);
            }
        }
    }
}

/// <summary>
/// The plan for Myanmar, whose syllable is put in order by giving every part of it a place and
/// sorting, and whose medial consonants are marks that may be drawn before what they belong to.
/// </summary>
/// <remarks>
/// Myanmar is the plainest of these to state and the least like the others to implement. There is
/// no asking the font what it can do: which part of the syllable a character belongs to is decided
/// by what it is, in one pass down the syllable, and the sort does the rest. The r written as a
/// medial goes before the consonant it belongs to, as does a vowel written on the left, and the
/// kinzi — an r with its killer mark, standing for an r at the start — is drawn after the base.
/// </remarks>
internal sealed class MyanmarPlan : ShapingPlan
{
    public static readonly MyanmarPlan Instance = new();

    protected override string[] ScriptTags => ["mym2", "mymr"];

    public override void Substitute(TrueTypeFont font, string text, List<ShapeItem> buffer)
    {
        var substitutor = font.Substitutor;
        if (substitutor is null) return;

        substitutor.SelectScript(ScriptTags);
        substitutor.WithinSyllables = true;

        {
            foreach (var item in buffer)
            {
                item.Category = (byte)IndicSyllables.CategoryOf(item.CodePoint);
                item.Position = (byte)IndicPosition.Start;
            }

            var syllables = Divide(buffer);

            substitutor.Apply(buffer, "locl");
            substitutor.Apply(buffer, "ccmp");

            foreach (var (start, end, _) in syllables) Reorder(buffer, start, end);

            substitutor.Apply(buffer, "rphf");
            substitutor.Apply(buffer, "pref");
            substitutor.Apply(buffer, "blwf");
            substitutor.Apply(buffer, "pstf");

            substitutor.WithinSyllables = false;

            substitutor.Apply(buffer, "pres");
            substitutor.Apply(buffer, "abvs");
            substitutor.Apply(buffer, "blws");
            substitutor.Apply(buffer, "psts");
        }
    }

    private static List<(int Start, int End, SyllableKind Kind)> Divide(List<ShapeItem> buffer)
    {
        var syllables = new List<(int, int, SyllableKind)>();
        var at = 0;

        IndicCategory Category(int index) => (IndicCategory)buffer[index].Category;

        while (at < buffer.Count)
        {
            var start = at;

            // A kinzi: an r with its killer and the mark that joins it to what follows.
            if (at + 2 < buffer.Count && Category(at) == IndicCategory.Ra &&
                Category(at + 1) == IndicCategory.Asat && Category(at + 2) == IndicCategory.Halant)
            {
                at += 3;
            }
            else if (Category(at) == IndicCategory.ConsonantWithStacker)
            {
                at++;
            }

            if (at < buffer.Count && (IndicSyllables.IsConsonant(Category(at)) ||
                                      Category(at) is IndicCategory.Placeholder
                                          or IndicCategory.DottedCircle))
            {
                at++;
                if (at < buffer.Count && Category(at) == IndicCategory.VariationSelector) at++;

                at = Tail(buffer, at);
            }
            else if (at > start)
            {
                at = Tail(buffer, at);
            }
            else
            {
                at++;
            }

            if (at == start) at++;

            for (var i = start; i < at; i++) buffer[i].Syllable = syllables.Count;

            syllables.Add((start, at, SyllableKind.Consonant));
        }

        return syllables;
    }

    /// <summary>Everything that may follow the consonant of a Myanmar syllable.</summary>
    private static int Tail(List<ShapeItem> buffer, int at)
    {
        IndicCategory Category(int index) => (IndicCategory)buffer[index].Category;

        while (true)
        {
            // A consonant joined under the last, which may repeat.
            if (at + 1 < buffer.Count && Category(at) == IndicCategory.Halant &&
                (IndicSyllables.IsConsonant(Category(at + 1)) ||
                 Category(at + 1) == IndicCategory.Vowel))
            {
                at += 2;

                if (at < buffer.Count && Category(at) == IndicCategory.VariationSelector) at++;

                continue;
            }

            break;
        }

        if (at < buffer.Count && Category(at) == IndicCategory.Halant) return at + 1;

        while (at < buffer.Count)
        {
            var category = Category(at);

            var part = category is IndicCategory.Asat or IndicCategory.MedialYa
                or IndicCategory.MedialRa or IndicCategory.MedialWa or IndicCategory.MedialHa
                or IndicCategory.MedialLa or IndicCategory.VowelPre or IndicCategory.VowelAbove
                or IndicCategory.VowelBelow or IndicCategory.VowelPost or IndicCategory.Accent
                or IndicCategory.Nukta or IndicCategory.VariationSelector
                or IndicCategory.SyllableModifier or IndicCategory.SyllableModifierPost
                or IndicCategory.PwoTone;

            if (!part) break;

            at++;
        }

        if (at < buffer.Count && IndicSyllables.IsJoiner(Category(at))) at++;

        return at;
    }

    /// <summary>
    /// Gives every part of a syllable a place and sorts them into it. Every rule of Myanmar
    /// reordering is in the one walk below.
    /// </summary>
    private static void Reorder(List<ShapeItem> buffer, int start, int end)
    {
        IndicCategory Category(int index) => (IndicCategory)buffer[index].Category;

        var @base = end;
        var hasKinzi = start + 3 <= end && Category(start) == IndicCategory.Ra &&
                       Category(start + 1) == IndicCategory.Asat &&
                       Category(start + 2) == IndicCategory.Halant;

        var limit = hasKinzi ? start + 3 : start;

        @base = limit;

        for (var i = limit; i < end; i++)
        {
            if (!IndicSyllables.IsConsonant(Category(i))) continue;

            @base = i;
            break;
        }

        var at = start;

        // The kinzi is drawn after the consonant it belongs to, not before it.
        for (; at < start + (hasKinzi ? 3 : 0); at++)
            buffer[at].Position = (byte)IndicPosition.AfterMain;

        for (; at < @base; at++) buffer[at].Position = (byte)IndicPosition.PreConsonant;

        if (at < end)
        {
            buffer[at].Position = (byte)IndicPosition.BaseConsonant;
            at++;
        }

        var place = IndicPosition.AfterMain;

        for (; at < end; at++)
        {
            var category = Category(at);

            // An r written as a medial is drawn before the consonant it is written on.
            if (category == IndicCategory.MedialRa)
            {
                buffer[at].Position = (byte)IndicPosition.PreConsonant;
                continue;
            }

            if (category == IndicCategory.VowelPre)
            {
                buffer[at].Position = (byte)IndicPosition.PreMatra;
                continue;
            }

            if (category == IndicCategory.VariationSelector)
            {
                buffer[at].Position = buffer[at - 1].Position;
                continue;
            }

            if (place == IndicPosition.AfterMain && category == IndicCategory.VowelBelow)
            {
                place = IndicPosition.BelowConsonant;
                buffer[at].Position = (byte)place;
                continue;
            }

            if (place == IndicPosition.BelowConsonant && category == IndicCategory.Accent)
            {
                buffer[at].Position = (byte)IndicPosition.BeforeSub;
                continue;
            }

            if (place == IndicPosition.BelowConsonant && category == IndicCategory.VowelBelow)
            {
                buffer[at].Position = (byte)place;
                continue;
            }

            if (place == IndicPosition.BelowConsonant)
            {
                place = IndicPosition.AfterSub;
                buffer[at].Position = (byte)place;
                continue;
            }

            buffer[at].Position = (byte)place;
        }

        Sort(buffer, start, end);

        // Several vowels written to the left keep the order they were stored in.
        var firstLeft = end;
        var lastLeft = end;

        for (var i = start; i < end; i++)
        {
            if ((IndicPosition)buffer[i].Position != IndicPosition.PreMatra) continue;

            if (firstLeft == end) firstLeft = i;
            lastLeft = i;
        }

        if (firstLeft >= lastLeft) return;

        buffer.Reverse(firstLeft, lastLeft - firstLeft + 1);

        var from = firstLeft;

        for (var j = from; j <= lastLeft; j++)
        {
            if (Category(j) != IndicCategory.VowelPre) continue;

            buffer.Reverse(from, j - from + 1);
            from = j + 1;
        }
    }

    /// <summary>A stable sort by place, which a syllable of a dozen glyphs wants.</summary>
    private static void Sort(List<ShapeItem> buffer, int start, int end)
    {
        for (var i = start + 1; i < end; i++)
        {
            var held = buffer[i];
            var j = i - 1;

            while (j >= start && buffer[j].Position > held.Position)
            {
                buffer[j + 1] = buffer[j];
                j--;
            }

            buffer[j + 1] = held;
        }
    }
}

/// <summary>
/// The plan for Thai and Lao, which do not reorder at all.
/// </summary>
/// <remarks>
/// These two are written without spaces between the words and stack their vowels and tone marks
/// above and below the consonants, but every character is stored in the order it is drawn. What
/// they need is what any script with marks needs — the marks put where the font says, which the
/// positioning does — and the composition rules, which a font uses to draw a tone mark higher
/// where a vowel is already there.
/// </remarks>
internal sealed class ThaiPlan : ShapingPlan
{
    public static readonly ThaiPlan Thai = new(["thai"]);
    public static readonly ThaiPlan Lao = new(["lao "]);

    private ThaiPlan(string[] tags) => _tags = tags;

    private readonly string[] _tags;

    protected override string[] ScriptTags => _tags;

    public override void Substitute(TrueTypeFont font, string text, List<ShapeItem> buffer)
    {
        var substitutor = font.Substitutor;
        if (substitutor is null) return;

        substitutor.SelectScript(ScriptTags);

        substitutor.Apply(buffer, "ccmp");
        substitutor.Apply(buffer, "locl");
        substitutor.Apply(buffer, "rlig");
        substitutor.Apply(buffer, "liga");
        substitutor.Apply(buffer, "calt");
        substitutor.Apply(buffer, "clig");
    }
}
