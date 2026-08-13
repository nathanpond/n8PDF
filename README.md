# n8PDF

Converts `.docx` to PDF. Written from scratch: no third-party DOCX or PDF library, no headless
Word, LibreOffice, browser engine, sidecar service or container. A consumer adds one assembly
reference and calls one method.

```csharp
Converter.ConvertFile("report.docx", "report.pdf");
```

## The constraint

`src/n8PDF` carries **zero** `PackageReference` entries, and `LibraryInvariantTests` fails the
build if that ever changes. The only things it builds on are the base class library's
`System.IO.Compression` (the DOCX container, and Flate for PDF streams) and `System.Xml.Linq`.
Everything domain-specific is ours: OPC relationships, WordprocessingML semantics, the style
cascade, TrueType/OpenType parsing, text measurement, line breaking, pagination, and the PDF
writer including font embedding.

## Layout

```
src/n8PDF/
  Packaging/     OPC container: zip, content types, relationships
  Ooxml/         WordprocessingML model and parsers, plus Units
  Styling/       the formatting cascade, producing Resolved*Format
  Fonts/         SFNT parsing, metrics, font resolution, shaping
  Text/          the bidirectional algorithm and the Unicode tables it reads
  Images/        PNG, GIF, BMP, TIFF, EMF and JPEG decoding
  Layout/        measurement, line breaking, page composition, list counters
  Pdf/           object model, writer, content streams, Type0 font embedding
  Diagnostics/   LayoutTrace — the testing spine
  Converter.cs   public API
tests/n8PDF.Tests/
  Fixtures/Minimal/     hand-authored .docx, one feature each (generated, committed)
  Fixtures/Real/        documents Word itself wrote (tools/make-real-fixtures.sh)
  Fixtures/Reference/   Word-exported reference PDFs (tools/make-reference-pdfs.sh)
  Golden/               committed layout traces
```

Data flows one direction: **Packaging → Ooxml → Styling → Layout → Pdf**, with `Fonts` serving
Layout and Pdf. Word's page origin is top-left and PDF's is bottom-left; the flip happens once,
in `PdfRenderer`, and nowhere else.

## Running

```bash
dotnet test                        # everything
N8PDF_BLESS=1 dotnet test          # re-bless layout goldens after an intended change
N8PDF_REQUIRE_QPDF=1 dotnet test   # fail rather than skip if qpdf is missing (use in CI)
tools/make-reference-pdfs.sh       # generate missing Word reference PDFs (macOS + Word)
```

Converted fixtures are written to `artifacts/test-output/` for eyeballing. That directory is
git-ignored.

## Validation

Four tiers, cheapest and most diagnostic first.

1. **Layout goldens** (`Golden/*.json`) — every positioned run's coordinates, font and size,
   compared against a committed trace. A failure names the run that moved and by how much. These
   prove nothing changed, not that we are correct.
2. **Unit and structural tests** — font metrics against published values, unit conversions, the
   style cascade including toggle-property cancellation, and PDF structure read back out of the
   generated file.
3. **Reference comparison** — against PDFs exported from Word into `Fixtures/Reference/`, named
   after the fixture. This is the only tier that can say we match Word. Every fixture is required
   to have one: a missing reference fails rather than skips, because a skipped comparison is
   indistinguishable from a passing one. Generate them with `tools/make-reference-pdfs.sh`.

   Both PDFs are read through one content-stream parser and compared line by line in points.
   Across the fixtures, line start positions match Word exactly and every baseline agrees to
   within 0.46pt, and to within 0.29pt in every document that does not raise or lower a run —
   close to Word's own vertical quantum of 1/300 inch. `Fidelity_report` writes
   the full per-line table to `artifacts/test-output/fidelity-report.txt`.

4. **Structural validation** — `qpdf --check` over every converted fixture, verifying the
   cross-reference table, stream lengths and object graph we hand-rolled. A tolerant viewer will
   render a structurally broken PDF perfectly well, so this catches what eyeballing cannot.
   Optional: `brew install qpdf` to enable, or `N8PDF_REQUIRE_QPDF=1` to fail when it is absent.

### Viewers

Preview uses Apple's Quartz engine and Chrome uses PDFium — two independent implementations. Both
agreeing is decent evidence a file is well formed; they are not a substitute for tier 4. Note that
glyph *positions* come from the content stream, so all conforming viewers agree on geometry; what
differs between them is rasterisation, and the one feature of ours genuinely sensitive to that is
synthetic bold (text render mode 2).

## Matching Word

Several layout rules here were derived by measuring Word's own output rather than from the spec,
each with a fixture built so that only one candidate model survives:

- Adjacent paragraph spacing **collapses to the larger** of space-after and space-before, it does
  not sum. Across a page break the collapse still applies, but the previous paragraph's
  space-after is absorbed by the page it ended on.
- A line-spacing multiple's **extra leading goes below** the baseline, so the first line of a
  paragraph sits at its natural ascent whatever the multiple.
- The font's **line gap belongs above the ascent**, not below the descent.
- Word **fills in what a document's styles leave unstated** from its own built-in definitions —
  see `WordBuiltInStyles`. These sit *below* the document's `docDefaults` in precedence: they are
  a fallback for what nothing states, not an override. Set
  `ConversionOptions.ApplyWordBuiltInStyleDefaults = false` to render strictly what the document
  says.

Text wrapping around a floating picture is single-pass: a float's exclusion applies to the text
laid out from its anchor onwards, while Word lays out the whole page and reflows text above the
anchor too. The one case where that shows — a float whose top clearance reaches back over the
previous paragraph's last line — is handled by moving those lines down, which is correct because
a full-width float does not change their width. A partial-width float reaching backwards would
need the line broken again, and is not handled.

Three rules about how tall a line is were settled by `superscript-probe` and `numbering`, and none
of them is what the code did before it was asked. A raised or lowered run keeps the line box of the
size it was *given*, not the smaller size it is *drawn* at: a twenty point superscript in a twelve
point line makes that line as tall as a twenty point one, above the baseline and below it, while a
twelve point superscript in a twelve point line changes nothing at all. A line's box is the tallest
ascent over the deepest descent across its runs, which is not the tallest of the runs' own boxes —
twelve point Times with an eleven point Calibri mark on the end takes the Times ascent and the
Calibri descent, and is deeper than either font alone would make it. And a list's number is drawn
on its line without being part of its box, which is the one thing on a line that is not.

