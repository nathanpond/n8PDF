namespace n8PDF.Text;

/// <summary>
/// What a character is to the universal shaping engine.
/// </summary>
/// <remarks>
/// The names are the engine's own rather than any script's. It knows nothing about which writing
/// system a character belongs to: it knows that this one is something a syllable is built on, that
/// one is a vowel drawn above, this one a consonant drawn below, that one a mark that goes on a
/// mark. A script nobody has written rules for is shaped correctly if its characters are
/// classified and its font says what to do with them, which is the whole idea.
/// </remarks>
internal enum UseCategory : byte
{
    Other,

    /// <summary>Something a syllable is built on: a consonant, an independent vowel, a number.</summary>
    Base,

    /// <summary>A number that other numbers are joined to.</summary>
    BaseNumber,

    /// <summary>Something standing in for a base that is not there.</summary>
    BaseOther,

    /// <summary>A joiner or a variation selector: something between letters that is not one.</summary>
    GraphemeJoiner,

    /// <summary>A consonant written under the one before it, by a character of its own.</summary>
    ConsonantSubjoined,

    /// <summary>The mark that joins one consonant to the next.</summary>
    Halant,

    /// <summary>The one that joins numbers.</summary>
    HalantNumber,

    NonJoiner,

    WordJoiner,

    /// <summary>An r at the head of a cluster, drawn as a mark at its end.</summary>
    Repha,

    /// <summary>A consonant that stacks what follows it without a visible mark.</summary>
    ConsonantWithStacker,

    /// <summary>A joining mark that leaves no gap and is not drawn.</summary>
    InvisibleStacker,

    /// <summary>Tai Tham's sakot, which joins a consonant to the next without being either.</summary>
    Sakot,

    /// <summary>A mark that kills the vowel and stops the reordering with it.</summary>
    ReorderingKiller,

    /// <summary>A mark that is a joining mark in one script and a vowel modifier in another.</summary>
    HalantOrVowelModifier,

    // ----- Egyptian hieroglyphs, which are laid out in blocks rather than lines -----

    Hieroglyph,
    HieroglyphJoiner,
    HieroglyphBegin,
    HieroglyphEnd,
    HieroglyphModifier,
    HieroglyphMirror,

    // ----- everything positioned round a base, by the side it is drawn on -----

    ConsonantFinalAbove,
    ConsonantFinalBelow,
    ConsonantFinalPost,

    ConsonantMedialAbove,
    ConsonantMedialBelow,
    ConsonantMedialPost,
    ConsonantMedialPre,

    ConsonantModifierAbove,
    ConsonantModifierBelow,

    VowelAbove,
    VowelBelow,
    VowelPost,
    VowelPre,

    VowelModifierAbove,
    VowelModifierBelow,
    VowelModifierPost,
    VowelModifierPre,

    SymbolModifierAbove,
    SymbolModifierBelow,

    ConsonantFinalModifierAbove,
    ConsonantFinalModifierBelow,
    ConsonantFinalModifierPost
}

/// <summary>What kind of cluster a run of characters makes.</summary>
internal enum UseCluster : byte
{
    /// <summary>One ending in the mark that joins it to whatever follows.</summary>
    ViramaTerminated,

    /// <summary>One ending in a sakot, which does the same in Tai Tham.</summary>
    SakotTerminated,

    /// <summary>An ordinary one: a base with whatever is written round it.</summary>
    Standard,

    NumberJoinerTerminated,
    Numeral,
    Symbol,
    Hieroglyph,

    /// <summary>One with no base to hang from, which is still drawn.</summary>
    Broken,

    /// <summary>Anything that is not a cluster at all.</summary>
    NotACluster
}

/// <summary>
/// Divides a run into the clusters the universal engine shapes, and says what each character is.
/// </summary>
/// <remarks>
/// The grammar below is the specification's, said in code rather than in a table: a cluster is
/// something to build on, then the consonants written round it, then the medial consonants, then
/// the vowels, then the marks on the vowels, then the final consonants and the marks on those —
/// each group in its own order, each part of a group optional. What makes it worth writing out is
/// that the whole engine hangs off it: everything that follows is done to a cluster, and a run
/// divided wrongly is shaped wrongly however good the rest is.
/// </remarks>
internal static class UseSyllables
{
    public static UseCategory CategoryOf(int codePoint)
    {
        var at = Find(UseTables.Starts, UseTables.Ends, codePoint);

        return at < 0 ? UseCategory.Other : UseTables.Kinds[at];
    }

    /// <summary>
    /// The name a font files this character's script under, or null where the script is one this
    /// engine does not shape.
    /// </summary>
    public static string? ScriptTagOf(int codePoint)
    {
        var at = Find(UseTables.ScriptStarts, UseTables.ScriptEnds, codePoint);

        return at < 0 ? null : UseTables.ScriptTags[at];
    }

