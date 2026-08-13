namespace n8PDF.Text;

/// <summary>What a character is to the shaper of an Indic or South-East Asian script.</summary>
internal enum IndicCategory : byte
{
    /// <summary>Anything the shaper has nothing to say about.</summary>
    Other,

    Consonant,

    /// <summary>An independent vowel, which begins a syllable in place of a consonant.</summary>
    Vowel,

    /// <summary>A dot written under a letter that makes it a different letter.</summary>
    Nukta,

    /// <summary>
    /// The mark that takes the vowel off a consonant, and so asks for it to be joined to the next.
    /// Called virama where it is a mark and halant where it is a joiner; both here.
    /// </summary>
    Halant,

    NonJoiner,

    Joiner,

    /// <summary>A dependent vowel sign, written round the consonant it is pronounced after.</summary>
    Matra,

    /// <summary>One that is sorted after a post-base sign rather than before it.</summary>
    MatraPost,

    /// <summary>A sign written over the whole syllable: a nasal, an aspirate.</summary>
    SyllableModifier,

    /// <summary>One with no place of its own, which follows whatever is at the end.</summary>
    SyllableModifierPost,

    /// <summary>A Vedic accent.</summary>
    Accent,

    /// <summary>Something standing in for a consonant that is not there.</summary>
    Placeholder,

    /// <summary>The circle a broken cluster is drawn on, which is the same thing said out loud.</summary>
    DottedCircle,

    /// <summary>A sign that shifts the register of what follows, in the scripts that have one.</summary>
    RegisterShifter,

    /// <summary>A consonant written as a mark on the one before it.</summary>
    ConsonantMedial,

    /// <summary>An encoded repha: an r at the start of a cluster, already written as its mark.</summary>
    Repha,

    /// <summary>The letter r, which is the one that becomes a repha.</summary>
    Ra,

    /// <summary>A letter that takes marks the way a consonant does without being one.</summary>
    Symbol,

    /// <summary>A consonant that stacks what follows it without a visible halant.</summary>
    ConsonantWithStacker,

    VariationSelector,

    // ----- Khmer and Myanmar, which sort their vowels by side rather than into one order -----

    VowelAbove,
    VowelBelow,
    VowelPre,
    VowelPost,

    /// <summary>Khmer's robat and the signs that behave like it.</summary>
    Robatic,

    XGroup,
    YGroup,

    // ----- Myanmar's own -----

    /// <summary>The asat, which kills the vowel of the letter it is written over.</summary>
    Asat,

    MedialHa,
    MedialRa,
    MedialWa,
    MedialYa,
    MedialLa,

    /// <summary>A Pwo Karen tone mark.</summary>
    PwoTone
}

/// <summary>
/// Where in a syllable something is drawn, from the left of it to the right.
/// </summary>
/// <remarks>
/// The order of these is the whole point of them. An Indic syllable is stored in the order it is
/// spoken and drawn in the order it is seen, and turning one into the other is done by giving every
/// part of it a place on this list and sorting. A vowel stored last but written to the left of its
/// consonant is <see cref="PreMatra"/>, which sorts before the consonant it was stored after.
/// </remarks>
internal enum IndicPosition : byte
{
    Start = 0,

    /// <summary>An r at the start of the cluster, which will be drawn as a mark at its end.</summary>
    RaToBecomeRepha = 1,

    /// <summary>A vowel sign written to the left of everything.</summary>
    PreMatra = 2,

    /// <summary>A consonant written before the base one.</summary>
    PreConsonant = 3,

    /// <summary>The consonant the rest of the syllable is arranged around.</summary>
    BaseConsonant = 4,

    AfterMain = 5,

    AboveConsonant = 6,

    BeforeSub = 7,

    BelowConsonant = 8,

    AfterSub = 9,

    BeforePost = 10,

    PostConsonant = 11,

    AfterPost = 12,

    /// <summary>The syllable modifiers and Vedic accents, which come last of all.</summary>
    SyllableModifierOrVedic = 13,

    End = 14
}

/// <summary>What kind of syllable a run of characters makes.</summary>
internal enum SyllableKind : byte
{
    Consonant,
    Vowel,
    Standalone,
    Symbol,
    Broken,
    NotIndic
}