The shifts themselves were measured at three sizes rather than fitted to one. A lowered run drops
about a twelfth of its size, which is far less than it looks like it should be and was nearly twice
that here; a raised one rises about a third.

Word also quantises vertical positions to 1/300 inch (0.24pt). That is not implemented — our
residuals are already smaller than one quantum — but it is the floor on how closely anything can
match Word vertically.

## Current scope

Implemented: cells merged down the page (`w:vMerge`), which behave as one tall cell — no rule is
drawn across the run, its shading covers all of it, and its text is placed over the run as a whole,
by its own vertical alignment. The rows a run covers keep the heights their own cells ask for and
the merged text runs down through them, so three lines merged across three one-line rows leave
those rows a line tall each; only what will not fit makes the run taller, and the last row of the
run takes all of it. A run reaching the foot of the page divides there wherever the break falls —
inside the row it begins in as readily as between the rows it covers — and Word rules the merged
cell at a page break although it rules none between a run's own rows, so both halves are closed
boxes. Breaking a table row across a page, at a line boundary inside its cells, with both
halves closed off by a full border box the way Word draws them — and moving the row whole instead
where it says `w:cantSplit` or where not even a line of it would fit. Vertical page alignment (top, centred, bottom, and justified — which spreads the
spare height between the paragraphs rather than between the lines, so a paragraph that wraps stays
whole), font subsetting for both kinds of outline. A TrueType face is numbered again from
nothing, so a document that used thirty glyphs embeds thirty rather than the three thousand the
face has; a CFF one keeps its numbering — renumbering it means rewriting what its charset says
about every glyph — and has its charstrings and the subroutines nothing reaches emptied instead.
Times New Roman goes from 676KB to 33KB, Hiragino Sans GB from 11.4MB to 497KB. Kerning, read from a font's GPOS table as well as the legacy one — Calibri has only
the former and Times New Roman only the latter, so both are needed to kern either — applied where
`w:kern` asks for it and from the type size it names upwards, tab stops of every alignment — left, centre, right and decimal, the last three
resolved once the text after the tab has been measured, with a stop the line has already passed
falling through to the next one, and leaders filling the gap a stop opens (dots, hyphens,
underscores and middle dots, set on a grid measured from the edge of the page so that entries of
different lengths line up with each other), and the vertical rule a bar stop asks for, down every
line of its paragraph — widow and orphan control (two lines of a paragraph on each side of a page or column
break, or none — a three-line paragraph moves whole, since it cannot give two to both), keeping a
paragraph with the next one and keeping its own lines together (`w:keepNext` and `w:keepLines`,
including chains of headings that move as one), section breaks (next-page, continuous, even- and odd-page, each section with its own
page size, orientation and margins, running heads inherited per kind from the section before where
one says nothing), multiple text columns (evenly divided or individually stated, column breaks,
the rule down the gap where the document asks for one, and the last page of a section evened out
between its columns where a continuous break closes it), footnotes and endnotes (numbered in reference order, arabic for footnotes and roman
for endnotes unless the document says otherwise, and numbered again from the beginning on every
page or in every section where the section asks for it; a footnote goes to the foot of the page its
reference lands on — or under the last line of text on it, where the section asks for that — and
takes that space out of the body above it, dividing between that page and
the next where it is too long for the room left under it, an endnote carries on after the
body like ordinary content — or after each section where the document asks for that — and both are
ruled off by the separator), hyperlinks (external addresses as clickable regions, internal links to bookmarks
anywhere in the document, with the regions placed and padded the way Word places them), headers
and footers (per page, with separate first-page and even-page variants, and
fields evaluated), fields — page numbers (PAGE, NUMPAGES, SECTION,
SECTIONPAGES, PAGEREF, each following a section that begins its numbering again), what a document says about itself (AUTHOR, TITLE, SUBJECT, KEYWORDS,
COMMENTS, LASTSAVEDBY, CREATEDATE, SAVEDATE, PRINTDATE, DOCPROPERTY, FILENAME), counters (SEQ, with
its restart, repeat and format switches), references to a bookmark's text (REF), running heads
(STYLEREF), literal text
(QUOTE), and the clock (DATE, TIME) — each spelled the way its `\*` switch asks, in arabic, roman,
letters, ordinals, words, hex or dollars, and cased by Upper, Lower, FirstCap or Caps, with a
`\@` picture deciding how a date reads, lists and numbering (decimal, letters, roman and bullets, nested levels with
independent counters and multi-level templates, hanging indents), images, inline and floating (PNG — interlaced or not — GIF, BMP, TIFF and EMF all read from scratch, JPEG passed through untouched — and decoded, in every
form including progressive, arithmetic and four channels of ink, where a TIFF holds one; the
four-channel pictures a printing press wants, as either a JPEG or a TIFF; transparency via a soft mask; square, top-and-bottom and no-wrap text flow around anchored
pictures), tables (fixed and autofit column sizing, horizontal spans, borders, shading,
cell margins and vertical alignment, rows kept whole across page breaks), page size and margins
from `sectPr`, paragraphs and runs, `xml:space` handling,
line and page breaks, tabs (left-aligned stops), font family via theme resolution, size, bold,
italic, underline, strikethrough, colour, caps, super/subscript, character spacing and scaling,
alignment including justification, indents including hanging, spacing before/after with
contextual spacing, line spacing (auto/exact/at-least), pagination, real font metrics with
`.ttc` support, and Type0/CIDFontType2 embedding with a `ToUnicode` map so text stays selectable.

Table autofit is the one piece here that approximates rather than reproduces. Word's algorithm is
undocumented; ours measures each column's minimum (widest word) and maximum (unwrapped) width and
shares out the available space between them. It reproduces both behaviours that were measured —
content-width columns when the table fits, and a table filling the text area exactly when it does
not — and agrees with Word to 0.16pt on `table-autofit-probe`, but it is not derived from the real
algorithm the way the paragraph rules are.