    private static int Find(int[] starts, int[] ends, int codePoint)
    {
        var low = 0;
        var high = starts.Length - 1;

        while (low <= high)
        {
            var middle = (low + high) / 2;

            if (codePoint < starts[middle]) high = middle - 1;
            else if (codePoint > ends[middle]) low = middle + 1;
            else return middle;
        }

        return -1;
    }

    /// <summary>
    /// Where each cluster of a run begins and ends, and what kind it is.
    /// </summary>
    /// <remarks>
    /// Every kind of cluster is tried at each position and the longest wins, with the earlier kind
    /// winning where two reach the same length. That is what the specification's own machine does,
    /// and the alternatives overlap enough that anything less would divide some runs differently.
    /// </remarks>
    public static List<(int Start, int End, UseCluster Kind)> Find(IReadOnlyList<UseCategory> categories)
    {
        // The grammar is read over what is visible. A joiner, a variation selector, a mark that is
        // ignorable by default: none of them is part of a cluster's shape, and letting one break a
        // cluster in two would undo the very join it was written to ask for. They stay where they
        // are and belong to the cluster they stand in.
        var visible = new List<int>(categories.Count);

        for (var i = 0; i < categories.Count; i++)
        {
            if (categories[i] == UseCategory.GraphemeJoiner) continue;

            // A non-joiner before a mark is passed over too: it is asking for the mark to stand
            // apart rather than for the cluster to end.
            if (categories[i] == UseCategory.NonJoiner)
            {
                var next = i + 1;
                while (next < categories.Count && categories[next] == UseCategory.GraphemeJoiner) next++;

                if (next < categories.Count && IsUnicodeMark(categories[next])) continue;
            }

            visible.Add(i);
        }

        if (visible.Count == 0)
        {
            return categories.Count == 0
                ? []
                : [(0, categories.Count, UseCluster.NotACluster)];
        }

        var seen = new UseCategory[visible.Count];
        for (var i = 0; i < visible.Count; i++) seen[i] = categories[visible[i]];

        var clusters = new List<(int, int, UseCluster)>();
        var scanner = new Scanner(seen);
        var at = 0;

        while (at < seen.Length)
        {
            var (end, kind) = scanner.Longest(at);

            if (end <= at)
            {
                end = at + 1;
                kind = UseCluster.NotACluster;
            }

            // Back to where these characters really are: a cluster runs to wherever the next one
            // begins, so anything passed over between them belongs to this one.
            var from = clusters.Count == 0 ? 0 : visible[at];
            var to = end < visible.Count ? visible[end] : categories.Count;

            clusters.Add((from, to, kind));
            at = end;
        }

        return clusters;
    }

    /// <summary>
    /// Whether a category is one only a mark can have, for the rule about non-joiners before them.
    /// </summary>
    private static bool IsUnicodeMark(UseCategory category) =>
        category is UseCategory.VowelAbove or UseCategory.VowelBelow or UseCategory.VowelPost
            or UseCategory.VowelPre or UseCategory.VowelModifierAbove
            or UseCategory.VowelModifierBelow or UseCategory.VowelModifierPost
            or UseCategory.VowelModifierPre or UseCategory.ConsonantModifierAbove
            or UseCategory.ConsonantModifierBelow or UseCategory.ConsonantFinalAbove
            or UseCategory.ConsonantFinalBelow or UseCategory.ConsonantFinalPost
            or UseCategory.ConsonantMedialAbove or UseCategory.ConsonantMedialBelow
            or UseCategory.ConsonantMedialPost or UseCategory.ConsonantMedialPre
            or UseCategory.Halant or UseCategory.HalantOrVowelModifier
            or UseCategory.InvisibleStacker or UseCategory.ConsonantSubjoined;

    /// <summary>Whether a character is one of the marks that joins a consonant to the next.</summary>
    public static bool IsHalant(UseCategory category) =>
        category is UseCategory.Halant or UseCategory.HalantOrVowelModifier
            or UseCategory.InvisibleStacker;

    /// <summary>Whether a character is drawn after the base rather than before it.</summary>
    public static bool IsAfterBase(UseCategory category) =>
        category is UseCategory.ConsonantFinalAbove or UseCategory.ConsonantFinalBelow
            or UseCategory.ConsonantFinalPost or UseCategory.ConsonantFinalModifierAbove
            or UseCategory.ConsonantFinalModifierBelow or UseCategory.ConsonantFinalModifierPost
            or UseCategory.ConsonantMedialAbove or UseCategory.ConsonantMedialBelow
            or UseCategory.ConsonantMedialPost or UseCategory.ConsonantMedialPre
            or UseCategory.VowelAbove or UseCategory.VowelBelow or UseCategory.VowelPost
            or UseCategory.VowelPre or UseCategory.VowelModifierAbove
            or UseCategory.VowelModifierBelow or UseCategory.VowelModifierPost
            or UseCategory.VowelModifierPre;

