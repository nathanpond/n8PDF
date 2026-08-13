namespace n8PDF.Text;

/// <summary>What a character is to the line breaking algorithm.</summary>
internal enum LineBreakClass : byte
{
    // ----- the ones that decide by themselves -----

    MandatoryBreak,
    CarriageReturn,
    LineFeed,
    NextLine,
    Space,
    ZeroWidthSpace,

    /// <summary>A mark, which belongs to whatever it is written on.</summary>
    CombiningMark,

    /// <summary>Something that holds what is on either side of it together.</summary>
    Glue,

    WordJoiner,
    ZeroWidthJoiner,

    // ----- and the rest, which decide by their company -----

    Alphabetic,
    HebrewLetter,
    Numeric,
    Ideographic,
    ComplexContext,
    Quotation,

    OpenPunctuation,
    ClosePunctuation,
    CloseParenthesis,
    Exclamation,
    Nonstarter,
    Inseparable,

    BreakAfter,
    BreakBefore,
    BreakBoth,
    Hyphen,

    /// <summary>A place held for something else, which breaks on both sides of it.</summary>
    ContingentBreak,

    SymbolAllowingBreak,
    InfixNumeric,
    PrefixNumeric,
    PostfixNumeric,

    HangulLvSyllable,
    HangulLvtSyllable,
    HangulLJamo,
    HangulVJamo,
    HangulTJamo,

    EmojiBase,
    EmojiModifier,
    RegionalIndicator
}

/// <summary>What a character of a script written without spaces is to a syllable.</summary>
internal enum ComplexRole : byte
{
    /// <summary>Not one of those scripts.</summary>
    None,

    /// <summary>A letter that may begin one, and so a place a line may be broken.</summary>
    Begins,

    /// <summary>A mark or vowel that belongs to the letter before it.</summary>
    Continues
}

/// <summary>
/// Where a line of text may be broken.
/// </summary>
/// <remarks>
/// Not at spaces. Chinese and Japanese are written with no spaces at all and break between one
/// character and the next; Thai, Lao, Khmer and Burmese are written without them too and break
/// between syllables; and English has spaces that may not be broken at — the one in "10 kg" — and
/// places without a space where a break is allowed, as after a hyphen. A converter that looks for
/// spaces draws a line of Japanese straight off the edge of the page, which is what this one did.
///
/// The Unicode line breaking algorithm decides it from a property of each character and a list of
/// rules about pairs, applied in order, the first that matches winning. What it does not decide is
/// the scripts written without spaces *and* without a break between every character: for those it
/// says to consult a dictionary of the language, which this converter has not got. What it does
/// instead is what Word does with the same paragraph — break between one syllable and the next,
/// wherever a letter begins a new one.
/// </remarks>
internal static class LineBreaker
{
    /// <summary>
    /// Whether a line may be broken before each character of a run. The first is never a break.
    /// </summary>
    public static bool[] Opportunities(string text) =>
        text.Length == 0 ? [] : new Run(text).Opportunities();

    /// <summary>
    /// One run of text, with every property of it the rules ask about read once.
    /// </summary>
    private sealed class Run
    {
        private readonly LineBreakClass[] _classes;

        /// <summary>What each character is to a syllable of a script written without spaces.</summary>
        private readonly ComplexRole[] _complex;

        /// <summary>Which characters the one after them may not be parted from.</summary>
        private readonly bool[] _bound;

        /// <summary>Which were folded into the character before them by rule 9.</summary>
        private readonly bool[] _attached;

        /// <summary>Which are brackets as wide as what they are written among.</summary>
        private readonly bool[] _wide;

        /// <summary>Which are code points set aside for emoji that do not exist yet.</summary>
        private readonly bool[] _reserved;

        public Run(string text)
        {
            var length = text.Length;

            _classes = new LineBreakClass[length];
            _complex = new ComplexRole[length];
            _bound = new bool[length];
            _attached = new bool[length];
            _wide = new bool[length];
            _reserved = new bool[length];

            Read(text);
            Fold();
        }

        public bool[] Opportunities()
        {
            var breaks = new bool[_classes.Length];

            for (var i = 1; i < breaks.Length; i++)
            {
                // Rule 9 again: whatever a mark was folded into, it is not left behind by itself,
                // and neither is the letter a vowel written before it belongs to.
                if (_attached[i] || _bound[i - 1]) continue;

                breaks[i] = Allowed(i);
            }

            return breaks;
        }

