namespace n8PDF.Text;

/// <summary>
/// Which way a character runs, and how it behaves among characters that run the other way.
/// </summary>
/// <remarks>
/// The names are the standard's own. Three of them are strong — a letter of a left-to-right
/// script, a letter of a right-to-left one, and an Arabic letter, which is right-to-left and
/// changes the numbers around it as well. The rest are weak or neutral: a digit, a comma, a
/// space, a bracket. Nearly all the difficulty of writing two directions on one line is in what
/// those weak and neutral characters do when they fall between the strong ones.
/// </remarks>
internal enum BidiClass
{
    /// <summary>Left-to-right: a Latin, Greek or Cyrillic letter, and most of what is unassigned.</summary>
    L,

    /// <summary>Right-to-left: a Hebrew letter, and the scripts written the same way.</summary>
    R,

    /// <summary>An Arabic letter, which is right-to-left and makes the digits after it Arabic.</summary>
    AL,

    /// <summary>A European digit.</summary>
    EN,

    /// <summary>A sign that belongs to a European digit: plus, minus.</summary>
    ES,

    /// <summary>A terminator of a European number: a currency sign, a per cent.</summary>
    ET,

    /// <summary>An Arabic-Indic digit.</summary>
    AN,

    /// <summary>A separator inside a number: a comma, a full stop, a colon.</summary>
    CS,

    /// <summary>A mark drawn on the character before it, which takes that character's direction.</summary>
    NSM,

    /// <summary>A character that is not drawn and counts for nothing.</summary>
    BN,

    /// <summary>The end of a paragraph.</summary>
    B,

    /// <summary>A tab, which divides a line into segments.</summary>
    S,

    /// <summary>Whitespace.</summary>
    WS,

    /// <summary>Neutral: punctuation, symbols, everything with no direction of its own.</summary>
    ON,

    LRE,
    RLE,
    LRO,
    RLO,
    PDF,
    LRI,
    RLI,
    FSI,
    PDI
}