    /// <summary>The grammar itself, walked over one run.</summary>
    private sealed class Scanner(IReadOnlyList<UseCategory> categories)
    {
        private int Count => categories.Count;

        private bool Is(int at, UseCategory category) => at < Count && categories[at] == category;

        private bool IsAny(int at, params UseCategory[] any) =>
            at < Count && Array.IndexOf(any, categories[at]) >= 0;

        /// <summary>Takes as many of one kind as there are.</summary>
        private int Many(int at, UseCategory category)
        {
            while (Is(at, category)) at++;

            return at;
        }

        /// <summary>The longest cluster starting here, and which kind it is.</summary>
        public (int End, UseCluster Kind) Longest(int at)
        {
            var best = (End: -1, Kind: UseCluster.NotACluster);

            void Try(int end, UseCluster kind)
            {
                if (end > best.End) best = (end, kind);
            }

            // In the order the specification lists them, so that where two reach equally far the
            // earlier one is what the cluster is called.
            Try(WithNonJoiner(ViramaTerminated(at)), UseCluster.ViramaTerminated);
            Try(WithNonJoiner(SakotTerminated(at)), UseCluster.SakotTerminated);
            Try(WithNonJoiner(Standard(at)), UseCluster.Standard);
            Try(WithNonJoiner(NumberJoinerTerminated(at)), UseCluster.NumberJoinerTerminated);
            Try(WithNonJoiner(Numeral(at)), UseCluster.Numeral);
            Try(WithNonJoiner(Symbol(at)), UseCluster.Symbol);
            Try(WithNonJoiner(Hieroglyph(at)), UseCluster.Hieroglyph);

            if (Is(at, UseCategory.ConsonantFinalModifierPost)) Try(at + 1, UseCluster.NotACluster);

            Try(WithNonJoiner(Broken(at)), UseCluster.Broken);

            return best;
        }

        /// <summary>A cluster may be closed by a non-joiner, which asks for it to stop there.</summary>
        private int WithNonJoiner(int end) =>
            end > 0 && Is(end, UseCategory.NonJoiner) ? end + 1 : end;

        // ----- the pieces -----

        /// <summary>The marks and stacked consonants that may follow what is built on.</summary>
        private int ConsonantModifiers(int at)
        {
            at = Many(Many(at, UseCategory.ConsonantModifierAbove), UseCategory.ConsonantModifierBelow);

            while (true)
            {
                var next = at;

                if (IsAny(next, UseCategory.Halant, UseCategory.HalantOrVowelModifier,
                        UseCategory.InvisibleStacker, UseCategory.Sakot) &&
                    Is(next + 1, UseCategory.Base))
                {
                    next += 2;
                }
                else if (Is(next, UseCategory.ConsonantSubjoined))
                {
                    next++;
                }
                else
                {
                    return at;
                }

                at = Many(Many(next, UseCategory.ConsonantModifierAbove),
                    UseCategory.ConsonantModifierBelow);
            }
        }

        private int MedialConsonants(int at)
        {
            if (Is(at, UseCategory.ConsonantMedialPre)) at++;
            if (Is(at, UseCategory.ConsonantMedialAbove)) at++;
            if (Is(at, UseCategory.ConsonantMedialBelow)) at++;
            if (Is(at, UseCategory.ConsonantMedialPost)) at++;

            return at;
        }

        private int DependentVowels(int at)
        {
            var vowels = Many(Many(Many(Many(at, UseCategory.VowelPre), UseCategory.VowelAbove),
                UseCategory.VowelBelow), UseCategory.VowelPost);

            if (vowels > at) return vowels;

            // Or, instead of any vowel at all, the mark that says there is none.
            return Is(at, UseCategory.Halant) ? at + 1 : at;
        }

        private int VowelModifiers(int at)
        {
            if (Is(at, UseCategory.HalantOrVowelModifier)) at++;

            return Many(Many(Many(Many(at, UseCategory.VowelModifierPre),
                UseCategory.VowelModifierAbove), UseCategory.VowelModifierBelow),
                UseCategory.VowelModifierPost);
        }

        private int FinalConsonants(int at) =>
            Many(Many(Many(at, UseCategory.ConsonantFinalAbove), UseCategory.ConsonantFinalBelow),
                UseCategory.ConsonantFinalPost);