How far inside its edge a cell starts its text was settled by measuring rather than by reading, and
is stranger than it sounds. `table-inset-weights-probe` holds the same one-cell table fifteen times
over — border weights from nothing to six points against no margin, then margins against a fixed
border, then no margin element at all — each on a page of its own so that no table's height carries
into the next one's position. Word's export of it gives three rules, and none of them is the
addition that would be guessed:

- Across, the inset is the greater of the cell margin and half the border, not their sum. Half a
  border falls inside the cell and half outside — Word's own border rectangles straddle the margin
  at every weight — so text starts at the border's inner edge unless a margin reaches further in.
- Down, the whole border is cleared rather than half of it. A six point border pushes the first
  line six points down and three points across.
- Declaring no cell margin is not the same as declaring one of zero: Word puts half a point in a
  table that says nothing about the matter. The familiar 108 twips comes from the built-in
  `TableNormal` style, which a hand-written document does not have, but what is left is not
  nothing. Word rounds every position to 1/300 inch, so the true value is somewhere between seven
  and twelve twips; ten is used.

A field that depends on where it falls cannot be worked out while the page it is on is still being
filled, so a document holding one is laid out twice: the first pass records the page each field
landed on and how the pages divide between sections, and the second uses it. Word settles its own
page numbers the same way, and like Word this converges rather than being exact — a field whose
text changes length between the two passes could in principle move to another page and be a page
out. The second pass is only run for a document that needs one.

Word itself recalculates only these page-dependent fields when it exports, and leaves every other
field showing whatever it last computed, which is what the fields fixture's reference had to work
around: it is exported with its fields updated first, and `tools/make-reference-pdfs.sh` names the
fixtures that are. Anything this converter cannot work out keeps its cached result, which is the
honest answer — showing nothing would lose text the document has, and guessing would show something
it never said. COMPANY and MANAGER are among those: the Word this was measured against does not
evaluate them either.

A running head is the one field whose answer depends on where the pages fell rather than only on
what the document says, and Word's rules for it were read off its export of the `styleref` fixture:
a header shows the first paragraph of the named style on its page, a footer looks *down* its page in
the same way rather than up it, `\l` asks for the last one on the page instead, and a page holding
none of that style carries the last one before it — which is what walks a chapter title through the
pages under it. In the body the field looks backwards to the nearest one above it, and only where
there is none does it look forward. The style is named rather than identified: Word answers
`STYLEREF Heading1` with an error telling the reader to apply the style, so an id is not a name
here even where it looks like one.

A table of contents is the one field whose answer is a run of paragraphs rather than a few words,
and it is worked out again rather than read back: a stale table is as wrong as a stale page number,
and a document that has never had one built has nothing to read back at all. The headings are
gathered by the outline level their style stands at — `\o "1-3"` says which levels, `\t
"Style,Level"` names styles outright, `\n` leaves the page numbers off — and each entry is set in
the `TOCn` style named for its level, with a tab out to that style's right stop and the page the
heading landed on. A document defining no such styles gets an indent and a dotted leader of its own
so that the numbers still line up.

Two details of Word's own come from measuring its export of the `toc` fixture rather than from
reasoning. Tab stops are measured from the margin and not from the paragraph's indent, so the page
numbers of a second-level entry line up with a first-level one's rather than sitting eleven points
further out — which was wrong here until this fixture showed it. And the paragraph the field closes
in outlives it, empty: Word leaves the mark of it on a line of its own below the entries, set in the
document's default rather than in a table-of-contents style, which is the extra line between a table
of contents and the first heading under it.

An index is written in two halves, and both are implemented. Where a term belongs, the document
carries an `XE` field that draws nothing at all — it is there to be found, not read — and where the
index goes, an `INDEX` field gathers every one of them, sorts them, and lists each term against the
pages it was marked on. A term written `Engine:analytical` is a subentry and reads as `analytical`
under a heading of `Engine`, indented by its `Index2` style; a page marked twice over is one page
number; `\h` asks for a line holding the letter each group begins with, `\e` and `\l` say what goes
between a term and its pages and between one page and the next, `\t` shows something else in place
of the pages ("see Engine"), and `\f` keeps two indexes in one document apart.

A document written for a mail merge does not carry its own data: it names a source — a
spreadsheet, an address book — that only the machine it was written on can reach. Converted as it
stands, its fields show what Word shows for the same document, each field's own name in guillemets
with whatever `\b` and `\f` ask to be printed around it, so a letter reading `Dear «Title»,` reads
that way. Converted with a `MergeRecord` given to it, the same document reads as the letter itself
— and the text around a field then prints only where the field has something to print, so an empty
middle name takes its brackets with it. MERGEREC and MERGESEQ number the record, and the fields
that carry a merge from one record to the next (NEXT, NEXTIF, SKIPIF) draw nothing, which is what
Word draws for them.

The two fields that work something out rather than look it up are the last of them. IF compares two
things and chooses between two pieces of text — numbers as numbers, anything else as text without
regard to case, and the text an equality is asked against may hold `*` and `?` wildcards. A formula
field is an equals sign and an expression: the five operators and their precedence, brackets,
comparisons, percentages, and the functions Word knows (SUM, PRODUCT, AVERAGE, COUNT, MIN, MAX,
ABS, INT, ROUND, SIGN, MOD, AND, OR, NOT, IF, TRUE, FALSE, DEFINED). In a table it reads the cells
around it, by direction or by name.

Three of its answers were measured rather than reasoned about, and none is what would be guessed. A
picture's `#` reserves a *space* where it has no digit to show, so five against `$#,##0.00` comes
out as `$   5.00`. A direction reads only as far as the numbers go — a column of 10, "n/a" and 3
sums to 3 from below it, not to 13 — while a range named outright reads all of it and passes over
what is not a number, so the same column averaged as `A1:A3` is 6.5 rather than 4.33. And a formula
with no picture reads to two decimal places with the zeros at the end dropped: 10/3 is 3.33, 10/4
stays 2.5.

Pictures come in as GIF, BMP, TIFF, PNG or JPEG. Only JPEG passes through untouched — PDF's own
image filter is JPEG, so decoding and re-encoding it would cost quality for nothing — and the rest
are unpacked to samples by decoders written here: a bitmap of one to thirty-two bits a pixel,
written from the foot up or the top down and run-length packed or not; a GIF through its colour
table, interlaced or not, with the colour it treats as transparent becoming the mask a PDF carries
separately; a TIFF at either end, in grey, colour or a palette, written in strips of rows or in
tiles, with its channels together or each kept apart, packed with nothing, LZW, PackBits, Deflate
or one of the fax encodings.