        /// <summary>What each character is, before any of it is folded together.</summary>
        private void Read(string text)
        {
            for (var i = 0; i < text.Length; i++)
            {
                var codePoint = (int)text[i];

                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length &&
                    char.IsLowSurrogate(text[i + 1]))
                {
                    codePoint = char.ConvertToUtf32(text[i], text[i + 1]);
                }

                _classes[i] = ClassOf(codePoint);
                _wide[i] = Among(LineBreakTables.WideBrackets, codePoint);
                _reserved[i] = Within(LineBreakTables.ReservedStarts, LineBreakTables.ReservedEnds,
                    codePoint);

                // The scripts that need a dictionary are noted, and what cannot begin a syllable is
                // folded into what it is written with, so that only the letters that can are left.
                if (_classes[i] == LineBreakClass.ComplexContext)
                {
                    var joins = JoinsPrevious(codePoint);

                    _complex[i] = joins ? ComplexRole.Continues : ComplexRole.Begins;
                    _bound[i] = BindsNext(codePoint);

                    _classes[i] = joins ? LineBreakClass.CombiningMark : LineBreakClass.Alphabetic;
                }

                // The second half of a character written as two is a mark, so that it is folded
                // into the first half and never left at the end of a line by itself.
                if (char.IsLowSurrogate(text[i]) && i > 0)
                {
                    _classes[i] = LineBreakClass.CombiningMark;
                    _wide[i] = _wide[i - 1];
                    _reserved[i] = _reserved[i - 1];
                }
            }
        }

        /// <summary>
        /// The marks folded into what they are written on, which is rule 9. Doing it here rather
        /// than in the rules keeps every later rule from having to say "unless it is a mark".
        /// </summary>
        /// <remarks>
        /// What a mark is written on is the last character that was neither a mark nor a joiner,
        /// since a joiner between them changes nothing about what the mark belongs to. A mark with
        /// nothing at all before it is a letter in its own right, which is rule 10.
        /// </remarks>
        private void Fold()
        {
            var written = -1;

            for (var i = 0; i < _classes.Length; i++)
            {
                if (_classes[i] is not (LineBreakClass.CombiningMark
                    or LineBreakClass.ZeroWidthJoiner))
                {
                    written = _classes[i] is LineBreakClass.MandatoryBreak
                        or LineBreakClass.CarriageReturn or LineBreakClass.LineFeed
                        or LineBreakClass.NextLine or LineBreakClass.Space
                        or LineBreakClass.ZeroWidthSpace
                        ? -1
                        : i;

                    continue;
                }

                if (written < 0)
                {
                    // A joiner keeps its class even here, since what it does is to what follows it.
                    if (_classes[i] == LineBreakClass.CombiningMark)
                    {
                        _classes[i] = LineBreakClass.Alphabetic;
                        written = i;
                    }

                    continue;
                }

                _attached[i] = true;

                // And a joiner that does have something before it keeps its class as well: every
                // rule that would read it is decided by rule 8a before they get the chance.
                if (_classes[i] == LineBreakClass.ZeroWidthJoiner) continue;

                _classes[i] = _classes[written];
                if (_complex[written] != ComplexRole.None) _complex[i] = ComplexRole.Continues;
            }
        }

