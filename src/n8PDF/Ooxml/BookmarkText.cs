using System.Text;

namespace n8PDF.Ooxml;

/// <summary>
/// Collects the text each bookmark covers, which is what a REF field shows.
/// </summary>
/// <remarks>
/// A bookmark is a pair of markers rather than a container: everything between the start and the
/// end belongs to it, including whatever other bookmarks begin and end in between, so several can
/// be open at once and each collects the text it sees while it is.
/// </remarks>
internal static class BookmarkText
{
    public static Dictionary<string, string> Collect(WordDocument document)
    {
        var open = new Dictionary<int, (string Name, StringBuilder Text)>();
        var collected = new Dictionary<string, string>(StringComparer.Ordinal);

        Walk(document.Body, open, collected);

        // A bookmark whose end is missing — or which reaches past the body — keeps what it saw.
        foreach (var (_, (name, text)) in open) collected[name] = text.ToString();

        return collected;
    }

    private static void Walk(
        IEnumerable<BlockElement> blocks,
        Dictionary<int, (string Name, StringBuilder Text)> open,
        Dictionary<string, string> collected)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    Walk(paragraph, open, collected);
                    break;

                case Table table:
                    foreach (var row in table.Rows)
                    foreach (var cell in row.Cells)
                        Walk(cell.Content, open, collected);

                    break;
            }
        }
    }

    private static void Walk(
        Paragraph paragraph,
        Dictionary<int, (string Name, StringBuilder Text)> open,
        Dictionary<string, string> collected)
    {
        // Paragraphs run together rather than into each other: a bookmark over two of them reads
        // as two sentences rather than one long word.
        if (open.Count > 0)
        {
            foreach (var (_, (_, text)) in open)
            {
                if (text.Length > 0) text.Append(' ');
            }
        }

        foreach (var run in paragraph.Runs)
        foreach (var content in run.Content)
        {
            switch (content)
            {
                case BookmarkInline bookmark:
                    open[bookmark.Id] = (bookmark.Name, new StringBuilder());
                    break;

                case BookmarkEndInline end when open.Remove(end.Id, out var finished):
                    collected[finished.Name] = finished.Text.ToString().Trim();
                    break;

                case TextInline text when open.Count > 0:
                    foreach (var (_, (_, builder)) in open) builder.Append(text.Text);
                    break;

                case FieldInline field when open.Count > 0:
                    // What a field last showed is the best that can be said about it here: this
                    // runs before layout, so a page number has no value yet.
                    foreach (var (_, (_, builder)) in open) builder.Append(field.CachedText);
                    break;

                case TabInline when open.Count > 0:
                    foreach (var (_, (_, builder)) in open) builder.Append('\t');
                    break;
            }
        }
    }
}