The two layouts are the format's other way of dividing a picture up. Tiles are rectangles rather
than rows, each written at the full size the tags declare however little of the picture it covers,
so the ones along the right and the foot carry padding that has to be left behind. Kept apart, the
channels are three pictures of one sample each rather than one picture of three, laid over one
another at the end. Both were written and read back through `sips` as well as here, which is what
said a tile's sides must be multiples of sixteen: it refused a file of eight-by-four tiles that
this read quite happily.

A fax is not written as pixels at all. A page of black on white is sent as the lengths of the runs
its lines fall into, in a Huffman code the standard fixes rather than one built from the page — and
a line may be written against the line above it instead, saying how far each change of colour has
moved rather than where it is, which on a page of text is far shorter. All three encodings are
read: lines written on their own, lines written either way with a bit each saying which, and lines
written against one another throughout.

Getting that right took the two-way check further than anywhere else here. The tables are large and
mechanical, so a file is written with the library's own tables and handed to macOS's `sips` to
read: if a single code were wrong it could not read it. Then the same file is read back here. The
first version of the writer took the easy way and wrote every group 4 line in full, spelling out
its runs — legal, and passed. Rewriting it to write lines the way a fax actually does, against one
another, immediately produced a file `sips` read perfectly and this one got 171 pixels of 480
wrong: the reader was starting each line at its first pixel where the standard starts one pixel
before it, so a change at the very start of a line could never be found. No amount of round-tripping
against the easy encoding would have shown it.

An interlaced PNG is not one picture but seven, each a coarser or finer sieve of the whole — the
first every eighth pixel of every eighth row, the last every pixel of every other row — and each is
written as an image in its own right, with its own rows and its own filters over them. So each pass
is unfiltered on its own and its pixels are then put where they belong, a few bits at a time where
four of them share a byte.

A picture written with sixteen bits a sample keeps them, in a PNG or in a TIFF. A PDF carries
either precision, so reducing one to eight would throw away exactly what it was written to keep. A
PNG and a PDF both write the bigger half of a sample first, so those two bytes go through as they
lie; a TIFF writes them the way round the rest of its numbers go, which has to be read from the
file rather than assumed — a sample read from the wrong end is still a picture, only the wrong one.
The single thing still reduced to eight bits is the colour table of a TIFF written through a
palette, where the index is what the depth describes and the table is a few hundred entries whose
lower halves no document has ever needed. What says the halves have
not been transposed or the precision misdeclared is the page itself: a picture written at sixteen
bits and described as eight comes out as noise, so the check is that a reader which shares nothing
with this one draws the colour that went in.

Reading a format is easy to do nearly right, so these are checked two ways. Files whose every pixel
is known are built by the tests and read back — which is the only way to reach a bitmap written
upside down or a GIF written in four passes — and then the same picture is turned into each format
by macOS's own `sips`, and by `tiffutil` for each of the TIFF packings, and read back again: what
comes out has to be what the PNG it was made from holds, to the sample. Both found real faults. A
bitmap's height is at a different offset in the modern header than in the old one, and a TIFF
written big end first keeps a small number in the *high* half of the four bytes its tag reserves.

A metafile is not a picture at all but the record of one being drawn — move here, line to there,
fill with this, write that — so reading one is an interpreter rather than a decoder, and what comes
out stays a drawing all the way to the PDF, whose own operators write it out again. That keeps a
chart sharp at any size a reader looks at it, and it keeps the text inside one selectable. What is
handled is what a picture in a document is made of: paths and the shapes that are shorthand for
them, the pens and brushes that colour them, the fonts and the text, and the bitmaps a drawing can
carry. What is not is the rest of an interface built to drive a screen: raster operations, clipping
regions and palettes.

A metafile written by anything modern carries the same drawing twice — once in those records, and
once in the newer GDI+ ones that travel inside their comments, a format smuggled through a format.
Both are read, and where a file has the newer ones they are what draws it. That is what they are
for: the old records beside them are a copy left for readers that have never heard of the new, and
where the two differ at all it is the new half that is the fuller. They are read in the one order
the file puts them in, because the old records are not always only a copy — a file may hand the
drawing back to them part way through, for something the newer interface had no way to record, and
says so where it does. From there the old records draw until the newer ones resume, which is what
the specification asks for and what the file means.

Preferring them used to be impossible to justify, and the reason is worth keeping. Word for Mac
renders classic metafile records and draws nothing whatever for EMF+ ones — its export of a file
holding only those is a blank space — so there was no second implementation here to read EMF+
against, and everything else in this project is measured against Word. What settles it is that a
file carrying both carries one picture twice: Word draws the old half of the metafile fixture and
this draws the new, so comparing the two pages compares this reading of EMF+ against Word after
all. The drawing's text lands within 0.07pt across and 0.12pt down of where Word puts it, and the
two pages agree on 97% of what is covered and what is left as paper — the rest being the edges
along which no two renderers ever agree. That is the same standard the rest of the metafile work is
held to, and it is now the newer records being held to it.

Its text needed one measurement. A drawing says where the *top* of its text goes and a PDF says
where the baseline goes, and the distance between them is the height of the characters themselves:
the em less what hangs below the line, with the leading above them left out. Word's own rendering
of the fixture puts the baseline 10.98pt below the point the record names, at 14pt Times New Roman,
and the em less the descent is 10.97pt of it.

A drawing is also the one thing here that cannot be checked by reading text positions out of a PDF:
a chart could be drawn upside down and the comparison would not notice. So `tools/rasterize.swift`
draws the page with macOS's own PDF reader and the pixels are looked at — which is how the drawing
in the fixture is known to be where it was put, in the colours it was given. It found a real fault
in the *fixture* rather than the reader: a metafile says how big it is twice over, in the frame it
declares and in the resolution of the device it was recorded for, and the test's writer had been
writing the two at odds. Word believed one and this believed the other, and both were right.