        /// <summary>
        /// Whether the run may be broken before the character at this position, by the rules in the
        /// order they are given.
        /// </summary>
        private bool Allowed(int at)
        {
            var before = _classes[at - 1];
            var after = _classes[at];

            // 4, 5: a break where the text says so, and never inside a pair that stands for one.
            if (before is LineBreakClass.MandatoryBreak or LineBreakClass.LineFeed
                or LineBreakClass.NextLine)
            {
                return true;
            }

            if (before == LineBreakClass.CarriageReturn) return after != LineBreakClass.LineFeed;

            // 6: and never before one of them.
            if (after is LineBreakClass.MandatoryBreak or LineBreakClass.CarriageReturn
                or LineBreakClass.LineFeed or LineBreakClass.NextLine)
            {
                return false;
            }

            // 7: nor before a space, nor before what a zero-width space allows a break at.
            if (after is LineBreakClass.Space or LineBreakClass.ZeroWidthSpace) return false;

            // 8: after one, though — passing over any spaces that follow it.
            var start = Previous(at);
            if (_classes[start] == LineBreakClass.ZeroWidthSpace) return true;

            // 8a: and not between a joiner and what it joins to.
            if (before == LineBreakClass.ZeroWidthJoiner) return false;

            // 11: never on either side of a word joiner.
            if (after == LineBreakClass.WordJoiner || before == LineBreakClass.WordJoiner)
                return false;

            // 12, 12a: glue holds what is on either side of it, except after a space or a break.
            if (before == LineBreakClass.Glue) return false;

            if (after == LineBreakClass.Glue &&
                before is not (LineBreakClass.Space or LineBreakClass.BreakAfter
                    or LineBreakClass.Hyphen))
            {
                return false;
            }

            // 13: nor before the punctuation that closes something.
            if (after is LineBreakClass.ClosePunctuation or LineBreakClass.CloseParenthesis
                or LineBreakClass.Exclamation or LineBreakClass.InfixNumeric
                or LineBreakClass.SymbolAllowingBreak)
            {
                return false;
            }

            // 14: nor after an opening bracket, whatever spaces follow it.
            if (_classes[start] == LineBreakClass.OpenPunctuation) return false;

            // 15: nor between a quotation mark and an opening bracket.
            if (_classes[start] == LineBreakClass.Quotation && after == LineBreakClass.OpenPunctuation)
                return false;

            // 16, 17: nor between a closing bracket and what may not start a line, nor inside a dash
            // that is written as two.
            if (_classes[start] is LineBreakClass.ClosePunctuation or LineBreakClass.CloseParenthesis &&
                after == LineBreakClass.Nonstarter)
            {
                return false;
            }

            if (_classes[start] == LineBreakClass.BreakBoth && after == LineBreakClass.BreakBoth)
                return false;

            // 18: after a space, wherever the rules above have not already decided.
            if (before == LineBreakClass.Space) return true;

            // 19: never on either side of a quotation mark.
            if (after == LineBreakClass.Quotation || before == LineBreakClass.Quotation) return false;

            // 20: on both sides of the character that holds a place for something else.
            if (after == LineBreakClass.ContingentBreak || before == LineBreakClass.ContingentBreak)
                return true;

            // 21: not before what breaks before it, nor after what breaks after it — and 21a, not
            // after a hyphen that itself follows a Hebrew letter.
            if (after is LineBreakClass.BreakAfter or LineBreakClass.Hyphen or LineBreakClass.Nonstarter)
                return false;

            if (before == LineBreakClass.BreakBefore) return false;

            if (at >= 2 && _classes[at - 2] == LineBreakClass.HebrewLetter &&
                before is LineBreakClass.Hyphen or LineBreakClass.BreakAfter)
            {
                return false;
            }

            // 21b: nor between a Hebrew letter and the symbol that stands for a division.
            if (before == LineBreakClass.SymbolAllowingBreak && after == LineBreakClass.HebrewLetter)
                return false;

            // 22: nor before something that may not be separated from what it follows.
            if (after == LineBreakClass.Inseparable) return false;

            // 23, 23a: a letter and a number stay together, and so do a number and the marks that
            // stand before or after it.
            if (IsLetter(before) && after == LineBreakClass.Numeric) return false;
            if (before == LineBreakClass.Numeric && IsLetter(after)) return false;

            if (before == LineBreakClass.PrefixNumeric &&
                after is LineBreakClass.Ideographic or LineBreakClass.EmojiBase
                    or LineBreakClass.EmojiModifier)
            {
                return false;
            }

            if (before is LineBreakClass.Ideographic or LineBreakClass.EmojiBase
                    or LineBreakClass.EmojiModifier &&
                after == LineBreakClass.PostfixNumeric)
            {
                return false;
            }

            // 24: what stands before and after a sum.
            if (before is LineBreakClass.PrefixNumeric or LineBreakClass.PostfixNumeric && IsLetter(after))
                return false;

            if (IsLetter(before) &&
                after is LineBreakClass.PrefixNumeric or LineBreakClass.PostfixNumeric)
            {
                return false;
            }

            // 25: a number is more than its digits — the sign or currency mark before it, an opening
            // bracket, the separators inside it, a closing bracket, and the mark after. All of that is
            // one thing, and "$1,234.56" is not four places a line may be broken.
            if (before is LineBreakClass.PrefixNumeric or LineBreakClass.PostfixNumeric)
            {
                if (after == LineBreakClass.Numeric) return false;

                if (after is LineBreakClass.OpenPunctuation or LineBreakClass.Hyphen &&
                    at + 1 < _classes.Length && _classes[at + 1] == LineBreakClass.Numeric)
                {
                    return false;
                }
            }

            if (before is LineBreakClass.OpenPunctuation or LineBreakClass.Hyphen &&
                after == LineBreakClass.Numeric)
            {
                return false;
            }

            if (Number(at - 1) &&
                after is LineBreakClass.Numeric or LineBreakClass.SymbolAllowingBreak
                    or LineBreakClass.InfixNumeric or LineBreakClass.ClosePunctuation
                    or LineBreakClass.CloseParenthesis)
            {
                return false;
            }

            if (after is LineBreakClass.PrefixNumeric or LineBreakClass.PostfixNumeric &&
                Number(before is LineBreakClass.ClosePunctuation
                    or LineBreakClass.CloseParenthesis ? at - 2 : at - 1))
            {
                return false;
            }

            // 26, 27: Hangul, whose syllables are written as two or three pieces.
            if (before == LineBreakClass.HangulLJamo &&
                after is LineBreakClass.HangulLJamo or LineBreakClass.HangulVJamo
                    or LineBreakClass.HangulLvSyllable or LineBreakClass.HangulLvtSyllable)
            {
                return false;
            }

            if (before is LineBreakClass.HangulLvSyllable or LineBreakClass.HangulVJamo &&
                after is LineBreakClass.HangulVJamo or LineBreakClass.HangulTJamo)
            {
                return false;
            }

            if (before is LineBreakClass.HangulLvtSyllable or LineBreakClass.HangulTJamo &&
                after == LineBreakClass.HangulTJamo)
            {
                return false;
            }

            if (IsHangul(before) &&
                after is LineBreakClass.Inseparable or LineBreakClass.PostfixNumeric)
            {
                return false;
            }

            if (before == LineBreakClass.PrefixNumeric && IsHangul(after)) return false;

            // 28, 29, 30: letters stay with letters, a number with the mark inside it, and a bracket
            // with the letter or number beside it — but not the letters of the scripts written without
            // spaces, which would then never break at all. Those break wherever a syllable begins,
            // which after the folding above is wherever a letter is left standing.
            if (_complex[at - 1] != ComplexRole.None && _complex[at] == ComplexRole.Begins) return true;

            if (IsLetter(before) && IsLetter(after)) return false;

            if (before == LineBreakClass.InfixNumeric && IsLetter(after)) return false;

            if ((IsLetter(before) || before == LineBreakClass.Numeric) &&
                after == LineBreakClass.OpenPunctuation && !_wide[at])
            {
                return false;
            }

            if (before == LineBreakClass.CloseParenthesis && !_wide[at - 1] &&
                (IsLetter(after) || after == LineBreakClass.Numeric))
            {
                return false;
            }

            // 30a: a flag is two letters and is not broken in half.
            if (before == LineBreakClass.RegionalIndicator && after == LineBreakClass.RegionalIndicator)
                return EvenRun(at);

            // 30b: nor is an emoji and the modifier that colours it, whether or not it is an emoji
            // Unicode has drawn yet.
            if (after == LineBreakClass.EmojiModifier &&
                (before == LineBreakClass.EmojiBase || _reserved[at - 1]))
            {
                return false;
            }

            // 31: anything else may be broken — and for the scripts that need a dictionary, that means
            // between one syllable and the next, since their marks were folded into their letters
            // above and only a letter beginning a new syllable is left.
            return true;
        }


