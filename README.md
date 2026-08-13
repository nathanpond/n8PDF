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
  Fonts/         SFNT parsing, metrics, font resolution
  Images/        PNG decoding and JPEG header reading
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
   within 0.29pt — close to Word's own vertical quantum of 1/300 inch. `Fidelity_report` writes
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
one says nothing), multiple text columns (evenly divided or individually stated, column breaks, and
the rule down the gap where the document asks for one), footnotes and endnotes (numbered in reference order, arabic for footnotes and roman
for endnotes unless the document says otherwise; a footnote goes to the foot of the page its
reference lands on and takes that space out of the body above it, an endnote carries on after the
body like ordinary content, and both are ruled off by the separator), hyperlinks (external addresses as clickable regions, internal links to bookmarks
anywhere in the document, with the regions placed and padded the way Word places them), headers
and footers (per page, with separate first-page and even-page variants, and
fields evaluated), fields — page numbers (PAGE, NUMPAGES, SECTION,
SECTIONPAGES, PAGEREF), what a document says about itself (AUTHOR, TITLE, SUBJECT, KEYWORDS,
COMMENTS, LASTSAVEDBY, CREATEDATE, SAVEDATE, PRINTDATE, DOCPROPERTY, FILENAME), counters (SEQ, with
its restart, repeat and format switches), references to a bookmark's text (REF), running heads
(STYLEREF), literal text
(QUOTE), and the clock (DATE, TIME) — each spelled the way its `\*` switch asks, in arabic, roman,
letters, ordinals, words, hex or dollars, and cased by Upper, Lower, FirstCap or Caps, with a
`\@` picture deciding how a date reads, lists and numbering (decimal, letters, roman and bullets, nested levels with
independent counters and multi-level templates, hanging indents), images, inline and floating (PNG — interlaced or not — GIF, BMP, TIFF and EMF all read from scratch, JPEG passed through untouched,
transparency via a soft mask; square, top-and-bottom and no-wrap text flow around anchored
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
Both are read. The two are alternatives rather than halves, so a file carrying both is drawn from
the old records: they are what every reader of a metafile has always understood, and they are the
half whose reading can be checked against another implementation. The newer records are read only
where they are all a file has, which is a file that would otherwise draw nothing at all.

That order is deliberate, and the reason is worth stating plainly. Word for Mac renders classic
metafile records and draws nothing whatever for EMF+ ones — its export of a file holding only
those is a blank space — so there is no second implementation on this machine to read EMF+ against.
Everything else here is measured against Word; this one part is not, and is tested only against a
writer built from the same reading of the specification, which is precisely the kind of agreement
this project distrusts everywhere else. Preferring the old records keeps that uncertainty off the
files documents are actually made of: a chart pasted out of a spreadsheet carries both, and is
drawn the way Word draws it, to within a point.

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

A picture divided into several JPEGs is reported rather than half-read. The strips are separate
files and joining them would mean decoding them, which is the one thing this set out not to do.
The older way is also the only thing here with no second opinion behind it: `sips` will not read a
file written that way at all, so what is tested is that the JPEG comes back, and no more than that.

Not yet: nothing of what a document usually holds. What is left is a TIFF whose JPEG is divided
into several strips, which would have to be decoded to be joined., splitting a note across pages, restarting note numbering per page or per section, notes
positioned beneath the text rather than at the foot of the page, endnotes gathered at the end of
each section rather than of the document, RTL and complex
scripts, balancing the columns of a section's last page, footnotes under the column that refers to
them rather than under the whole measure, and page numbering restarted per section.

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