A TIFF may also hold a JPEG rather than pixels, and that one is not decoded at all: a PDF carries a
JPEG as the file it already is, so the work is putting the file back together rather than taking it
apart. A TIFF divides one in two ways — the older keeps the whole file in a tag of its own, and the
newer keeps the tables every scan shares apart from the scan itself, so that a picture in many
strips need not repeat them, which makes the file the tables without their end followed by the scan
without its beginning. What comes out is handed to `sips` to read, because a file put back together
wrongly still parses as far as its header: reading its size back would prove nothing.

A picture divided into several JPEGs, one to a strip, is the one case that has to be decoded: the
strips are separate files with nothing in common but the picture they are parts of, and a PDF has
no way to be handed several of them as one image. So there is a baseline JPEG decoder here after
all, used for that alone — a JPEG holds not the picture but a description of it, each block of
eight by eight pixels written as how much of each of sixty-four waves it is made of, and reading
one is that in reverse. Two decoders never agree to the sample, since they round the same sums
differently, but ours and the one macOS uses agree to within six levels of 255 on the same file,
and a strip put in the wrong place would be out by far more than that.

A JPEG's numbers may be written all at once or a little at a time, and both are read. A sequential
file gives every wave of a block before moving to the next; a progressive one gives the coarsest
waves of the whole picture first and returns for the rest, and may send the high bits of a number
in one pass and its low bits in another — so the numbers are gathered and turned into pixels only
once the last pass has been read. A progressive file is where a JPEG is most easily read *nearly*
right: a pass misread leaves a picture that is still a picture, only softer or blockier than it
should be. So these are tested against real ones rather than any this could write — macOS ships
several, written by encoders that had no idea this existed — and read against its decoder they
agree to three levels of 255, mean under a tenth, at sizes up to 2048 square.

A JPEG may also be coded arithmetically rather than by code tables, which almost nothing writes —
the method was patented for most of the format's life — but files exist and there is no reading
round one. Nothing here recovers from a mistake: Huffman codes resynchronise at the next symbol,
whereas an interval narrowed by the wrong probability is wrong for ever after, so a decoder of this
is either right or produces noise. That makes it testable in a way little else is. Recoding a JPEG
from one entropy coding to the other changes not one number in it, so the same picture is written
both ways by an encoder that shares nothing with this, and the arithmetic file has to decode to
what the ordinary one decodes to — not close to it, equal to it, sample for sample. It does, for
the sequential and progressive forms, with and without restarts. Reading a whole picture correctly
is not evidence that the hundred and thirteen probability states are right, it is proof of it,
which is worth having: two columns of that table transposed reads eleven decisions correctly and
then quietly falls apart.

A picture bound for a printing press holds four channels rather than three — not the light a screen
adds up to a colour but the ink a press lays down to take light away, and the fourth is black
because the first three together make a muddy brown rather than black. Both a JPEG and a TIFF may
hold one, and both are read. The catch is which way up: Adobe's tools write such a JPEG with nought
standing for all of an ink rather than none of it, and every one in practice is one of theirs.
Nothing warns a reader but a marker beside the picture. Read without noticing, the page comes out
in exactly the wrong colours, which is easy to do and — in a document of photographs — not always
easy to see. What is decoded here is turned back as it is read; a JPEG passing through untouched
cannot be, so the PDF is told instead, and told only where that marker says so.

That is the half no amount of reading samples can settle, so it is tested by drawing the page:
a picture of known inks goes into a document and macOS's own PDF reader draws it, and each quarter
has to come out the colour its ink stands for. The inks were chosen so that no two channels hold
the same value, which is what says they arrive in the right order as well as the right way up. A
TIFF of a press's own inks rather than the four is reported rather than drawn in colours that are
not its own.

Two things have no second opinion behind them. The older way of holding a JPEG inside a TIFF:
`sips` will not read a file written that way at all, so what is tested is that the JPEG comes back
and no more. And the four-channel files whose colours were turned into brightness before coding —
nothing installed here writes one, so that path is transcribed from libjpeg's own conversion rather
than checked against a file, and it is the one thing in these formats that has never met a real
example.

A note too long for the room left under the page its reference falls on is divided rather than
moved: a note belongs to the page its mark is on, so what will not fit goes to the foot of the page
after. Where it divides was read off Word's export of `footnote-split-probe`, and it is simpler
than it looks — the note takes everything left under the line that refers to it, the body stops
there, and the rest carries over. Nineteen of that note's twenty lines fit, which is what this
produces, with every line of both pages within half a point of Word's.

Two things follow from it that are worth stating. The rule above a carried note is drawn right
across the measure rather than the two inches drawn above a note that begins where it stands, which
is Word's way of saying without words that what follows is the end of something; the document keeps
that second rule as a note of its own, in the same way as the first. And a note may outlast the
document it belongs to — one referenced near the end and long enough to fill several pages has no
body text left to carry it — so pages are made for the rest of it, holding nothing else. Word does
the same, which `footnote-overrun-probe` is there to have asked: its second page has no body at all
and the last thirty-seven lines of the note at the foot of it.

A section of columns closed by a continuous break has its last page evened out: the columns come to
much the same depth rather than the first being full and the last empty, which is what a continuous
break is usually inserted to do. A section closed by a break to a new page is not evened out, and
neither is the last section of a document — `columns-balanced` holds all three cases and Word's
export of it says so.

Where the columns divide had to be measured too, since the obvious rules disagree. Word divides
thirty-five lines eighteen and seventeen, and ten lines five and five: a column takes lines while it
is still short of the depth the content is to be divided at, so the line that reaches that depth is
the last one in. Rounding either way gives one of those two answers and not the other.

In a section of columns a note goes under the column its reference is in, set to that column's
measure and ruled off by a separator of its own. Each column keeps its own area, so what one column
gives up for its notes is not taken out of the next: in `footnote-columns`, whose first column
carries two notes and second one, the columns stop 13.4pt apart, and that difference is what says
the space comes out of the column rather than the page.

A section may also ask for its notes under the last line of text rather than at the foot of the
page. On a page whose text reaches the bottom margin the two are the same place, which is most
pages; on one whose text stops early — the last page of nearly every document — the notes come up
with the text, and `footnote-beneath-text` is a document of exactly those two pages.

