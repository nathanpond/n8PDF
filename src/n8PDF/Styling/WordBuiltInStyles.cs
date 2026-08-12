using n8PDF.Ooxml;

namespace n8PDF.Styling;

/// <summary>
/// The parts of Word's built-in style definitions that a document can leave unstated.
/// </summary>
/// <remarks>
/// Word ships definitions for its built-in styles and merges them into whatever a document's
/// <c>styles.xml</c> declares. A document that defines <c>Normal</c> as an empty element still
/// gets Word's spacing for it, so a converter that honours only what the file says will place
/// every paragraph after the first too high.
///
/// Only measured values live here. Each was recovered by rendering a document through Word and
/// reading the geometry back out of the PDF, not from documentation — see the
/// <c>builtin-normal-*</c> fixtures, which state one property and leave the other open so the two
/// can be separated. Word's own values are believed to be
/// <c>w:after="160" w:line="278" w:lineRule="auto"</c>, and all three fixtures agree with that to
/// within Word's 1/300 inch vertical quantum.
///
/// Styles beyond Normal are deliberately absent. There is no evidence Word applies built-in
/// definitions for the heading styles here: a probe applying identical properties under
/// "heading 1", "heading 2" and a custom name produced identical geometry once the page-break
/// spacing rule was corrected. Anything added should be measured the same way first.
/// </remarks>
public static class WordBuiltInStyles
{
    /// <summary>Word's built-in space-after for the Normal style, in twips (8 points).</summary>
    public const int NormalSpacingAfterTwips = 160;

    /// <summary>
    /// Word's built-in line spacing for the Normal style, in 240ths of a line — a multiple of
    /// about 1.158.
    /// </summary>
    public const int NormalLine = 278;

    /// <summary>
    /// Paragraph properties Word supplies for the Normal style.
    /// </summary>
    /// <remarks>
    /// These sit <em>below</em> the document's <c>docDefaults</c> in precedence, not above. A
    /// document that states line spacing in <c>docDefaults</c> and leaves its Normal style empty
    /// gets the value it stated — verified against Word, whose output matches the document's
    /// <c>docDefaults</c> in exactly that case. They are a fallback for what nothing states, not
    /// an override.
    /// </remarks>
    public static ParagraphProperties NormalParagraphProperties => new()
    {
        SpacingAfterTwips = NormalSpacingAfterTwips,
        Line = NormalLine,
        LineRule = LineSpacingRule.Auto
    };
}