        /// <summary>The last position that is not a space, which several rules look back over.</summary>
        private int Previous(int at)
        {
            var i = at - 1;
            while (i > 0 && _classes[i] == LineBreakClass.Space) i--;

            return i;
        }

        /// <summary>Whether it belongs instead to the letter after it, which may not be left behind.</summary>
        /// <remarks>
        /// Thai and Lao write four of their vowels before the consonant they are sounded after, and
        /// Khmer and Burmese have a sign that turns the consonant following it into a subscript. In
        /// either case the two are one syllable and a line may not be broken between them.
        /// </remarks>
        private static bool IsLetter(LineBreakClass value) =>
            value is LineBreakClass.Alphabetic or LineBreakClass.HebrewLetter;

        private static bool IsHangul(LineBreakClass value) =>
            value is LineBreakClass.HangulLJamo or LineBreakClass.HangulVJamo
                or LineBreakClass.HangulTJamo or LineBreakClass.HangulLvSyllable
                or LineBreakClass.HangulLvtSyllable;

        /// <summary>
        /// Whether the run of digits and separators ending here begins with a digit, which is what
        /// the rules about numbers are written in terms of.
        /// </summary>
        private bool Number(int at)
        {
            for (var i = at; i >= 0; i--)
            {
                if (_classes[i] == LineBreakClass.Numeric) return true;

                if (_classes[i] is not (LineBreakClass.SymbolAllowingBreak or LineBreakClass.InfixNumeric))
                    return false;
            }

            return false;
        }