That fixture answered a question nobody had asked, and corrected this. A line carrying a reference
never moves to make room for its own note. `footnote-carry-probe` puts a reference on the very last
line a page has room for and Word keeps the line there, squeezing the whole note in beneath it;
the other fixture puts one where there is no room left at all, and Word still keeps the line and
carries the whole note to the next page under the wide rule. This used to move the line instead,
which is the obvious way to keep a note with its reference and moves body text Word leaves alone.

A section may begin the page numbering again, which is what a document with a preface does, and at
a number of its own rather than necessarily at one. What follows the restart and what does not was
read off Word's export of `page-numbering-restart`, a document of three sections whose footer counts
pages four ways at once: the page number follows it, the total counts the document through
regardless, and a reference to a page names the number that page is printed as rather than where it
stands. The properties on a section break describe the section it closes rather than the one it
opens, which is what makes a fixture for this worth writing rather than reasoning about — the first
number stated belongs to the pages before it.

Endnotes gather at the end of the document, or at the end of each section where the document asks
for it, which is what a book of chapters does with them. Each group is written where its section
stops, before the break that opens the next one, so the notes of a chapter belong to the pages of
that chapter.

Where the instruction lives is the reverse of everywhere else, and cost a fixture to find out.
Every other thing about how a note is set is read from the section; this one Word reads from the
settings part and nowhere else. A document asking for it in its sections alone — which is what the
format's own reading suggests, and what this fixture was written as — comes back from Word with
every note at the end regardless. Word's own writer puts it in both places, which is what says
which of them it believes: setting the option through Word itself and reading back what it wrote is
how that was settled.

Where a document numbers its notes again from the beginning, two things had to be measured rather
than read. The first is where the instruction lives: the format allows it in the settings, as a
default for the whole document, and in each section's properties. Word reads only the section. A
document asking in its settings alone for its notes to begin again on every page comes back from
Word numbered straight through, so that is what happens here, and there is a test that says so.

The second is per-page numbering itself, which cannot be settled while the page it depends on is
still being filled — the line carrying a mark may yet move to the next page and take its number
with it. So it is done the way page numbers are: the first pass records the page each mark landed
on and the second numbers from that, converging rather than being exact for the same reason. Per
section needs none of that, since a mark is composed inside the section it belongs to. Endnotes
restart by section too, and are still gathered at the end of the document, so their numbers repeat
down the one list — which looks wrong until you check, and is exactly what Word does with the same
document.

Not yet: for pictures, nothing a document holds, and nothing left of these formats at all. What
remains elsewhere is the faces that describe their shaping only in Apple's state tables, with no
OpenType tables to read — and Hangul, whose syllables are composed rather than shaped.

A line of Hebrew or Arabic is not a line drawn backwards. Text is stored in the order it is read
and drawn in the order it appears, and the two part company the moment a line holds both
directions, which nearly every real line does: a number is written left to right whatever is around
it, and so is a Latin name inside a Hebrew sentence. The Unicode bidirectional algorithm decides
which way each character runs and what order the line is drawn in, and it is implemented here in
full — the paragraph rules, the explicit embeddings and isolates, the weak and neutral characters,
the bracket pairs, the levels and the reordering.

The tables it reads are generated from the Unicode character database rather than written, by
`tools/make-bidi-tables.py`, because they are a hundred thousand answers and any of them typed by
hand would be a chance to be wrong. The library carries no dependencies, so the output is committed
as source.

This is the one part of the converter with a reference implementation to hand, and it is checked
the way that deserves: GNU FriBidi implements the same standard and shares nothing with this, so
fifteen hundred lines built at random out of Hebrew, Arabic, Latin, digits, brackets, marks and
directional characters go through both and are compared level for level, and another eight hundred
are compared on where every character of them ends up. They agree on all of it. The comparison
found two real faults while it was being written — the backward searches of two rules end on what
a sequence sits after and not only on a character, and the formatting characters count as
whitespace for the rule that resets the end of a line — and a third when the drawing order was
compared: a mark drawn on a letter has to be put back after it when its run is turned round, which
is a rule that matters to anything that draws rather than merely reorders.

Hebrew is laid out with all of that behind it. A paragraph that says `w:bidi` begins at the right
margin; the words of a line are placed in the order they are drawn rather than the order they are
stored; a word that runs right to left is drawn from its own far end, with the marks kept on the
letters they belong to and the brackets facing the way the reader is going.

A run is stored in the order it is read all the way to the writer, and turned round there, as
glyphs. That is not a detail of where the reversal happens. Which letter joins to which, which mark
belongs to which letter, and which letters may be written as one shape are all questions about the
text, and a shaper handed a word backwards answers all three backwards. What can be turned round
safely is the glyphs, because each carries its own advance and its own offset and both are measured
from where that glyph itself begins. What has a direction of
its own keeps it: a number inside a Hebrew sentence reads as it was written, and so does a Latin
name. Hebrew inside an ordinary left-to-right paragraph is turned round too — which way a paragraph
runs says where its lines begin, not which way its characters go.

Word's export of the `hebrew` fixture says the lines begin where Word begins them, to within a
hundredth of a point, in paragraphs running both ways and with numbers, Latin, brackets and
punctuation inside them. What that comparison cannot say is what the lines *say*: Word writes a
line of Hebrew as many runs, and encodes some of them as pairs whose map back to characters gives
the two the other way round, so the text this reader recovers from Word's file is not quite the
text Word drew. The drawn order is checked instead against the algorithm's own answer, and that is
checked against another implementation of the standard.

A font is chosen per character rather than per run, because most fonts hold very few of the
characters there are: Arial Hebrew has no Latin letters at all, Times New Roman has no Japanese,
and a document written in Hebrew with an English name in it names one font for both. Asked for a
character its face has not got, the run is set in two — the rest of it where it was, and that
character in a face that can draw it. A converter that does not do this loses text the document
plainly holds, without failing and without saying so.

Which face is borrowed is a matter of taste rather than of correctness, and the taste is the
document's: the substitution chain a missing family already walks is walked again, and only where
none of those can draw it is everything else tried, in a fixed order so that two machines holding
the same fonts do not disagree. Where nothing at all can draw it the run keeps its own face; the
document is then short of a glyph, which is the truth, rather than short of a page.

