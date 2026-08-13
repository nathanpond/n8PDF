namespace n8PDF.Text;

/// <summary>Which side of itself a character joins on.</summary>
internal enum Joining
{
    /// <summary>Joins on neither side. Most of the characters there are.</summary>
    None,

    /// <summary>Joins to what follows it only.</summary>
    Left,

    /// <summary>Joins to what precedes it only — alef, dal, ra and their like.</summary>
    Right,

    /// <summary>Joins on both sides, which most Arabic letters do.</summary>
    Dual,

    /// <summary>Joins on both sides without being a letter: the tatweel that stretches a word.</summary>
    Join,

    /// <summary>Stands between two letters without breaking the join: a vowel mark, a dot.</summary>
    Transparent
}

/// <summary>Which of its four shapes a letter is drawn in.</summary>
internal enum JoiningForm
{
    /// <summary>Joined on neither side: a letter standing alone.</summary>
    Isolated,

    /// <summary>Joined to what follows: the shape a word begins with.</summary>
    Initial,

    /// <summary>Joined on both sides: the shape in the middle of a word.</summary>
    Medial,

    /// <summary>Joined to what precedes: the shape a word ends with.</summary>
    Final
}

/// <summary>
/// Which shape each letter of a run of Arabic takes.
/// </summary>
/// <remarks>
/// An Arabic letter is written differently depending on what stands beside it. Most letters join on
/// both sides and so have four shapes — alone, opening a word, inside one, ending one — and a
/// handful join only on the right, which is why a word can end in the middle of itself: nothing
/// after alef or dal joins back to them, and the letter following starts a new shape as though it
/// began a word.
///
/// None of this is a property of the character. The same letter is the same character in all four
/// shapes; which is drawn depends on its neighbours, and the font holds the four as separate
/// glyphs to be swapped in. Working out which is what this does; swapping them is the font's
/// business, through its substitution tables.
///
/// Marks are passed over as though they were not there. A vowel written above a letter must not
/// break the join between that letter and the next, which is what makes the type "transparent" the
/// most important of the six.
/// </remarks>
internal static class ArabicJoining
{
    /// <summary>The joining type of a character, from the Unicode character database.</summary>
    public static Joining TypeOf(int codePoint)
    {
        var starts = JoiningTables.Starts;

        var low = 0;
        var high = starts.Length - 1;

        while (low <= high)
        {
            var middle = (low + high) / 2;

            if (codePoint < starts[middle]) high = middle - 1;
            else if (codePoint > JoiningTables.Ends[middle]) low = middle + 1;
            else return JoiningTables.Kinds[middle];
        }

        return Joining.None;
    }

    /// <summary>Whether a run holds anything that joins at all, and so needs any of this.</summary>
    public static bool Joins(string text)
    {
        foreach (var character in text)
        {
            if (TypeOf(character) is Joining.Dual or Joining.Right or Joining.Left or Joining.Join)
                return true;
        }

        return false;
    }

    /// <summary>
    /// The shape each character of a run takes, given what stands either side of it.
    /// </summary>
    /// <param name="text">The run, in the order it is read.</param>
    public static JoiningForm[] Forms(string text)
    {
        var forms = new JoiningForm[text.Length];
        var types = new Joining[text.Length];

        for (var i = 0; i < text.Length; i++) types[i] = TypeOf(text[i]);

        for (var i = 0; i < text.Length; i++)
        {
            forms[i] = JoiningForm.Isolated;

            if (types[i] is not (Joining.Dual or Joining.Right or Joining.Join)) continue;

            // What precedes it, passing over anything transparent: a letter joins backwards only
            // where what came before joins forwards.
            var before = Previous(types, i);
            var after = Next(types, i);

            var joinedBefore = before is Joining.Dual or Joining.Left or Joining.Join;
            var joinedAfter = after is Joining.Dual or Joining.Right or Joining.Join;

            // A letter that joins only on the right takes the shape that reaches back and no more,
            // whatever follows it.
            if (types[i] == Joining.Right) joinedAfter = false;

            forms[i] = (joinedBefore, joinedAfter) switch
            {
                (true, true) => JoiningForm.Medial,
                (true, false) => JoiningForm.Final,
                (false, true) => JoiningForm.Initial,
                _ => JoiningForm.Isolated
            };
        }

        return forms;
    }

    private static Joining Previous(Joining[] types, int at)
    {
        for (var i = at - 1; i >= 0; i--)
        {
            if (types[i] != Joining.Transparent) return types[i];
        }

        return Joining.None;
    }

    private static Joining Next(Joining[] types, int at)
    {
        for (var i = at + 1; i < types.Length; i++)
        {
            if (types[i] != Joining.Transparent) return types[i];
        }

        return Joining.None;
    }
}