        /// <summary>Whether an even number of regional indicators stands before this one.</summary>
        private bool EvenRun(int at)
        {
            var count = 0;
            var i = at - 1;

            while (i >= 0 && _classes[i] == LineBreakClass.RegionalIndicator)
            {
                if (!_attached[i]) count++;
                i--;
            }

            return count % 2 == 0;
        }
    }

    /// <summary>What class a character belongs to, from the generated table.</summary>
    public static LineBreakClass ClassOf(int codePoint)
    {
        var starts = LineBreakTables.Starts;

        var low = 0;
        var high = starts.Length - 1;

        while (low <= high)
        {
            var middle = (low + high) / 2;

            if (codePoint < starts[middle]) high = middle - 1;
            else if (codePoint > LineBreakTables.Ends[middle]) low = middle + 1;
            else return LineBreakTables.Kinds[middle];
        }

        return LineBreakClass.Alphabetic;
    }

    /// <summary>
    /// Whether a character of one of those scripts belongs to the letter before it.
    /// </summary>
    /// <remarks>
    /// The marks do, and so do a handful of Thai and Lao vowels and signs that are written as
    /// letters in their own right but stand after a consonant and cannot begin a line: sara a, sara
    /// aa, sara am, lakkhangyao, maiyamok and the abbreviation sign, with their Lao counterparts.
    /// </remarks>
    private static bool JoinsPrevious(int codePoint) =>
        System.Globalization.CharUnicodeInfo.GetUnicodeCategory(codePoint)
            is System.Globalization.UnicodeCategory.NonSpacingMark
            or System.Globalization.UnicodeCategory.SpacingCombiningMark
            or System.Globalization.UnicodeCategory.ModifierLetter
        || codePoint is 0x0E2F or 0x0E30 or 0x0E32 or 0x0E33 or 0x0E45
            or 0x0EAF or 0x0EB0 or 0x0EB2 or 0x0EB3;

    /// <summary>Whether it belongs instead to the letter after it, which it may not be parted from.</summary>
    /// <remarks>
    /// Thai and Lao write four of their vowels before the consonant they are sounded after, and
    /// Khmer and Burmese have a sign that turns the consonant following it into a subscript. In
    /// either case the two are one syllable, and a line may not be broken between them.
    /// </remarks>
    private static bool BindsNext(int codePoint) =>
        codePoint is >= 0x0E40 and <= 0x0E44 or >= 0x0EC0 and <= 0x0EC4 or 0x17D2 or 0x1039;

    /// <summary>Whether a code point is one of a short list.</summary>
    private static bool Among(int[] list, int codePoint)
    {
        foreach (var member in list)
        {
            if (member == codePoint) return true;
        }

        return false;
    }

    /// <summary>Whether it falls in one of a short list of runs.</summary>
    private static bool Within(int[] starts, int[] ends, int codePoint)
    {
        for (var i = 0; i < starts.Length; i++)
        {
            if (codePoint >= starts[i] && codePoint <= ends[i]) return true;
        }

        return false;
    }
}