That is also why `font-fallback` is compared to Word on where its text goes rather than on how wide
it is. The lines begin exactly where Word begins them, and every character a font could not draw is
on the page; the borrowed faces are not the same width as Word's, because Word's choice is its own
and not discoverable from the document.

A mark is drawn where the font says it goes. An accent, a Hebrew vowel point, an Arabic dot: none
of them has a place of its own and none can be drawn by advancing the pen. The font gives the mark
an anchor and the letter an anchor, and the two are brought together — a movement of the mark alone,
which the pen does not know about, so the letter after is set as though the mark were not there. A
mark drawn on a mark is placed against that mark rather than the letter, which is how a letter
carries two.

Where no movement along the line can express it — a point below a letter, an accent above one —
the text is raised for that glyph and put back down after, which is the only thing a PDF has to say
it with.

There is a reference implementation for this too, and it is used the same way. HarfBuzz shapes text
for nearly everything that draws it and shares nothing with this; asked for the same characters in
the same face it gives the same glyphs, the same advances and the same offsets, to the design unit,
for Latin accents, Hebrew points and pointed Arabic alike — including the marks written over a
shape that stands for four letters. Both are asked for the run in the order it is drawn, so the
numbers are compared as they are rather than translated first.

For the Indic and South-East Asian scripts it is used with one thing to be careful about. Several of
the faces macOS ships for them carry two complete descriptions of how to shape them: the OpenType
tables every other platform reads, and Apple's own state tables. HarfBuzz prefers Apple's wherever a
font has them, so asking it about one of those faces answers a question about a table this converter
does not implement and Word does not read either. The tests therefore ask it about a copy of the
face with those tables taken out. The two mostly agree; where they do not, the difference is worth
seeing rather than hiding — Khmer Sangam MN's OpenType tables write a consonant and its vowel as a
shape plus a blank, and its state machine deletes the blank instead.

Eighty words across sixty-two scripts are compared that way, glyph for glyph, advance for advance and
offset for offset, and agree on all of it. Word's exports of the three fixtures agree about the page:
every line begins exactly where Word begins it, and no line is more than a tenth of a point wider or
narrower. What those comparisons cannot say is what the lines say — a shaped syllable is one glyph
standing for several characters, and Word's file maps them back to whatever code the glyph happens to
sit at, so a line of Devanagari comes out of it as "नम#$".

For the universal engine the Word comparison covers one script rather than seventy, and that is
Word's limit rather than a choice: asked for Tibetan, Javanese or Cham on this machine, Word draws
the letters side by side without stacking or reordering anything. Sinhala it draws properly, and
agrees with this converter to four hundredths of a point on every line — including the line whose
vowel is written on both sides of its letter. For the rest, HarfBuzz is the only reference there is,
and it is the same reference Word's own engine was written against.

Arabic joins its letters, which makes the shape of a letter a fact about its neighbours rather than
about itself. Most letters have four — alone, opening a word, inside one, ending one — and a
handful join only on the right, which is why a word can end in the middle of itself: nothing after
alef or dal joins back to them. A mark written over a letter must not break the join, and the four
shapes are four glyphs of the same character, chosen through the font's `isol`, `init`, `medi` and
`fina` features. Some pairs may not be written as two at all: lam followed by alef is one shape,
and drawing them apart is a spelling mistake rather than an ugly line.

Two things about ligatures took measuring rather than guessing. A font says, per lookup, whether a
match may reach across the marks between the letters, and both answers are needed in the same
font and the same word: the lookup that writes lam, lam and heh as the one shape for the name of
God reaches across the vowels written over them, while the lookup that combines a shadda with the
vowel beside it is matching marks and must not skip them. And a mark on such a shape is placed by a
further table again — the ligature offers a place for each of the letters it stands for, and a
vowel over the second lam is not a vowel over the first. Which letter it belongs to is how many of
the shape's letters stand before it in the text.

What is drawn as one shape is still read as four characters. A ligature is written into the PDF's
map back to text as everything it stands for, so a word joined on the page can still be searched
for and copied out as the word.

Word's export of the `arabic` fixture agrees on all of it: every line begins within four tenths of
a point of Word's, and along the line each glyph stands where Word stands it to a hundredth. What
that comparison cannot say is what the lines say, for a nearer reason than Hebrew's — Word writes
Arabic as the presentation forms, one glyph to a run, so its file reads back as characters nobody
typed, and the name of God, which it draws as the single glyph the font holds for it, reads back
out of Word's own file as the letter J.

An Indic syllable is drawn neither in the order it is stored nor one shape to a letter. A vowel may
be written to the left of the consonant it is pronounced after though it is stored after it;
consonants with no vowel between them are written as one stacked shape; and an r at the head of a
cluster is written as a small mark at the end of it. A converter that walks the characters and looks
each one up does not draw an ugly line — it draws a different word.

None of that can be settled character by character, and the syllable is the unit throughout. It is
divided out of the run; the consonant the rest hangs from is found by asking the font what shapes it
has — the rules are written in terms of what a font can do, so there is no way round asking; every
part is given a place in the visual order and sorted into it; the font's rules for making conjuncts
are applied one at a time; and then what those rules managed to make decides where the vowel and the
r finally go. Sort, ask the font, sort again. Nine scripts follow it — Devanagari, Bengali,
Gurmukhi, Gujarati, Oriya, Tamil, Telugu, Kannada and Malayalam — differing in where a repha ends
up, how it is asked for, and which side of the base a below-base form may appear on.

The specification for those scripts was rewritten, and a font says which set of rules it was drawn
against by which of two names it files its script under. Both are implemented, because both are
shipped: this machine has Shree Devanagari 714 and Arial Unicode MS written to the older rules, and
Devanagari Sangam MN to the newer, and the two are shaped differently — under the older rules a
joining mark after the base is moved to the end of the syllable, the below-base feature is not
applied before the base, and what the font is asked about a pair of letters is asked of the pair in
company rather than standing alone.