        private int FinalModifiers(int at)
        {
            var modifiers = Many(Many(at, UseCategory.ConsonantFinalModifierAbove),
                UseCategory.ConsonantFinalModifierBelow);

            if (modifiers > at) return modifiers;

            return Is(at, UseCategory.ConsonantFinalModifierPost) ? at + 1 : at;
        }

        /// <summary>What a cluster is built on, with the r that may stand before it.</summary>
        private int Start(int at)
        {
            if (IsAny(at, UseCategory.Repha, UseCategory.ConsonantWithStacker)) at++;

            return IsAny(at, UseCategory.Base, UseCategory.BaseOther) ? at + 1 : -1;
        }

        private int Middle(int at)
        {
            at = VowelModifiers(DependentVowels(MedialConsonants(ConsonantModifiers(at))));

            while (Is(at, UseCategory.Sakot) && Is(at + 1, UseCategory.Base)) at += 2;

            return at;
        }

        private int Tail(int at) => FinalModifiers(FinalConsonants(Middle(at)));

        private int ViramaTerminatedTail(int at)
        {
            at = ConsonantModifiers(at);

            return IsAny(at, UseCategory.InvisibleStacker, UseCategory.ReorderingKiller)
                ? at + 1
                : -1;
        }

        private int SakotTerminatedTail(int at)
        {
            at = Middle(at);

            return Is(at, UseCategory.Sakot) ? at + 1 : -1;
        }

        private int SymbolTail(int at)
        {
            var above = Many(at, UseCategory.SymbolModifierAbove);

            if (above > at) return Many(above, UseCategory.SymbolModifierBelow);

            var below = Many(at, UseCategory.SymbolModifierBelow);

            return below > at ? below : -1;
        }

        private int NumberJoinerTail(int at)
        {
            var last = -1;

            while (Is(at, UseCategory.HalantNumber))
            {
                if (!Is(at + 1, UseCategory.BaseNumber)) return at + 1;

                at += 2;
                last = at;
            }

            return last;
        }

        private int NumeralTail(int at)
        {
            var last = -1;

            while (Is(at, UseCategory.HalantNumber) && Is(at + 1, UseCategory.BaseNumber))
            {
                at += 2;
                last = at;
            }

            return last;
        }

        private int ViramaTerminated(int at)
        {
            var start = Start(at);

            return start < 0 ? -1 : ViramaTerminatedTail(start);
        }

        private int SakotTerminated(int at)
        {
            var start = Start(at);

            return start < 0 ? -1 : SakotTerminatedTail(start);
        }

        private int Standard(int at)
        {
            var start = Start(at);

            return start < 0 ? -1 : Tail(start);
        }

        private int NumberJoinerTerminated(int at) =>
            Is(at, UseCategory.BaseNumber) ? NumberJoinerTail(at + 1) : -1;

        private int Numeral(int at)
        {
            if (!Is(at, UseCategory.BaseNumber)) return -1;

            var tail = NumeralTail(at + 1);

            return tail < 0 ? at + 1 : tail;
        }

        private int Symbol(int at)
        {
            if (!IsAny(at, UseCategory.Other, UseCategory.BaseOther, UseCategory.HieroglyphBegin))
                return -1;

            return Math.Max(at + 1, AnyTail(at + 1));
        }

        /// <summary>Whichever of the ways a cluster may end reaches furthest.</summary>
        private int AnyTail(int at) =>
            Math.Max(Math.Max(Tail(at), SakotTerminatedTail(at)),
                Math.Max(SymbolTail(at), ViramaTerminatedTail(at)));

        private int Hieroglyph(int at)
        {
            at = Many(at, UseCategory.HieroglyphBegin);

            if (!Is(at, UseCategory.Hieroglyph)) return -1;

            at = Glyph(at);

            while (Is(at, UseCategory.HieroglyphJoiner))
            {
                at++;
                at = Many(at, UseCategory.HieroglyphBegin);

                if (Is(at, UseCategory.Hieroglyph)) at = Glyph(at);
            }

            return at;

            int Glyph(int from)
            {
                from++;

                if (Is(from, UseCategory.HieroglyphMirror)) from++;
                if (Is(from, UseCategory.HieroglyphModifier)) from++;

                return Many(from, UseCategory.HieroglyphEnd);
            }
        }

        /// <summary>A cluster with nothing to hang from, which is still drawn.</summary>
        private int Broken(int at)
        {
            var start = Is(at, UseCategory.Repha) ? at + 1 : at;

            var end = Math.Max(AnyTail(start),
                Math.Max(NumberJoinerTail(start), NumeralTail(start)));

            return end > start ? end : -1;
        }
    }
}