/// <summary>
/// Divides a run of an Indic script into syllables, and says what each character of it is.
/// </summary>
/// <remarks>
/// Everything the shaper does is done to a syllable rather than to a run: the reordering, the
/// features that make conjuncts, the finding of the consonant the rest hangs off. A syllable is a
/// consonant or a vowel with its marks — with, before it, any number of consonants each followed by
/// the mark that joins it to the next, and after it any number of vowel signs, nasals and accents.
/// Written out as a grammar it is short; written out as prose it is what the paragraph above says.
/// </remarks>
internal static class IndicSyllables
{
    public static IndicCategory CategoryOf(int codePoint)
    {
        var at = Find(codePoint);
        return at < 0 ? IndicCategory.Other : IndicTables.Kinds[at];
    }

    public static IndicPosition PositionOf(int codePoint)
    {
        var at = Find(codePoint);
        return at < 0 ? IndicPosition.End : IndicTables.Places[at];
    }

    private static int Find(int codePoint)
    {
        var starts = IndicTables.Starts;

        var low = 0;
        var high = starts.Length - 1;

        while (low <= high)
        {
            var middle = (low + high) / 2;

            if (codePoint < starts[middle]) high = middle - 1;
            else if (codePoint > IndicTables.Ends[middle]) low = middle + 1;
            else return middle;
        }

        return -1;
    }

    /// <summary>
    /// A vowel and a placeholder are treated as consonants: neither can occur in a syllable that
    /// has one, and treating them alike means one set of rules rather than three.
    /// </summary>
    public static bool IsConsonant(IndicCategory category) =>
        category is IndicCategory.Consonant or IndicCategory.ConsonantWithStacker
            or IndicCategory.Ra or IndicCategory.ConsonantMedial or IndicCategory.Vowel
            or IndicCategory.Placeholder or IndicCategory.DottedCircle;

    public static bool IsJoiner(IndicCategory category) =>
        category is IndicCategory.Joiner or IndicCategory.NonJoiner;

    public static bool IsMatra(IndicCategory category) =>
        category is IndicCategory.Matra or IndicCategory.MatraPost;

    private static bool IsModifier(IndicCategory category) =>
        category is IndicCategory.SyllableModifier or IndicCategory.SyllableModifierPost;

    /// <summary>
    /// Where each syllable of a run begins and ends, and what kind it is.
    /// </summary>
    /// <param name="categories">What each character of the run is.</param>
    public static List<(int Start, int End, SyllableKind Kind)> Find(IReadOnlyList<IndicCategory> categories)
    {
        var syllables = new List<(int, int, SyllableKind)>();

        var at = 0;

        while (at < categories.Count)
        {
            var start = at;
            var kind = SyllableKind.NotIndic;

            // A repha or a stacking consonant may open a syllable, standing before the consonant
            // the rest of it is built on.
            var opened = categories[at] is IndicCategory.Repha or IndicCategory.ConsonantWithStacker;
            if (opened) at++;

            // An r followed by the mark that joins it to what comes next is the other way a
            // syllable opens: it is the sequence that becomes a repha.
            var reph = !opened && at + 1 < categories.Count &&
                       categories[at] == IndicCategory.Ra && categories[at + 1] == IndicCategory.Halant;

            if (reph && at + 2 < categories.Count && IsStart(categories[at + 2]))
            {
                at += 2;
            }
            else
            {
                reph = false;
            }

            if (at < categories.Count && IsConsonant(categories[at]))
            {
                kind = categories[at] == IndicCategory.Vowel
                    ? SyllableKind.Vowel
                    : categories[at] is IndicCategory.Placeholder or IndicCategory.DottedCircle
                        ? SyllableKind.Standalone
                        : SyllableKind.Consonant;

                at++;

                // A joiner may follow the consonant, asking for a particular shape of what
                // follows, and so may the dot that changes which letter it is.
                if (at < categories.Count && IsJoiner(categories[at])) at++;
                at = Modifiers(categories, at);

                at = Tail(categories, at);
            }
            else if (at < categories.Count && categories[at] == IndicCategory.Symbol)
            {
                kind = SyllableKind.Symbol;
                at++;

                if (at < categories.Count && categories[at] == IndicCategory.Nukta) at++;
                at = SyllableTail(categories, at);
            }
            else if (opened || reph)
            {
                // Something opened a syllable that then held nothing to hang it on.
                kind = SyllableKind.Broken;
                at = Tail(categories, Modifiers(categories, at));
            }
            else if (IsBroken(categories[at]))
            {
                kind = SyllableKind.Broken;
                at = Tail(categories, Modifiers(categories, at));
            }
            else
            {
                at++;
            }

            if (at == start) at++;

            syllables.Add((start, at, kind));
        }

        return syllables;
    }