Most of the writing systems descended from Brahmi are not given rules of their own at all. There are
too many of them, they are alike enough, and what differs between them is what the font already
describes — so one engine shapes all of them, working from what each character *is* rather than from
which script it belongs to. Something to build on; a vowel drawn above, below, before or after; a
consonant written under the one before it; a mark on a mark. Classify the characters, divide the run
into clusters on that basis, ask the font for its shapes in a fixed order, and move the two things
that are drawn away from where they are stored: an r at the head of a cluster, which is drawn as a
mark at its end, and a vowel written to the left of the letters it is pronounced after. That is the
whole of it, and it shapes some seventy scripts — Sinhala, Tibetan, Javanese, Balinese, Cham, Newa,
Chakma, Adlam, Egyptian hieroglyphs — none of which has a line of code to itself.

The classification is not a property in the database. It is worked out from five that are — what
part of a syllable a character is, which side of its consonant it is drawn, whether it joins like
Arabic, whether it is ignorable, and its general category — by rules Microsoft publishes, together
with the overrides Microsoft publishes for the characters the database has not caught up with.
`tools/make-use-tables.py` does that once and writes out the answer.

Three things about it were only found by comparing. A joiner does not divide a cluster: the grammar
is read over what is visible, or the very character written to ask for two letters to be joined
would separate them. A font that files its rules under no script in particular is not written to
this engine and must not be shaped by it — Noto Sans Tai Tham is such a font, and draws a left-side
vowel by moving the glyph, so reordering the characters first moves it twice. And a vowel written on
both sides of its consonant at once is stored as one character and cannot be drawn as one: it is
taken apart first, into the two halves the database says it is made of, and only the left half is
moved.

Khmer and Myanmar descend from the same writing and reorder for the same reasons, more plainly.
Khmer marks a stacked consonant with a character of its own rather than by the absence of a vowel,
and moves an r written under a consonant to the front of the whole cluster; Myanmar decides which
part of the syllable each character belongs to in one pass and sorts, with the medial r and the
left-side vowel going before the consonant they are written on. Thai and Lao reorder nothing at all:
they stack their vowels and tone marks above and below, and store every character in the order it is
drawn, so what they need is the marks put where the font says.

What each character is to a shaper, and where round its consonant it is drawn, is generated from the
Unicode character database by `tools/make-indic-tables.py` rather than typed. Not all of it can be:
where a matra ends up depends on the script as well as on the side it is written on, because the
sorted order is a visual one and the scripts stack their parts differently. Those per-script answers
are in the generator, from the OpenType script development specifications.

Text reaches the page as glyphs rather than as characters. Between the two stands a shaper: it
takes a run and a face and gives back the glyphs that draw it, each carrying its own advance and
the character it came from. Everything downstream reads that — a line's width is the sum of its
glyphs' advances, and what is written into the page is those same glyphs.

What the shaper does is decided by the writing in front of it: a character takes the glyph the
font's character map gives it; a letter of a script that joins takes the shape its neighbours call
for; letters the font says may not be written apart are written as one; a pair is drawn closer
together where the face says so; and a mark is moved onto what it belongs to. The point of it is
not cleverness but shape. A character is not a glyph — one may need
several, several may need one, and which glyph a character takes can depend on its neighbours —
and a pipeline that carries characters as far as the page cannot be told the difference. This one
carries glyphs, so it can be.

It also puts right something that was true before it: a line's width and the kerning written into
the page were worked out by two separate walks over the same text, one in the measurer and one in
the writer. Two walks that must agree are two walks that can disagree. There is one now.

Underneath it is a reader for the two tables a font describes its own shaping in. `GSUB` says what
glyphs a run becomes and `GPOS` says where they go, and the two are the same machine pointed at
different ends of the problem: both walk a run, both pass over what a lookup says it cannot see,
both have rules that match on what stands before and after, and in both a matched rule does not act
itself but names other lookups to run at places inside the match. So the walking, the skipping, the
matching and the nesting are written once, and only the acting is written twice — substitution of
one glyph for another, of one for several and of several for one; adjustment of a single glyph, of
a pair, and of a mark onto the letter, the ligature or the mark it belongs to.

Two of its parts are what make a complex script work at all. The first is `GDEF`, which says what
each glyph is: a rule about two letters that must still fire with a vowel written between them says
"ignore marks", and which glyphs are marks is a question only the font can answer. The second is
the script list, which had been passed over on the grounds that a lookup fires only on the glyphs
it covers and those are its own script's. That is true of nearly every feature and false of the one
whose whole purpose is to draw the same glyphs differently: `locl`, taken from every script at once,
draws Hindi in Marathi's letters. The script a run is in is now asked for by name.

Hinting is kept, because Word keeps it: its own exports carry `cvt`, `fpgm` and `prep` in every
subset they embed. It cannot be subset in any case — control values are reached by index and
function numbers are worked out as the instructions run, so which of them a glyph needs is not a
question that can be answered without running them. `ConversionOptions.DropFontHinting` takes all
three tables out along with the instructions inside each glyph, which more than halves the file at
no cost to the shapes; it is off because it is a departure from Word rather than a step towards
it.

CFF subsetting is the one thing here with no Word reference behind it: every PostScript-outline
face on this machine is for a script the converter cannot shape, so there is no document Word
could be asked to render. It is checked instead by fontTools, which reads the rebuilt font and
*draws* its glyphs — a subset that emptied a subroutine something still calls parses perfectly and
draws rubbish, so executing them is the check that matters. Install it with `python3 -m pip install
fonttools`; without it those tests report and skip, and `N8PDF_REQUIRE_FONTTOOLS=1` makes absence
a failure.

`ContentCoverageTests` asserts that every text run and every placeable image in a document reaches
the PDF, so an unimplemented construct fails loudly instead of vanishing from the output.

### Real documents

`Fixtures/Real/` holds documents Word wrote. `tools/make-real-fixtures.sh` takes the seed
documents defined in `Fixtures.RealSeeds`, opens each in Word and saves it straight back out,
which rewrites the package in Word's own terms — a `styles.xml` carrying several hundred latent
styles, `settings.xml`, its theme, `docProps`. None of that can be produced by hand, and it is
what these fixtures exist to test. They go through the same per-line comparison as everything
else.

`tools/make-real-fixtures.sh` --list shows what would be generated. Add to `RealSeeds` to cover
more; third-party templates are best avoided, since their licence terms would come with them.