    /// <summary>Whether a character can stand at the head of a syllable.</summary>
    private static bool IsStart(IndicCategory category) =>
        IsConsonant(category) || category is IndicCategory.Repha or IndicCategory.Symbol;

    /// <summary>
    /// Whether a character can only be part of a syllable that has lost its consonant: a mark on
    /// nothing, which the shaper still has to draw somewhere.
    /// </summary>
    private static bool IsBroken(IndicCategory category) =>
        category is IndicCategory.Halant or IndicCategory.Nukta or IndicCategory.Matra
            or IndicCategory.MatraPost or IndicCategory.SyllableModifier
            or IndicCategory.SyllableModifierPost or IndicCategory.Accent
            or IndicCategory.RegisterShifter or IndicCategory.ConsonantMedial;

    /// <summary>The dot and the register shifter that may follow a consonant.</summary>
    private static int Modifiers(IReadOnlyList<IndicCategory> categories, int at)
    {
        if (at < categories.Count && categories[at] == IndicCategory.NonJoiner &&
            at + 1 < categories.Count && categories[at + 1] == IndicCategory.RegisterShifter)
        {
            at += 2;
        }
        else if (at < categories.Count && categories[at] == IndicCategory.RegisterShifter)
        {
            at++;
        }

        if (at < categories.Count && categories[at] == IndicCategory.Nukta)
        {
            at++;
            if (at < categories.Count && categories[at] == IndicCategory.Nukta) at++;
        }

        return at;
    }

    /// <summary>
    /// What may follow the first consonant of a syllable: any number of joined consonants, then a
    /// medial one, then the vowel signs, then the marks that sit over the whole syllable.
    /// </summary>
    private static int Tail(IReadOnlyList<IndicCategory> categories, int at)
    {
        while (true)
        {
            var group = Halant(categories, at);
            if (group == at) break;

            // A joined consonant, or the end of the syllable where the mark stands alone.
            if (group < categories.Count && IsConsonant(categories[group]))
            {
                at = group + 1;

                if (at < categories.Count && categories[at] == IndicCategory.Joiner) at++;
                at = Modifiers(categories, at);

                continue;
            }

            // A halant that ends the syllable rather than joining anything to it.
            at = group;

            if (at < categories.Count && categories[at] == IndicCategory.NonJoiner) at++;

            return SyllableTail(categories, at);
        }

        if (at < categories.Count && categories[at] == IndicCategory.ConsonantMedial) at++;

        // The vowel signs, each of which may carry a dot and a joining mark of its own.
        while (at < categories.Count)
        {
            var before = at;

            while (at < categories.Count && IsJoiner(categories[at])) at++;

            if (at < categories.Count && IsMatra(categories[at]))
            {
                at++;
            }
            else if (at + 1 < categories.Count && IsModifier(categories[at]) &&
                     categories[at + 1] == IndicCategory.MatraPost)
            {
                at += 2;
            }
            else
            {
                at = before;
                break;
            }

            if (at < categories.Count && categories[at] == IndicCategory.Nukta) at++;
            if (at < categories.Count && categories[at] == IndicCategory.Halant) at++;
        }

        return SyllableTail(categories, at);
    }

    /// <summary>The mark that joins a consonant to the next, with the joiners around it.</summary>
    private static int Halant(IReadOnlyList<IndicCategory> categories, int at)
    {
        var start = at;

        if (at < categories.Count && IsJoiner(categories[at])) at++;

        if (at >= categories.Count || categories[at] != IndicCategory.Halant) return start;

        at++;

        if (at < categories.Count && categories[at] == IndicCategory.Joiner)
        {
            at++;
            if (at < categories.Count && categories[at] == IndicCategory.Nukta) at++;
        }

        return at;
    }

    /// <summary>The nasals and accents that close a syllable.</summary>
    private static int SyllableTail(IReadOnlyList<IndicCategory> categories, int at)
    {
        var mark = at;

        if (mark < categories.Count && IsJoiner(categories[mark])) mark++;

        if (mark < categories.Count && IsModifier(categories[mark]))
        {
            at = mark + 1;

            if (at < categories.Count && IsModifier(categories[at])) at++;
            if (at < categories.Count && categories[at] == IndicCategory.NonJoiner) at++;
        }

        while (at < categories.Count && categories[at] == IndicCategory.Accent) at++;

        return at;
    }
}
