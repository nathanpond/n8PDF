# n8PDF

Converts `.docx` to PDF. Written from scratch: no third-party DOCX or PDF library, no headless
Word, LibreOffice, browser engine, sidecar service or container. A consumer adds one assembly
reference and calls one method.

```csharp
Converter.ConvertFile("report.docx", "report.pdf");
```

## The API

Six types, and that is the whole of what a version promises:

| | |
|---|---|
| `Converter` | `Convert(byte[])`, `Convert(Stream, Stream)`, `ConvertFile(string, string)` |
| `ConversionOptions` | fonts, layout, title, file name, dates, a mail-merge record, and whether to fill in Word's built-in style defaults |
| `LayoutOptions` | kerning, and the default tab stop |
| `FontLibrary` | registering fonts, and whether to discover the platform's |
| `MailMergeRecord` | the fields a merge field asks for |
| `FontFormatException` | what registering something that is not a font throws |

Everything else — the OPC reader, the document model, the style cascade, the font engine, the
layout engine, the PDF writer — is internal. All of it used to be public, which would have frozen
174 types at the first published version: the shape of a positioned line, the name of a table's
border edge, every enum the parser reads. `PublicApiTests` writes the surface out in full and
fails on anything that grows it, so adding to the promise is a deliberate act with a diff to show
for it.

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
dotnet pack src/n8PDF -c Release   # the package, with its symbols and its documentation
```

Converted fixtures are written to `artifacts/test-output/` for eyeballing. That directory is
git-ignored.

**A warning is an error.** `Directory.Build.props` sets `TreatWarningsAsErrors` for every project,
so the build that introduces one is the build that fails, rather than the next push to CI. NuGet's
audit warnings are the one exception and stay warnings: they report what is known about a package
today rather than anything about this code, and would fail a build of an old commit for an advisory
published after it was written. To get past one while working, and only while working:

```bash
dotnet build -p:TreatWarningsAsErrors=false
```

### Cutting a release

```bash
git tag v1.0.1 && git push origin v1.0.1
```

That is the whole of it. `release.yml` builds at the version the tag names, runs the same tests
every push runs, packs the library with its symbols, publishes to NuGet and writes a GitHub release
with both files attached. The tag is the only place a version is written down: the number in
`n8PDF.csproj` is what a local build gets, and a release states its own on the command line, so a
package cannot disagree with the tag it was cut at. `LibraryInvariantTests` checks that it does,
because a workflow that forgot to would publish the wrong number quietly.

Publishing needs a `NUGET_API_KEY` secret. Without one everything else still happens and the log
says the push was skipped, so a release can be cut and inspected before anything is sent anywhere.
`workflow_dispatch` takes a version and is a rehearsal: it builds and packs and publishes nothing.

A release is built on a hosted runner, so it is tested to exactly the standard `ci.yml` sets — the
74 documents set in Word's own faces are compared by `full.yml`, on a machine that has Word, and a
release does not wait for them.

### What runs where

The suite compares against PDFs Word exported, and those are set in the faces Word brings with it —
Calibri, Cambria, and the Japanese and Chinese faces it carries rather than takes from the system.
So it matters which machine the tests run on, and there are two answers:

| | `ci.yml`, every push | `full.yml`, by hand |
|---|---|---|
| Runner | `macos-15`, hosted | self-hosted, labelled `word` |
| Documents compared against Word | 69 of 143 | all 143 |
| Also | `qpdf`, fontTools, FriBidi, `dotnet pack` | `qpdf`, fontTools, FriBidi |

The 74 documents written in Word's faces cannot be rendered as Word rendered them on a machine
without Word, so on a hosted runner they are left alone. There is no self-hosted runner registered
— that would mean a public repository's workflows running on a personal machine — so those 74 are
checked by running the suite on a machine that has Word, where `dotnet test` covers all 143. Which 74 is measured rather than declared
— a fixture is on the list when laying it out with those faces and without them gives different
answers — and `OfficeFontTests` keeps the list honest at both ends: it regenerates and checks the
list wherever the faces are present, and where they are absent it prints how much was skipped
rather than letting the gap pass unremarked. `N8PDF_REQUIRE_OFFICE_FONTS=1` turns their absence
into a failure, which is what the full run sets, so a runner that has quietly lost Word fails
instead of passing on two thirds of the comparison.

The faces the hosted runner *does* provide are macOS's own, at fixed paths. A runner image shipping
a different version of one of them would move glyphs by fractions of a point and fail the goldens
for a reason unconnected to any change, so `ci.yml` prints their fingerprints and the OS version on
every run — the first thing to compare when a golden fails only in CI.

### What a conversion costs

A page of text converts in about 1.4ms on the machine this was measured on. Finding the fonts is
the one thing that costs more than the document does: the platform's font directories here hold 651
files and 1.3GB, and every one of them has to be read to know what face it holds. That is done once
for the process and shared — the index is every face's name, style and file, and comes to 1.6MB —
and a face reads its own file only when a document asks for that face. So the first conversion in a
process pays about 600ms for the scan and every one after it pays nothing.

Before that, a conversion that said nothing about fonts read all 1.3GB, held 1.5GB while it did,
threw it away, and did the whole thing again for the next document: 450ms a page, whatever the page
held.

`FontLibraryCacheTests` holds that arrangement to the number, on a library of its own rather than on
this machine's font collection: three files indexed into a directory it writes itself, none of them
read; one family resolved, one of them read. It used to weigh the process's memory before and after
instead, which made every other test allocating at the same moment part of the answer — a test that
failed for reasons unconnected to fonts, and passed when run alone.

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
   Across the fixtures, line start positions match Word exactly, twenty-two documents match its
   baselines exactly as well, and everything else agrees to within 0.72pt — almost always by a
   single step of the 1/300 inch grid Word writes on, where a rounding falls the other way.
   `Fidelity_report` writes the full per-line table to `artifacts/test-output/fidelity-report.txt`.

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

## Reading a file someone else wrote

A `.docx` is a ZIP, which is to say it describes its own size, and a file written to be hostile
describes it wrongly. Two attacks are cheap to write and were both open here:

- **A part that decompresses without bound.** Zeros compress about a thousand to one, so a hundred
  kilobytes becomes a hundred megabytes and a hundred megabytes becomes a hundred gigabytes;
  nothing in the format objects. `PackageLimits` bounds it: 128MB for one part, 512MB across a
  package, 4096 parts. Set them on `ConversionOptions.Limits` for a document that genuinely needs
  more, and catch `PackageTooLargeException` to know that is what happened.
- **A part whose XML declares entities.** Ten entities, each ten of the one below, expand a
  kilobyte into a gigabyte — the same attack a layer up, and one that no amount of counting
  compressed bytes will catch. `XDocument.Load` parses a document type definition and expands them
  without limit, which is what it was doing here until `PackageLimitTests` fired a billion laughs
  at it. Parts are now read through a reader that prohibits definitions outright, which costs
  nothing legitimate: the Open Packaging Conventions forbid a DTD in a part.

- **A picture that declares itself enormous.** An image says its own size in its header, and every
  decoder here allocates from what it says before reading a byte of the picture: a PNG of 57 bytes
  can call itself fifty thousand pixels square and ask for seven and a half gigabytes. The part
  limits cannot see it coming, because the file really is small. `MaximumImagePixels` bounds the
  area — fifty million, which is a 600dpi A4 scan with room to spare — and it is counted in long
  arithmetic, since 70,000 squared does not fit in the int the pixels would have been allocated
  with. A picture past the limit is left out the way any unreadable picture is left out: twenty
  bytes of nonsense beginning `GIF89a` declare themselves 24,864 by 25,710, and a document holding
  such a thing should lose the picture rather than the conversion.

The size limits are counted against what comes out of the decompressor rather than what the header
claims. As it turns out a lying header cannot smuggle anything past — .NET stops the decompressor
at the declared size, so a part claiming to be small *becomes* small — but that is a property of
this framework rather than of the format, and the counting is what makes it not matter. The tests
build each attack rather than describing it, because a limit nobody has fired a shot at is a
comment rather than a defence.

The defaults are set for documents rather than for the fixtures here, the largest of which is 105KB
in one part across 6 parts — a hundredth of the smallest limit, which `PackageLimitTests` asserts,
so a fixture that ever approaches one says so.

Fonts are not in this list, and the reason is worth stating: **no font ever comes from a document**.
Word can embed one, and the relationship type for the font table is declared here, but nothing reads
it — faces come from the system directories or from what the caller registers, both of which the
caller chose. What was worth fixing was smaller: the table directory of an SFNT checked that a
table began inside the file and not that it ended there, so a malformed face could declare a table
of two gigabytes and be believed by anything that read one by its length. Lengths are clamped to
what is actually there. When embedded fonts are implemented, a byte limit belongs beside the others.

## Matching Word

Several layout rules here were derived by measuring Word's own output rather than from the spec,
each with a fixture built so that only one candidate model survives:

- Adjacent paragraph spacing **collapses to the larger** of space-after and space-before, it does
  not sum. Across a page break the collapse still applies, but the previous paragraph's
  space-after is absorbed by the page it ended on.
- A line-spacing multiple's **extra leading goes below** the baseline, so the first line of a
  paragraph sits at its natural ascent whatever the multiple.
- The font's **line gap belongs above the ascent**, not below the descent.
- Every baseline is written on a **grid of 1/300 inch**, while the line heights that stack the
  boxes stay exact — see [The grid every baseline stands on](#the-grid-every-baseline-stands-on).
- An **East Asian face gets three tenths of an em of extra leading**, whatever its own metrics
  say. Word gives MS Mincho, MS Gothic, KaiTi and MingLiU at 12pt a line of exactly 15.6pt,
  although those four faces ask for 1.0, 1.0, 1.14 and 1.20 em between them — and although Core
  Text reads those four values back from the files just as this reader does. It does the same for
  a line of Latin letters set in one of them, so the height belongs to the face rather than to the
  script written in it, and a face is taken to be East Asian when `OS/2` declares one of the five
  East Asian code pages. Measured by `east-asian-line-box-probe`; sixteen hundredths of the
  leading go above the ascent and the rest below the descent, which is as close as Word's own
  vertical quantum allows.
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

The shifts themselves are a share of the type size — a third up, a twelfth down — and that share is
fitted rather than derived, because **Word's is not a share of anything that can be read out of a
document or a font**. `superscript-shift-probe` puts the question to Word five sizes wide and three
faces deep, and eleven faces were measured while it was written:

- It is not a share of the size. For every face the sizes disagree: Times New Roman wants at least
  0.375 of the size at eight point and at most 0.350 at twelve, and no one number is both.
- It is not a share of anything the face declares — not its ascent, descent, cap height, x-height,
  nor the superscript offset in its own `OS/2` table. Calibri and Candara agree on every vertical
  metric to four decimal places, and Word raises a superscript 0.3325 of the size in one and 0.4525
  in the other. No linear combination of those metrics comes within twenty times the precision of
  the measurement.

So one number it stays, fitted to Times New Roman, which is what the fixtures are written in: every
size from eight point to ninety-six lands within a step of Word's grid, Arial within three, Calibri
within ten — all of Calibri's beyond forty-eight point, which is not a size anything is
superscripted at. `LineBoxTests` states the gap case by case rather than hiding it behind one
tolerance, because the gaps are the finding.

### The grid every baseline stands on

Word writes every baseline on a grid of one three-hundredth of an inch — 0.24 points, the same grid
it rounds a type size to, which is why a 15pt run comes out of one of its PDFs as 15.12. Two probes
say what is rounded and what is not:

- **The height of a line is exact.** `line-grid-probe` fills nine pages with forty single-spaced
  lines each and measures the distance from the first baseline to the fortieth. Were the height
  rounded, every gap on a page would be the same whole number of steps; instead they mix 2.16 with
  2.4, and the span comes to within a quarter point of thirty-nine exact heights. The height is
  worked out at the size the run **states**, not the size Word draws at: at two point, which Word
  draws at 1.92, a page would span 86.1 points if the drawn size decided it and 89.69 if the stated
  size did, and Word spans 89.52. Eleven point, drawn at 11.04, has the rounding going the other
  way and agrees.
- **Inside a line, the descent is rounded and the ascent takes what is left.** `line-ascent-probe`
  gives each of its seventy-four pages a first paragraph of one letter, so the baseline is the top
  margin plus that line's ascent and nothing else. Over the seventy-four — four faces, and Times
  New Roman and Arial at every half point from six to twenty — rounding the descent accounts for
  seventy-one. Rounding the ascent instead accounts for sixty-two, and no rounding of a height or a
  descent through any intermediate unit does better. The three misses are a single step each, and
  each is a descent that lands just above a half step.

Neither rounding accumulates: the next line starts from the exact height. Six of the nine forty-line
pages come out as Word's line for line, and 292 of the 360 baselines overall. Across the whole
fixture set, twenty-four documents of the 140 now agree with Word's page **exactly**, where before
the grid none did, and the average of each document's worst baseline is 0.298pt against the 0.387
it stood at then — what is left is almost everywhere a single step of the grid, where a rounding
falls the other way.

Anything moved after it is written — a line pushed down by a float, a page centred between its
margins, the contents of a table cell or a footnote, a raised or lowered run — is moved onto the
grid too, so nothing this engine writes along a line stands off it. What a line *draws* is not:
the rule above a carried footnote is where the arithmetic puts it, within a hundredth of a point
of Word's, and rounding it would take it a twentieth of a point away. `LineGridTests` holds every
fixture to the grid, and the two probes to Word's own page.

### A multiple of a line a picture has made taller

`w:lineRule="auto"` asks for a multiple of the line — 1.08 of it in Word's own Normal, which every
document Word writes inherits. Put a picture on such a line and the two readings of "the line" part
company: a multiple of the whole box, picture and all, or a multiple of the line the *text* would
have made with the picture set on top of it.

It is the second. `image-line-probe` puts pictures of six, twelve, twenty-four and ninety-six points
on a line of twelve point Times at multiples of one, 1.08, one and a half and two, and Word leaves
exactly the same room under the picture as under the text alone in all sixteen: a ninety-six point
picture on a 1.08 line makes a line 99.6 points tall, not the 106.8 that multiplying the whole box
gives. Two of the four heights are shorter than the line the text makes on its own, so the plain
rule is measured in the same document as the rule that replaces it.

Every fixture written by hand here sets its spacing to a single line, where a multiple of one makes
the two readings identical. It took a document Word wrote — `brochure`, whose picture paragraph
inherits Word's 1.08 — to tell them apart, and the error it found was 6.8 points.

### What a highlight and a background cover

Both are filled rectangles behind text, and both are measured rather than assumed — `highlight-probe`
and `paragraph-shading-probe`, compared against Word's own ink rather than its text.

A **highlight** is as wide as the run and as tall as the **line**, both edges put on the grid. The
line, not the run: a twelve point run beside a thirty-six point one is highlighted the full
forty-one points the two share. Its ends are where the run's are — a space inside the line is
covered, a space dropped at a line break is not, and a plain space between two highlighted words
leaves two boxes rather than one long one. A highlighted paragraph mark paints nothing at all. The
sixteen names are the sixteen colours of an old display adapter, each channel off, half on at 128,
or full; they are listed in `HighlightColors` and read off Word's page, not off a table.

A **shaded paragraph** is one rectangle per line, each covering its line box exactly, so the fills
of a paragraph — and of two shaded paragraphs in a row — tile without a seam. It reaches a fiftieth
of an inch past the paragraph's own edges on both sides: text from 72 to 540 is filled from 70.56 to
541.44. The paragraph's indents move it and the *first line's* indent does not, and centring the text
does not either — it is the paragraph that is shaded, not the line. A pattern is a straight blend of
the two colours it is given: `pct25` of red on yellow is #FFBF00, `solid` is the pattern colour
alone, and Word works the blend in whole 255ths with a half going down. The named textures —
`horzStripe` and its kind — are hatchings rather than blends and take the fill alone.

What a line paints now comes off the page with the line. A line at the foot of a page can be taken
off and laid again on the next — widow control alone moves two of them — and its fill has to go
with it or the page it left keeps a rectangle under empty space. The same bookkeeping carries a bar
tab's rule and a form field's box, neither of which was taken off before this.

**A run's own background** — a `w:shd` inside a `w:rPr` — turns out to be the highlight's
rectangle exactly. `run-shading-probe` mirrors `highlight-probe` page for page, and Word draws the
two identically down to the thousandth of a point: the run's width by the line's height, the same
ends, the same nothing behind a shaded paragraph mark. Two things are its own, and both are
measured there: it is drawn over the paragraph's background and takes none of the paragraph's
fiftieth of an inch of reach, and a run asking for a background *and* a highlight gets the
highlight alone — Word's page has one rectangle for such a run, not two.

**A cell** blends the same way, which `cell-shading-probe` measures rather than assumes: six shares
of red over yellow come out of Word as the same six colours a paragraph gives them, `pct12` among
them — that name means an eighth, not a twelfth, and Word's #FFDF00 says so. A cell differs from a
paragraph in one thing only, and it is worth knowing: **an automatic fill is a white surface in a
cell**. A cell asking for a clear pattern over `fill="auto"` is painted white; a paragraph asking
for exactly the same thing is not painted at all. Over an automatic fill a pattern blends with
white — half red comes out #FF7F7F in a cell, in a paragraph and in a run alike.

Two things the same probe settles by showing nothing at all. A `w:shd` on the **table** reaches no
cell of it: Word's export has nothing behind the cell that says nothing of its own, so neither has
this. And a **texture** — `horzStripe` and its kind — is a real hatch, which Word writes into its
PDF as a tiling pattern; a flat rectangle of the fill is drawn here instead, which is the one place
in all of this that is an approximation rather than a match.

### How far inside its own edges a shape sets its text

A text box holds its text clear of its edges by two things added together: the inset the shape
declares — a tenth of an inch at the sides and half of that above and below, where it declares
none — and **half its outline**, the half that falls inside the shape. `shape-inset-probe` is what
says so: its third page sets a six point outline against no inset at all, and the text there begins
3.12pt inside the shape rather than 6pt or nothing.

The outline itself straddles the edge. Word's export fills the whole extent and then strokes the
same rectangle, insetting neither, which is what a PDF does with a stroked path anyway — so the
frame here is drawn the same way and the two agree to a hundredth of a point.

### What an outline does to the line a shape sits on

An old-style (VML) shape with an outline is drawn a little down and to the right of its own box,
and the line it sits on is taller than the shape is. Both follow from one number — the outline's
weight rounded to whole points, and never less than one:

| | |
|---|---|
| the shape is drawn | the even number of points at or below it — 2⌊n/2⌋ |
| the line is as tall as | ⌈the shape's height⌉ + n − 1 |

So a quarter-point outline and a one-point outline behave alike, a shape 13½ points tall with a
4½pt outline sits on a line of eighteen points, and an 8pt outline is drawn eight points in.

`vml-stroke-stack-probe` is what says so, and it is built to be read finely: a single line can only
be measured to within a step of Word's grid, 0.24pt, which is wider than the differences here, so
each page stacks thirty shapes and divides that by thirty. Fourteen weights and five heights fit
both rules exactly. An earlier reading of a coarser probe had the offset as the even number of
points *reaching past* the weight, which agrees on every weight that probe held and is wrong at 1¼
and 3¼ — the two this one added.

The ceiling belongs to the outline rather than to the shape: the same shape with no outline sits on
a line of exactly its own height, 13.5 for 13.5, and so does an inline picture
(`inline-picture-line-probe`). Any outline, however fine, rounds it up to the whole point.

Two smaller things fell out of the same measurement. A line holding nothing but a picture is never
shorter than the paragraph's own mark — Word gives a 4½pt shape under an eleven point mark the
mark's 13.43pt line and stands the shape at the foot of it — and a picture rests on its line's
*exact* baseline rather than the rounded one its text is written at, which is why Word's shapes
land at precisely the margin plus their offset however the rounding of the line falls.

### Where a chart puts things

A chart is the one thing a document describes only as data: series, axes and formatting, with no
drawing of it anywhere, not even the cache a diagram carries. So every number below was measured
from Word's export rather than read anywhere.

- The **plot area** goes exactly where a chart states it, to the last decimal place, when it states
  it as fractions of the frame.
- A **bar's width** falls out of the gap between them, which is a percentage of the bar itself: one
  series at a gap of 150 makes a category two and a half bars wide, so four categories across 252pt
  give 63pt each and a bar of 25.2pt — which is what Word draws, to the quantum. Two series at a
  gap of 100 and an overlap of −27 share their category and then stand apart: 117 ÷ 3.27 = 35.78,
  against Word's 35.76.
- A **label ranged against its axis** ends a little under one em short of it — 9.278pt at ten point
  type and 18.547pt at twenty, so proportional with nothing fixed about it — and is set with the box
  from its ascenders to its descenders centred on its mark, which puts the baseline a quarter of the
  type size below it. The face's typographic ascent and descent are what that quarter comes from, not
  the ones a line is measured by: Calibri says 1536 and 512 of its 2048 for the first pair and 1950
  and 550 for the second, and only the first puts the label where Word puts it.
- A **label written under its axis** sits with its baseline 1.584 times its type size below it, at
  ten point and at twenty alike, and each hundred of `lblOffset` moves it a further 0.312 of that
  size.
- Whether the axis carries **marks** makes no difference to either: a chart drawn with them and one
  drawn without put their labels in exactly the same places. A mark itself reaches 40301 EMU
  outside its axis, which is 3.1733pt — the same on both axes of `chart-axis-probe` and on the
  lying axis of `chart-bar-stacked`, and outwards in every case.
- A chart's own **frame** is a white rectangle with ten point corners outlined in #898989 at half a
  point, which is what Word draws where the chart says nothing about its border.

Every line of all seven chart fixtures lands within 0.012pt of Word's across the page and 0.32pt
down it, and the ink of a page agrees with Word's on better than 99.4% of it.

**Where the plotting goes when the chart does not say** — which is what every chart in a real
document leaves to be worked out — is measured too, by `chart-layout-probe`. A chart carrying no
labels at all puts its plotting **eleven points inside its frame on every side**, whatever size the
frame is; a chart carrying them begins its labels **6.5pt inside the frame** and gives the plotting
what is left:

| side | what it makes room for |
|---|---|
| left | the widest label ranged against the axis there, plus the gap it keeps from the axis |
| foot | the line written under the axis: 1.584 type sizes below it, and its descender below that |
| top | half a label's height, so the topmost number does not overrun the frame |
| right | nothing — a category label wider than its bars is left to overrun, as Word leaves it |

A chart lying on its side swaps the two labelled edges over — the words go up the side and the
numbers along the foot — and swaps what the other two do with them. Its top takes the bare eleven
points, since nothing reaches above the plot; its right takes eleven **plus half the widest number**,
because the last number along the foot is centred on the plot's own corner and half of it hangs
past. Word gives the second page of `chart-bar-stacked` 39.34pt on the left, 11 above, 25.05 below
and 16.07 on the right, and each of the four falls out of the rules above to a fortieth of a point.

The heights in that table are the face as *Windows* reads it, where the baselines are the face as it
reads *itself*: for Calibri, 1950 and 550 of its 2048 against 1536 and 512. Two questions, two
answers, and using the second for both put the plot area two points out at twenty point type.

Across the six charts of the probe — varying the frame size, the width of the numbers, the type
size, the length of the category labels, and whether there are labels at all — the plot area lands
within a quarter of a point of Word's, and the chart with no labels lands exactly.

**What the axis runs between when the chart does not say** is measured by `chart-scale-probe` and
`chart-bar-scale-probe`, twenty-six charts between them, varying the numbers, how long the axis is,
which way it runs and what size its labels are set at. One rule accounts for every one:

> the step is the **smallest** of one, two or five times a power of ten for which the axis — running
> from the largest step at or below the least value to the smallest step **strictly** above the
> greatest and a twentieth — carries no more marks than the axis has room to write

So up a 126pt side at ten point: 7 runs to 8 in ones, 9.5 to 10 in ones, 10 to 12 in twos, 47 to 50
in fives, 105 to 120 in twenties, 1000 to 1200 in two hundreds, and 0.4 to 0.45 in twentieths. The
strictness is what puts a chart of exactly 100 at 120 rather than leaving its tallest bar against
the frame, and the twentieth is what puts a chart of 58 at 70 rather than 60 — `chart-legend-key-probe`
holds one of each, bars and an area, and Word stops both at 70. The foot is nought wherever nothing is negative, whatever the smallest value — a chart of
30 and 55 still starts at nought — and where something is negative the foot steps below it the same
way the top steps above: −20 and 60 give an axis from −30 to 70 in tens.

How much room a mark needs is the part that only the second probe could reach, since every chart in
the first is upright and 126pt tall. A label wants along its axis:

| axis | room per label |
|---|---|
| standing up | a tenth over its own type size — anything from 1.05 to 1.145 fits the measurements |
| lying down | three times it — anything from 3.02 to 3.15 fits |

and the axis takes as many steps as leaves room for one more label than it has steps, since a mark
is written at both ends as well as between — and never more than ten, however long the axis is.
The ten only shows itself on an axis long enough for eleven labels: `chart-area-scatter`'s
fifteenth page holds a chart of exactly one over a plot that would take eleven, and Word runs it to
1.2 in fifths rather than to 1.1 in tenths. That is why the same 47 that runs to 50 in fives up a
side runs to 60 in twenties along a foot of the same length, and why setting the labels in twenty
point rather than ten thirds the number of steps either way. Two of the fourteen pages exist only
to part the readings: a chart of millions divides its foot exactly as a chart of tens does, so the
room has nothing to do with how wide the numbers are; and the same chart set in twenty point divides
it into a third as many steps, so the room does grow with the type. All twenty-six come out label
for label as Word's.

The negative case also showed up something the positive ones cannot: the words under the bars go
beside the **nought** rather than at the foot of the plot, because that is where the two axes cross.
A chart whose bars all stand up puts the two in the same place; one with a bar hanging down does
not, and what hangs down hangs past its own label.

### Bars that lie down, and bars piled on each other

A bar chart is a column chart turned on its side, and almost nothing about it is stated: `barDir` is
the whole of what the format says, and everything that follows had to be measured from
`chart-bar-stacked`.

- The **categories run upwards**: the first is at the foot of the plot, not the top, which is the
  opposite of the left-to-right an upright chart uses.
- Within one category the **series run upwards too**, so of two clustered bars the second is the
  upper. Both reversals together are what makes a bar chart read the same way round as the column
  chart it is a turn of.
- The **value axis stays at the edge** it is drawn on — the foot — however far the categories move.
  The category axis crosses it at the nought, and its labels follow it: the last page of the fixture
  puts them 9.28pt to the left of the nought, three fifths of the way across the plot, and not
  beside the plot's own edge.
- **Gridlines** run the other way, up the plot rather than across it, and the one at the crossing is
  left out because the axis itself is drawn there. A chart with nothing negative leaves out the one
  at the foot of the scale for the same reason; one with something negative draws it and leaves out
  the nought.
- A **mark** on a lying category axis reaches to the left of it, and one on the value axis below —
  outwards in both cases, the same 3.1733pt.

**Stacking** is an overlap of a hundred and nothing else, so far as the width of a bar goes: two
stacked series across a 78pt category give a bar of 78 ÷ 2.5 = 31.2pt, which is what one clustered
series would have given, and Word draws exactly that. What changes is where each bar starts — at
where the last one ended rather than at the axis, with what rises above nought and what hangs below
it piled apart — and what the axis has to reach, which is what a category comes to rather than what
any one bar holds. Word runs the fixture's stacked page to 70 where the same numbers unstacked
would have stopped at 50.

Stacked **to the whole**, each bar is first taken as its share of its own category, and the axis
runs to exactly one — the single place the top of an axis is not a step above what it holds. The
labels are written by the axis's own number format, of which what is read here is what a chart
carries: how many decimal places to keep, whether to group the thousands, and whether the number is
a per cent.

One thing the negative page turned up that has nothing to do with lying down: a bar hanging below
nought is drawn **the other way about** — white, and outlined in black at three quarters of a point
— which is what `invertIfNegative` asks for, and asks for by default. Word draws it so even though
the series it belongs to asks for no outline at all.

All eight pages of the fixture agree with Word rectangle for rectangle within a quarter of a point,
which is the 1/300in Word rounds every edge it draws to.

### How a line curves, and where a pie sits

A line chart **curves through its points unless the series says not to** — the format's default is
smooth, which is not the obvious one, and Word writes `c:smooth` on every line chart it makes so
its own files never depend on it. The curve is a Catmull-Rom spline: each point is passed at a
slope of half the distance between its neighbours, the ends take the slope of their own segment,
and the Bézier controls sit a third of the way along those slopes. Every control point of the
fixture's curve comes out of that to the EMU — Word writes 266700 where the rule gives 266690.

The points themselves sit at the middles of the categories, where a bar chart's bars stand.

A pie is centred in its plot area and reaches the nearer pair of its edges: Word's export puts the
fixture's pie at the middle of a plot 216 by 172.8 with a radius of 86.4, which is half the shorter
side. Its slices begin at the top and run clockwise. A pie carries no axes, so a pie left to place
itself gets the bare eleven points on every side — the same margin a chart with no labels gets, and
Word draws it at exactly that.

Inside the frame, all four of `chart-line-pie` agree with Word on better than 99.9% of their ink.
The one thing left outside it is that Word clips a chart to its own frame, so the outer half of the
border it draws is cut away; nothing here clips, and that border straddles the edge instead. It
comes to a quarter of a point of halo round the outside of a chart.

### An area, and a chart of pairs

An area chart is a line chart with the space under it coloured in, and a scatter is the one kind
that has no categories at all. Both were measured from `chart-area-scatter`, nineteen pages of it.

- An area's corners sit **at the marks rather than between them**, so the first and last touch the
  ends of the plot: Word's four corners land at 162, 240, 318 and 396 across a plot running 162 to
  396. That is what `crossBetween="midCat"` asks for, and Word writes it on every area chart it
  makes; a line chart says `between` instead and keeps its points at the middles of the categories.
- The **category labels follow the points**, so the outermost two are centred on the plot's own
  corners and half of each hangs past. A chart left to place itself makes room for that half — the
  fifth page gives its right edge eleven points plus half of "Four" — and where a label is too wide
  for its category it **wraps**, which grows the foot by a line and the side by half of the widest
  line it came to. The nineteenth page, whose first category is nearly six times as wide as any of
  its numbers, lands within three hundredths of a point of Word both ways.
- Stacked, each area is a **band** rather than a shape hiding the ones behind: it runs along its own
  points and back along the series below it. Unstacked, they are drawn one over another in the order
  the chart lists them, opaquely — Word writes no transparency of its own, so a taller area behind a
  shorter one is simply hidden by it.
- A scatter is scaled **both ways**, and the foot is divided by the rule a lying axis uses rather
  than an upright one: three times the type size per label against a tenth over it. Its eighteenth
  page, left to Word both to place and to scale, divides a 320pt foot into eight and a 180pt side
  into six.

A **marker** is the one thing whose placing is Word's rounding rather than its arithmetic. A marker
of size s is drawn in a box of s rounded to the three-hundredth of an inch, whose corner is the
point less half that box rounded *down* to the same grid, and the shape itself sits half a
three-hundredth inside the box. So a marker of seven comes out 6.72 across and up to a third of a
point left of and above the point it belongs to. Four sizes and two shapes come out of that rule
exactly; on one point in four Word breaks the tie on the grid the other way, and what decides it is
not measurable from what is here.

A series that says nothing about its markers still gets them — in its own colour, outlined in it at
half a point, and seven points across where the series draws a line or six where it does not. Word
runs through **diamond, square, triangle and cross** for the first four such series, which is
measured; what it does with a fifth is Excel's old order and is not.

### What goes round the plotting

A title, a legend and the numbers written at the points are the three things a chart carries that
are not the plotting itself, and the first two take their room out of it. Measured from
`chart-title-legend-label`, nineteen pages.

**A title takes nine points and a line of its own type**, whatever that line comes to: at ten point
it takes 20.076 off the top of the plot, at eighteen 28.931, at twenty 31.146 and at thirty 42.216,
and two lines take two lines' worth. The line in question is the face as *Windows* reads it — for
Times New Roman 1.1074 ems against the 1.1499 a line of body text is set by — which is the same
split between the two pairs of metrics that runs through the rest of a chart. An axis title takes
the same nine points and a line off the side it names.

Where each then goes is measured once apiece: a chart's own title has its first baseline 7.43pt
below the top of the frame and is centred on the **frame**, while an axis title's box ends 12.5pt
inside the edge it belongs to and is centred on the **plot**. The one up the side is turned on its
end, reading upwards, and is drawn into the chart's own picture rather than set as a line of text —
turned text has no baseline to compare with an upright one's, so it is held to Word by ink instead.

**A legend** takes 11.8pt and a line along the top or the foot, and 15.118pt and its widest entry up
a side. Its key is a square 0.5492 of the type size across, the words beside it begin 0.8239 of the
size less 0.376pt from the key's own left edge, and the key sits that much again below their
baseline. Along the foot the entries are set 0.784 of the type size apart — except where one entry
is long enough that a seventh of it is more, which is what the four-series page shows and what
nothing here explains — and the whole block is centred a little right of the middle. Up a side they
are one to a line, 1.8083 type sizes apart, centred on the middle of the frame.

**A number written at a point** takes nothing from the plot and sits clear of what it names: past the
end of a bar by four and a half points and its own descender, inside the end by the same four and a
half and its ascender, and to the right of a point on a line by 8.5pt. What would overrun the top of
the chart is set against it instead. On a slice of a pie it goes out along the middle of the slice,
**14.3pt inside the rim** — measured on two pies of different sizes, whose labels sit 14.79, 14.58,
12.52 and 14.39 inside a rim of 72.96 and 14.17, 14.36, 12.30 and 14.15 inside one of 83.5. Word
fits those to the slices by a rule of its own, and the odd one out of each four is the narrowest
slice, which it pushes further out; that slice, and the degree and a half Word turns some labels off
the middle of their own, is the one place on these pages where two points of disagreement are left.

A pie or a doughnut with anything written on it also **gives way to it**: where Word is placing the
plotting itself, the disc comes out 0.86083 of the plot it would otherwise fill — 83.5 of the same
194 point plot that an unlabelled one fills to 97 — and by the same share whether the labels are ten
point, fourteen or twenty, so it is not the room the words take that decides. A chart that states
where its plot area goes is drawn exactly there, labels or no labels.

Everything else agrees with Word to within 0.73pt vertically and half a point horizontally, and the
ink of a page agrees on better than 99.3% of it.

### Four more kinds of chart

A doughnut, a bubble chart, a radar and a stock chart are the four kinds a document is likely to
hold that are none of the six above. Each is described by the same kind of part — numbers, axes and
formatting — and each was measured against Word's own export of it: `chart-doughnut-bubble`,
`chart-radar-stock`, `chart-kinds-probe`, `chart-kinds-probe-two` and `chart-legend-key-probe`,
fifty pages between them.

**A doughnut** is a pie with a hole through it. The hole is a percentage of the whole disc — a
quarter, a half and three quarters of an 86.4pt disc give holes of 21.6, 43.2 and 64.8 — and where
the chart holds more than one series, what the hole leaves is divided evenly between them, the first
series innermost: two series of a disc of 86.4 with a hole of half give rings of 43.2 to 64.8 and
64.8 to 86.4. A share written on a ring sits **at the middle of that ring**, whatever size it is set
in, which is the one label on a chart that needs no fitting at all. Its legend names its slices
rather than its series, as a pie's does.

**A bubble chart** is a scatter whose points carry a third number, drawn as how large a bubble to
put there. How large the largest one comes out is the frame's doing and not the plot's — a page
whose plotting is made small draws the same bubbles as one whose plotting fills the frame, and a
frame turned on its side draws the same as one standing up:

> diameter = (shorter side of the frame − 10) × scale ÷ (scale + 333⅓)

which gives 47.538 of a 216 point frame at the hundred per cent a chart means by saying nothing,
97.385 of a 432 point frame, and 131.684 at three hundred per cent of 288 — to the third decimal
place on all eleven pages, at seven scales and four frames. So however large the scale is asked to
be, the bubbles run out at the frame less five points a side. The rest are drawn in proportion by
**area** unless the chart says its numbers are widths, and each is kept inside the plot: Word wraps
every bubble in the plot area's own rectangle, so one larger than the plot is cut off at its edge
rather than drawn over what is written round it.

An axis a bubble chart leaves to Word reaches **a step further at each end** than the same numbers
would give a scatter, which is how the bubbles get somewhere to be: a foot running 1 to 7 comes out
−2 to 10 by twos where a scatter gets 0 to 8, and a side running 10 to 55 comes out 0 to 70 by tens
where a scatter gets 0 to 60. The side keeps its nought, since a value axis of nothing but positives
begins there whatever else is true. The marks those extra steps add are marks like any other and are
counted against the room the axis has for them.

**A radar** sets the categories round a circle and measures the values out from its middle. Word
squares the plot area to its shorter side and centres it in what it was given, so a plot 216 by
172.8 becomes 172.8 square, 21.6 in from each side. The first category is at the top and the rest run
clockwise; the value axis is ruled as one many-sided figure per mark rather than as lines across the
plot, and the axis itself and the spokes the categories stand on are not drawn at all. A series is
the figure through its own points: outlined where the chart draws lines, filled where it says
`filled`, and marked at its corners where it says `marker`.

The words round a web are set against a circle **a twenty-fifth wider than the rim** — 89.856 outside
a rim of 86.4, 74.33 outside one of 71.487, 90.35 outside one of 86.872 — with the near edge of each
label on that circle and its baseline where the circle crosses its own spoke, less 0.8pt and plus
half the difference between the label's ascent and its descent. It is a share of the web and nothing
to do with the type: the same web labelled at ten point and at twenty sets both at the same distance
out. One at the very foot is centred on its spoke and hangs its ascender on the same circle; one at
the very top clears it by a further 1.63pt below its descender. The numbers go up the middle,
ranged against it and ending 1.02 type sizes short less a twentieth of a point.

A web left to Word to place keeps `1.5385 × the type size + 5.743` clear on every side of the frame,
which is what decides how large it comes out: a 216 point frame gives a web 173.744 across at ten
point and 142.974 at twenty, and both are the line through them.

**A stock chart** is three or four series read together as one day's trading, and what it draws is
the lines *between* them rather than lines along them. Which series is which is said by nothing but
their order — high, low and close, or open, high, low and close. The line from the day's lowest to
its highest stands where a line chart's point would; the bar from what the day opened at to what it
closed at is as wide as one bar of a bar chart holding a single series, so a category 63 points wide
gives 25.2 at the gap of 150 a chart means by saying nothing and 42 at a gap of 50. A day that
closed higher than it opened is drawn white and one that closed lower black, both outlined, where
the chart says nothing about either. The close of a chart with no opening is shown by whatever the
series marks its points with, and a series marking them with nothing shows nothing: Word draws no
tick of its own.

**A legend draws a line** beside a series that is a line rather than a shape — a line chart's, a
radar's, a scatter's — 19.2pt long with the words beginning 21.225pt past where it starts, both at
ten point and at twenty, so neither is a share of the type. A series that marks its points draws one
at the middle of the key as well, and a series that is neither a shape nor a line — a stock chart's,
whose drawing is all in the lines between the series — gets no key at all, which is what puts its
three names where Word puts them.

### Setting an equation

An equation is not a line of runs. Word writes it in a language of its own — Office Math Markup, in
its own namespace — and what is in it is fractions and radicals rather than paragraphs and runs, so
a reader walking a paragraph looking for `w:r` finds nothing at all in one. That is what used to
happen here: an equation reached the page as the space it took up and nothing else.

Almost nothing about how one is set is a number anybody chose. A face meant for mathematics carries
a `MATH` table — where the axis of an equation sits, how far a superscript rises, how thick a
fraction's bar is, how much room a radical leaves over what is under it, and a set of taller shapes
for every bracket that has to grow. Those are read and used, and the rules that combine them are the
ones the OpenType specification lays down. What was measured is where Word departs from them, and it
departs in ten places:

- **An equation is set at the size of the text carrying it, not at the size its own runs state.**
  Its letters are drawn at their runs' size and everything else — every distance from the table,
  every bracket and radical it stretches — is measured in the em of the text round it.
  `math-structure-probe` is what says so: its paragraphs are twenty point and every run inside its
  equations is twelve, and Word draws the letters at twelve and the brackets and radicals round them
  at 19.92, which is twenty rounded to the 1/300 inch Word rounds a size to. An equation on a line
  of its own has no text round it, and takes its own runs — which is why the radical of the
  quadratic formula is at twelve point where the same radical in a sentence is 11.04.
- Its **script sizes are the face's own percentages taken down to a whole half point**. Cambria Math
  says 73% and 60%; twelve point gives 17.52 half points, so seventeen, so 8.5pt, written out as the
  8.4 Word rounds a size to. The same rule gives Word's 6.96 for a script of a script of twelve
  point, its 17.52 for a script of twenty-four, and its 4.08 for a script of six — three sizes and
  two levels, none of them a simple share of the size.
- **Inside something** — a bracket, a radical — the room between a letter and the sign after it is
  four eighteenths of the em the equation is *set* at and the letter's lean is not counted at all;
  out in the equation itself it is four eighteenths of the em its *letters* are and the lean as
  well. `x+y=z` agrees with Word's to four decimal places on every gap of it, and the pair inside a
  radical to a hundredth of a point.
- **A script sits in the corner of the letter it is on by what the face says of that corner.** The
  face states a lean for the letter and a kern for each of its four corners, the kern as a staircase
  of values by height; a superscript takes the lean and the corner kern, a subscript takes the
  corner kern alone, and the script's own opposite corner is added to it, each in its own em.
  Word's *f* with an *x* under it pulls the *x* back 2.35 points — the −400 units the face states
  for the f's bottom right — and its *f* with an *x* over it pushes the *x* out a point, which is
  the f's lean, 65 units for its top right corner and 65 more for the x's bottom left. **None of it
  applies where the letter is not the size the equation is set at**: the same *x²* kerns in a twelve
  point equation whose letters are twelve point and does not with sixteen point letters, or with
  twelve point letters in a sixteen point paragraph.
- The **baseline-drop rules apply to what is built rather than to a letter**, which is TeX's own
  rule: a script sits on a letter at the shift the table states, and on anything else at that
  thing's height less a drop. What counts as a letter is one glyph at the size the equation is set
  at — the *b²* of `math-kern-probe` is one and takes the stated shift, the *i²* of the equations
  fixture is a twelve point letter in an eleven point equation and takes the drop, and the limits of
  an integral are placed from the integral's own ink exactly. Under a radical the cramped shift is
  used, which is what Word uses.
- Where a superscript and a subscript would close up on each other, **the room wanted is shared
  evenly between them**: Word sets the two of *x* with an *i* under it at 4.56 and 2.64 where the
  shifts alone would give 4.04 and 2.25.
- A **bracket grows by the shapes the face keeps** rather than by being drawn larger, and it need
  only cover **five sixths** of what it holds before Word stops reaching for a taller one.
  `math-bracket-probe` walks a bracket up the whole of the face's series by growing what it holds
  from twelve point to seventy-two: seven of the twenty-two are the step from one shape to the next,
  and they put the factor between 0.8320 and 0.8434. Past the end of the series the face states a
  recipe — a head, a foot, and a middle repeated as often as it takes — and Word builds one, which
  is what happens to a bracket round a seventy-two point letter in a twelve point equation. It is
  the *whole* height that decides whether to build rather than the five sixths, and the pieces are
  overlapped as far as the face allows.
- An **n-ary operator is centred on the axis** — which raises Word's sum half a point and drops its
  integral by as much — and the **1.8886 points** it leaves after the limits before what the sum is
  taken of is a number no constant of the table and no fraction of an em accounts for. It is written
  down as what it was measured to be, from a sum and an integral that agree on it exactly although
  they agree on nothing else.
- **Which rules an operator's limits follow is the operator's own doing, not the markup's.**
  `math-nary-probe` writes each of four operators both ways round — limits above, limits beside —
  and a sum is set identically either way, as is an integral, while the two disagree with each
  other. An integral's limits are scripts on a box, placed from the operator's own ink; a sum's are
  placed by rules of their own: the lower one goes down by the stated shift **and 0.115 of what is
  in it**, the two of them straddle a line **0.08 of the size** apart, and an upper limit with no
  lower one takes the stated shift from that same line. Twenty limits, and every one of them lands
  where Word lands it but for two that round the other way.
- A limit the markup writes and leaves empty — which is what a document says when a limit has been
  deleted — **is not a limit, but the line still leaves room for it**: the face's typographic
  ascender at the size a limit is set at, and nothing below. Word's sum with only a lower limit asks
  its line for 11.0 points where the limit alone would ask for 4.4, and the 6.6 between them is that
  ascender to a twentieth of a point across five probes.

A slanted fraction is set at the full size with a taller *fraction slash* — not the solidus, which
the face keeps no shapes for — its numerator raised 0.3 of the type size and its denominator dropped
by the table's own shift. A matrix stands its columns an em apart and its rows a line apart, the
line being the face's ascent and descent and the leading the table asks for.

Every position in an equation is rounded to Word's own 1/300 inch, which is the only reason the
figures above come out exactly rather than a hundredth away.

The `equations` fixture holds seventeen of them beside Word's export of the same file. Drawn and
compared line by line, the two agree on **99.8%** of their ink, and every equation begins within a
third of a point of where Word begins it — most within four hundredths.

**How tall a line holding an equation is** was the last part of it that was not Word's, and
`math-line-box-probe` settled it. It stands twenty-five equations between rails — a two point full
stop on a line of its own — so that the room each asks for above and below the line can be read off
Word's page directly. Two things decide it:

- the **ink of everything in the equation, with the face's own math leading over it** — 300 design
  units, 1.6 points at eleven point. Nothing below: what hangs down asks for its ink and no more.
- and never less than **a line of the face at the size the equation is set at**, which is what a
  bare letter gets and what an equation whose ink is small keeps. An equation of nothing but letters
  is the one case where that floor follows the runs instead: an *x* at twenty-four point in an
  eleven point paragraph asks for a twenty-four point line.

Twenty-five probes, at three sizes and two levels of script, come out within 0.56 of a point of
Word's — most within a quarter, which is the 1/300 inch Word rounds a position to — and one within
0.92: a sum whose limits this engine places where the OpenType rules put an integral's and Word
places lower. Across the whole `equations` fixture the drift down the page is under two points,
where the reading before this probe existed left it thirteen.

`math-kern-probe` is what settled the corner kerns: fifteen scripts on letters chosen for what the
face says about them — the largest kern it states, the smallest, a negative one, and a staircase
whose step a full stop's ink does not reach. Every one of them lands within four hundredths of a
point of Word's, and fourteen within seven thousandths. Which height Word reads a staircase at
cannot quite be pinned: it behaves as though it reads the value where the glyph's own ink ends,
which Cambria Math's data cannot separate from reading it at the script's own baseline in the
script's em — but both differ from reading it at the height of the script, which is what the
specification's wording suggests and what Word's own full stop over an *i* rules out.

One more thing the probes turned up, which is about sizes rather than brackets: **Word measures at
the size a run states and writes the size rounded** to its 1/300 inch. It draws the *x* of a sixteen
point run at 16.08 and puts the script after it at the advance of a sixteen point *x*; it sets the
row *i=1* of the equations fixture at the advances of eight and a half point although it writes 8.4
for every one of them. The two are separated here: every measurement is at the size, every size
written into the file is rounded. What a bracket has to cover is the exception, since what it covers
is what is on the page.

An equation's letters are drawn from the mathematical alphabets — an *x* in an equation is U+1D465,
a character of its own, which is what Word draws — so what a reader copies out of one of our pages
is the letter that was set. Word's own file maps them back to nothing at all. The letters are drawn
into the line as a group of their own rather than in among the words on either side, so everything
is there and selectable and each is where Word puts it, but dragging across a whole line copies it
in a different order from Word's.

### Content wrapped in something else

A body holds paragraphs and tables, and a reader that walks it looking only for those two is right
about every document written by hand and wrong about most documents written by Word. Three things
wrap ordinary blocks:

- a **content control** (`w:sdt`), which Word puts round the cover page, the table of contents and
  every placeholder a template leaves to be filled in;
- a **compatibility alternative** (`mc:AlternateContent`), which offers the same content twice over;
- the old **custom XML** element (`w:customXml`), round whatever an older document tagged.

All three are unwrapped, wherever blocks are read: the body, a table cell, a running head, a note
and a text box. `content-controls` puts one of each on a page and names the line inside it, and
Word draws all of them in place with no more room between the lines than any other paragraph gets.
Where an alternative offers two branches Word draws the **choice** rather than the fallback — the
fixture's two branches hold different words so that its export says which — and that is the opposite
of how a *run*-level alternative is read here, where the choice may be a drawing this cannot read
and the fallback is what it is for.

This was found by asking the converter what it did with each, and it had been losing all of it in
silence. The test that should have caught it could not: it compared the text on the page against
the text in the *parsed model*, and a construct the reader drops is missing from both sides. It now
reads what the document says from the part itself, so the two sides come from different code, and
reverting the fix fails it.

### What a diagram is, and which half of it to draw

SmartArt is written down twice. There is what it means — points, the connections between them, and
a layout definition saying how points of that shape are arranged — and there is the arrangement it
last came to, kept beside it as a flat list of shapes at absolute positions with their geometry,
colours and text. The first is a language, a system of constraints and algorithms with a hundred
layouts written in it. **Word runs it afresh every time it opens a document**; every other reader
draws the cached arrangement, and so does this.

That is measured, not assumed: the seed for the `smartart` fixture carries a cache no layout would
produce — three boxes stepping down the frame — and Word's export shows three boxes in a row filling
it. `Word_lays_a_diagram_out_again_rather_than_trusting_the_cache` keeps that fact in the suite.

It also decides how a diagram can be held to Word at all. A hand-authored cache says nothing, since
Word throws it away; the only cache worth comparing is the one Word itself wrote, so the fixture is
a **real** document — the seed goes through Word, which rebuilds the diagram and saves its own
arrangement, and that is what gets rendered and compared.

Two things about diagram text differ from anything on a page. Its spacing is a percentage of a
line, where DrawingML's line is a flat six fifths of the type size rather than whatever the face
asks for — Word sets the fixture's paragraphs 15.6pt apart where 35% of the type size would be
12.6pt and 35% of six fifths of it is 15.1pt. And a word too wide for its box **comes apart between
its letters**: Word sets "Three" in a 67.84pt box as "Thre" and "e". So does a page — this
repository believed otherwise until `break-tolerance-probe` asked a page directly.

What is left is a constant 3.1pt: every line of the diagram is where Word puts it across the page,
and every line the right distance below the one above it, but each box's block of text sits 3.1pt
high. The block is centred, so that is either a 6.2pt disagreement about how tall the block is or a
3.1pt one about where the first baseline sits inside it — and those cannot be told apart here,
because Word writes the cache itself and so chooses the type size, the line spacing and the
anchoring. Both readings fit every line. It is recorded as a known divergence rather than fitted
away.

### How a diagram sets the text in its boxes

Two rules, and each was worth a point or two of the smartart fixture's divergence:

- **A percentage line spacing in a drawing takes its room off the top.** The same 90% that leaves a
  document's paragraph sitting at its natural ascent — losing the room below the baseline, which
  `line-spacing-multiples` measures — takes the room from above in a diagram, so the ascent is the
  scaled line less the *whole* descent. At 36pt Calibri: the line is 39.55, the descent 9.67, and
  Word's first baseline sits 29.90 below the frame where the face's own ascent is 34.28 and nine
  tenths of it is 30.85.
- **No space is kept after the last paragraph of a box, nor before the first**, unless its body
  says `spcFirstLastPara="1"`. Word's diagrams put 35% of a line between paragraphs, so keeping it
  at the end makes the block a third of a line too tall — and a block centred in its box then sits
  half of that too high.

`smartart-lines` is what separates them, and the separation is the whole point of it: with the text
centred, as a diagram normally sets it, the height of the block and the place of the first baseline
inside it are added together and no measurement can pull them apart. So the probe asks Word for
text anchored to the *top* of its boxes — through the layout, since Word rebuilds a diagram's cache
from that and not from what the document last held — and then the first baseline is the frame plus
one ascent and nothing else. Its three boxes hold one, two and three paragraphs, which is what says
the remaining error is constant rather than per line.

With both, the diagram in `smartart` agrees with Word to a single step of the grid, where it stood
at 3.36 points.

### How large a watermark is set

A watermark is a word on a path, and the size the document gives it is a single point — the shape
type says the letters are to be fitted to the shape, and that is what decides how large they come
out. What is fitted is the **ink itself**, stretched to fill the shape less its own insets, and not
the box the face would set the word in. `watermark-fit-probe` says so seven times over: the same
rectangle of ink comes back whether the box is asked for DRAFT, for CONFIDENTIAL, for a short word,
for a word with a tail below the line, or for the same word in another face — and it is the box
less the tenth of an inch at the sides and half of that above and below that every text box has.

The letters are stretched to that box rather than scaled to it, so a word with a descender is
squashed to the same height as one without. Every page of the probe agrees with Word on better than
99.8% of its ink, and the diagonal watermark of the `watermark` fixture on 99.89%.

Two things about it are deliberately not Word's. Word's export turns the letters into outlines, so
its own file holds no watermark text at all and a reader cannot search for the word; this keeps it
as text, which is why the line-by-line comparison has to allow the two files a different number of
lines. And Word for Mac draws a watermark in the document's own face whatever face the document
asked for — the probe's sixth page asks for Times New Roman and gets Calibri — where this sets it in
the face that was asked for. That page is the only one of the seven that agrees on less than 99.8%
of its ink; it agrees on 98%.

### What washing a picture out does

The other kind of watermark is a picture, faded until the page can be read through it, and the
fading is two numbers on the image: a gain and a black level, both written in sixty-fourths of a
thousand. `watermark-washout-probe` holds the same bands of flat colour six times over at different
settings, and what comes out of each channel — everything in nought to one — is

    gain × in + (1 − gain) ÷ 2 + black × (1 + gain)

clamped at both ends. The gain is a contrast about mid grey: half a gain leaves grey alone and pulls
black and white halfway towards it. The black level is a brightness on top of that, and counts for
more when the gain is high, which is the part of this that is fitted rather than explained. Word
writes a gain of 19661 and a black level of 22938 for every picture watermark it makes — three
tenths of the contrast, and pale enough to read a page through.

Every band of every setting comes out within one part in 256 of Word's, including the two that
saturate.

### Where an old-style shape is drawn

A shape in the older `w:pict` spelling is drawn a little way down and to the right of where its own
size puts it, and how far depends on the weight of its outline. `vml-stroke-probe` holds the same
rectangle thirteen times over, varying nothing but that weight:

| outline | none | ¼pt | ½pt | ¾pt | 1pt | 1½pt | 2pt | 3pt | 4½pt | 6pt |
|---|---|---|---|---|---|---|---|---|---|---|
| offset | 0 | 0 | 0 | 0 | 0 | 2pt | 2pt | 2pt | 4pt | 6pt |

The offset steps in twos rather than growing with the weight, and it starts a whole point in: it is
the smallest even number of points that reaches a point short of the outline. The text inside moves
by half as much again, which is what the six point page of `vml-inset-probe` shows — its text sits
at the very edge of the box, where the inset alone would put it three points inside. The last three
pages of the probe rule out the two obvious alternatives: a rounded rectangle and an ellipse at the
same weight are offset identically, and two shapes on one line neither shift each other nor
themselves.

Why it steps in twos is not explained here. What is implemented is the rule the measurements fit,
and none of it shows in an ordinary document: Word draws a text box with a ¾pt outline, and
everything at a point or less is offset by nothing at all.

Two things about the same shapes were measured and are **not** implemented, both of them out of
reach of any ordinary document. The first is that an outline thicker than a point also makes the
shape's *line* taller, so what follows it sits lower — by 0.96pt at 1½ and 2 points, 1.92pt at 3,
4.08pt at 4½ and 5.04pt at 6, which follows neither the weight nor the offset the same shape is
drawn at. The second is that such a shape sharing a line with another nudges its neighbour by about
a point. `vml-stroke-probe` holds the first as a known divergence in
`TextPositionComparisonTests`, and `vml-shapes` holds the second: the two pages agree on 96% of
that page's ink rather than the 99% the newer spelling's shapes manage.

### Which part of a table style reaches which cell

A table style is unlike every other kind: what it says depends on where a cell is rather than on
what the cell says about itself. It can describe thirteen different parts of a table — the whole of
it, the banding across the rows and down the columns, the first and last rows, the first and last
columns, and the four corner cells — and the order in which those override each other was measured
rather than read. `table-style-conditional-probe` gives every one of the thirteen a different type
size, so the size Word draws a cell at names the format that reached it, and seven pages of tables
give the whole lattice at once. Two of the answers are not the ones the specification's ordering
would give:

- **Banding down the columns beats banding across the rows.** With both in force the rows leave no
  mark at all — every cell in the middle of the probe's first table comes out at its column band's
  size — so the fixture needs a page with the column banding turned off to see the row banding at
  all.
- **A first row beats a first column.** Where a style defines no corner formats, the cell where the
  two meet is drawn in the first row's size.

The rest is as expected: the whole table, then the banding, then the edge columns, then the edge
rows, then the corners, each overriding what came before. Two orderings could not be measured
because nothing makes the two formats meet in one cell — a last row against a last column, and the
corners against each other — and each follows the pattern of the pair beside it.

A table of one row has a **first row and no last one**, and a table of one column a first column
and no last one. The single row of a four-column table comes out in the first row's size, and the
cells at either end of it in the north-west and north-east corners rather than the southern pair —
which matters, because a one-row table is what a great many documents use for a banner or a form.

Three more things the same fixture settled. `w:tblLook` gates every one of the conditional formats,
the corners and the banding included: with it turned off, a table drawn by a style with all
thirteen comes out entirely in the whole-table formatting. Banding begins counting *after* the
first row or column where there is one, and runs on through the last one whether or not a format
for it exists. And a table style sits between the document's defaults and the paragraph's own style
in the cascade — a paragraph style used in a cell overrides the table style, and direct formatting
overrides both.

### The border round a page

`w:pgBorders` draws a line round the page, and where that line falls depends on what it is measured
from. `page-border-probe` puts four sections to Word and reads the answer off the ink:

- **Offset from the page**, the space is to the **outside** of the line: a border 24 points in has
  its outer edge at 24 and its ink from 24 to 24.96.
- **Offset from the text**, the space is to the **inside**: a border against the text with no space
  at all has its inner edge on the margin and its ink just outside it.
- **`w:display`** picks the pages — `firstPage` means the section's first page and no other.
- **A missing side lets its neighbours run on to the paper's edge.** A border with a top and a left
  and nothing else draws its top line the full width of the page, not the width of the border.

A drawn width rounds **down** to the 1/300 inch grid rather than to the nearest. Three points is the
case that says so, being exactly half a step from either answer: Word draws it at 2.88 where 3pt is
12½ steps. Every coarser weight agrees either way.

Word draws each side as a bar between the corners and then fills the corners in; this draws one bar
corner to corner, which covers the same ground. So the comparison is of ink rather than rectangles,
and every page of the probe agrees with Word's exactly — no point of the margins differs.

### Numbers down the margin

`w:lnNumType` sets a number beside every line, or every fifth, out in the left margin. It is a
section's property rather than a paragraph's, and `line-number-probe` puts the whole of it to Word
in three sections and one export:

- **`w:countBy` counts every line and prints some of them.** With `countBy="5"` the count still
  runs 1, 2, 3 — only the multiples are set, so the tenth line carries a 10 whether or not the
  ninth carried a 9.
- **`w:restart`** is `newPage`, `newSection`, or `continuous`, and **`w:start` is ignored where the
  count is continuous**: the probe's middle section asks to begin at 10 and Word carries on from
  the 6 the section before it reached.
- **An empty line is counted**, which is why the probe's "Counting again 1." is the eighth line and
  not the seventh.
- **`w:suppressLineNumbers` on a paragraph passes its lines over entirely** — no number, and no
  turn: the count comes out of the two suppressed lines the same as it went in.
- **The paragraph a section break is written on is not counted.** That one is read off the count
  rather than seen: it runs 6, 7 across the break where counting the break's own paragraph would
  have made it 6, 8. Whether Word lays that paragraph out at all its export cannot say, since it
  falls at the foot of a page where an invisible line and no line look alike.

The number is set in the document's default face at the default size, both quantised like any other
text — Calibri 11pt is drawn at 11.04. `w:distance` names how far the *end* of the number stands
from the text, and 18 points is what Word uses when nothing says: a single figure begins at 48.48
on a page with an inch margin, and 720 twips moves it exactly 18 points further out.

**The width it is set against is the sum of its figures' widths rounded to the grid**, not its true
width. Word's own numbers say so: one figure stands at 48.48 and two at 42.96, which is 5.52 apart
where the figure itself measures 5.597 and 5.52 is what that rounds to. It follows that every
number lands on the grid, which every one of Word's does.

Every number of the probe agrees with Word's — the same figure, in the same place to a hundredth of
a point, beside the same line.

### A phonetic guide over a word

`w:ruby` sets a reading over the word it belongs to — ふりがな over 振仮名 — and a run holding one
was dropped whole: the guide went, and so did the word under it. `ruby-probe` puts every alignment
the markup has to Word, over the same word, and each of them comes out where Word puts it:

- **The wider of the guide and the word decides the room the pair takes**, and the narrower is set
  in the middle of it. A guide of eight letters over one takes forty-eight points, with the word
  centred underneath; a guide narrower than its word takes the word's own room.
- **`center`, `left` and `right`** put the slack between the two ends, at one end, or at the other.
- **`distributeLetter`** spreads the guide's letters so its ends meet the word's — four letters
  over three take three gaps of four points each.
- **`distributeSpace`** spreads them the same way but leaves space outside as well: half a gap at
  each end, which is what Word's 33 points inside a 36 point word comes to.
- **The guide sits on a baseline of its own**, raised by `w:hpsRaise` and set at the size `w:hps`
  names, and **the line grows to hold it**: the probe's lines stand 20.4 points apart where the
  twelve point Mincho alone would take 15.6.

The line box is the word's rather than the run's that wraps it — a guided word set in Mincho gives
the line a Mincho box however the run round it is written, which is what Word's own line spacing
says. And the pair is written into the page where it stands in the line rather than after
everything else on it, so that what a reader copies out reads as the document does.

### Two tables written one after the other

A document that means two tables must put a paragraph between them: two `w:tbl` elements that touch
are **one table** to Word, and the difference shows. `adjacent-tables-probe` borders each table
three points at the top and foot and half a point inside, so the join can be read off the ink — and
two touching tables come out with one line round the pair, none where they meet, and no space
between them either.

So they are folded into one when the document is read, and what the second table said about itself
is not thrown away with it:

- **Its rows keep the columns they were written with.** The probe's second table names its columns
  the other way round — a narrow one first — and Word keeps them that way, a table being free to
  have rows of different widths.
- **Its rows keep their own indent**, which is a row's property in Word's model rather than a
  table's. So does the *first* table's — the merged table stands where the first table's indent
  puts it, and then each row is indented again by whatever its own table asked for, which means a
  first table asking for half an inch has its own rows an inch in. That reading is forced by the
  one page where the first table is the indented one.
- **What it said about its borders is** thrown away: the line round the merged table is the first
  table's.

**And where the merged table will not fit, the whole of it is squeezed until it does.** A row that
overruns — because its own columns are wider, or because it asks to be indented, or both — does not
hang off the edge: Word fits every row's columns and every row's indent by one scale, so that the
widest row ends exactly at the width the first table declared. `merged-indent-probe` measures it
over ten pages:

| the second table | the widest row wants | the scale |
|---|---|---|
| indented 18 points | 233.52 | 0.925 |
| indented 36 | 251.52 | 0.859 |
| indented 72 | 287.52 | 0.751 |
| indented 108 | 323.52 | 0.668 |
| 270 points wide, not indented | 270 | 0.8 |
| 270 wide and indented 36 | 305.52 | 0.707 |
| narrow enough to fit indent and all | — | none |

so it is the width that decides it and not the indent, and a table that fits is left alone. What a
row is fitted *to* is the width the table **declares** rather than what its own columns come to: a
first table calling itself 180 points wide over a grid of 216 squeezes to 180. The indent an
overrunning row keeps is measured the way any indent is — to the edge the cell's text stands at,
so the border and the cell margin are absorbed into it — and is scaled with everything else.

Every row of all ten pages lands within 0.4pt of Word's, and the fourth page of
`adjacent-tables-probe`, which used to stand 5.54 points out, now lands within 0.03. The residual
is Word's own rounding of the share each column takes of the squeezed total: its first column comes
out a whisker narrower than two thirds every time, and what decides that is not measurable from
here.

### The boxes a form is filled in by

A legacy form field holding `w:checkBox` draws no text at all: the box **is** the field, and Word
draws it with lines rather than setting a character from a face — which is why a document full of
them came out with nothing where the boxes should be. `checkbox-probe` puts fifteen of them to
Word, ten sizes from eight point to seventy-two, some stating their own size and some taking the
text's, and three numbers come straight off the drawing:

- **The field is 1.15 times the size wide.** Exactly that, at every size measured — 13.8 points for
  a twelve point box, 82.8 for a seventy-two point one.
- **The box is drawn in the middle of that**, 2.2 points narrower, so it is inset 1.1 either side.
- **Its foot sits below the baseline** by 0.216 of the size, less 1.2 points: level with the
  baseline at eight point, a fifth of an inch below it at seventy-two.

The square is drawn three quarters of a point thick whatever its size, and a ticked one takes a
cross of two half-point lines corner to corner. Word strokes its square where this fills the four
sides of one, which covers the same ground; the cross needed a new primitive, since a rule is a bar
lying along the page and a diagonal is not.

A box that states its own size takes it whatever the text around it is set in, and one that does
not takes the text's — and either way the line grows as though a letter of that size were on it.
Every line of the probe sits where Word puts it, and the ink of the boxes covers what Word's covers.

### Breaking a word at the end of a line

`w:autoHyphenation` lets Word break a word between two lines. Where a word may be broken is not
something that can be worked out from its letters — it is a matter of a language's habits, and
every program that does it carries a table. This one carries Liang's patterns, as TeX has
distributed them since 1990, turned into source by `tools/make-hyphenation-tables.py` the same way
the Unicode tables are: the library has no dependencies and the answer must not depend on a file
being present at run time. The pattern file's own licence asks that its copyright notice be
preserved, and the generated source carries it.

Word's dictionary is its own, so the question was whether the two agree. They do:
`hyphenation-probe` gives Word a paragraph of long words in a narrow measure and every line comes
out where Word puts it — conspicu-ous, exam-ples, misun-derstanding, un-derstanding, or-ganisation.

Two rules are this library's rather than the table's, and both were measured:

- **A word is broken at the last place that fits.** Word breaks conspicuous after "conspicu" and
  organisation after "or", each being as much of the word as the line had room for.
- **Two letters must stay behind and two must go on.** The pattern file states two and three, as a
  typesetter would, but Word breaks PARTICULAR-LY and leaves LY to the next line.

The rest is what the document asks for, and each of the four is a fixture of its own:

- **`w:hyphenationZone`** is how much white a line may be left with before a word is broken to fill
  it — a quarter of an inch where the document says nothing. An inch of it leaves every word in the
  probe whole.
- **`w:consecutiveHyphenLimit`** is how many lines in a row may end in a hyphen. Two of them stops
  the third.
- **`w:doNotHyphenateCaps`** leaves a word in capitals whole.
- **`w:suppressAutoHyphens`** on a paragraph leaves that paragraph's words whole.

All four agree with Word's own export line for line.

### Columns the other way round

`w:bidiVisual` turns a table about: the first cell of a row stands at the right and the rest follow
leftwards. `column-order-probe` puts five tables to Word — three columns of different widths, each
cell shaded so the order can be read out of the ink as well as the text — and it turns out to be
the whole table that is turned, not merely the cells:

- **The table is laid from the right margin** rather than the left.
- **Its indent is measured from the right**: half an inch moves it half an inch leftwards.
- **The border a cell calls its left is drawn on its right.** The probe's table has a three point
  left border and half a point everywhere else, and the thick one comes out at the right-hand end
  of the mirrored table, which is where its first column is.
- **What that border does to the text inside is not turned about with it.** Word insets the
  content of a cell by the border it calls its left however that border is drawn, so the rightmost
  cell's text stands 1.44 points inside its left edge — half of a border drawn on the other side —
  rather than the half point the border there would ask for. Two of the probe's pages say so, and
  it is the one place where the mirroring is less than thorough.
- **Cells joined by `w:gridSpan` are joined at the right-hand end**, the columns they cover being
  the ones the row began with.

Every column of the probe stands where Word's stands, within a tenth of a point, with the same
words in it.

### Text turned on its side in a cell

`w:textDirection` turns a cell's text a quarter circle — `btLr` for the narrow heading a table
usually wants, `tbRl` for the other way. It is not the glyphs that are turned but the whole frame
the paragraphs are laid in, and `cell-direction-probe` puts eleven of them to Word:

- **The line runs along the cell's height and the lines stack across its width.** `btLr` reads from
  the foot of the cell upwards and stacks from the left; `tbRl` reads from the head down and stacks
  from the right.
- **Word does not make the row any taller to hold it.** A turned cell in a row one line tall breaks
  its text every two letters and runs out of the cell to the right — Word draws it there, past its
  own table, rather than growing the row. The height is settled by the cells that are not turned.
- **`w:vAlign` moves the stack of lines across the cell** rather than down it: top puts the first
  line against the left edge, bottom against the right.
- **The paragraph's own alignment works along the turned line**, so a centred one sits in the
  middle of the cell's height.

Every turned line of the probe stands where Word's stands, to within a step of the grid, reading
the same way with the same words on it. The comparison against Word that covers the rest of the
document leaves turned runs alone — a turned baseline cannot be set against an upright one's — so
`CellDirectionTests` reads them out of both files and sets them side by side.

**A word too wide for a box is broken inside it.** A page lets a long word overrun the margin and
stay whole, but a box does not: Word breaks it wherever it has to, taking as many letters as fit.
The probe has a cell a fifth of an inch wide in which Word sets "Unturnable" as "U", "nt", "ur",
"na", "bl", "e", and this now does the same — in a table cell as in a shape, upright as much as
turned. The break waits until the word has a line to itself, which is why "and rather" comes out
"an", "d", "rat" and not "an", "d r", "at".

### A table that floats

`w:tblpPr` takes a table out of the flow: it stands where it is put and the text runs round it.
`floating-table-probe` puts seven of them to Word — against each margin, on the paper itself, with
half an inch of daylight, with none, half an inch further down, and one drawn with a three point
border — and reads the answers off the ink and the text together.

- **The place names the cell's text edge, not the table's edge.** The same rule `w:tblInd` follows.
  The thick-bordered page is what proves it: thicken the border from half a point to three and the
  border grows outward while the text stays on the margin.
- **Down the page the place names the outer edge instead.** Word draws the thin border and the
  thick one with their tops in the same place, which only holds if what is put there is the outside
  of the line.
- **The daylight is measured from the outside of the border too**, which is why the text beside the
  thick-bordered table stands a point and a half further out than beside the thin one.
- **`tblpXSpec` names a place rather than measuring one** — `right` puts the table's right text
  edge on the right margin, `center` centres the box, since it hangs out equally at both ends.
- **A table anchored to the text stands where it would have stood** plus whatever `w:tblpY` says;
  one anchored to the paper stands where it is told and the flow takes no notice of it beyond
  making room.

The table is placed as a float in the same machinery a wrapped picture uses, so the lines it
reaches give up its width and the ones past it come back to the full measure. Every one of the
seven pages agrees with Word: the box within a tenth of a point sideways, and the text beside it
within half of one.

**A floating table with less of the page left than it needs breaks at a row**, and the rest carries
on at the top of the next page in the same place across the measure. That is what Word does with
one — `floating-table-break-probe` puts twenty rows where six of them fit and Word writes six, then
fourteen; sixty rows come out forty and twenty. Three more things the same probe settles:

- **The text that follows a broken table begins on the page the rest of it carries on to.** Word
  writes nothing beside the part that stayed behind, though it writes plenty beside a table that
  did not break — so the flow resumes below what was laid rather than beside it, which leaves it at
  the foot of the page.
- **A table anchored to the paper does not break.** One too tall for what is left below it is moved
  up until it ends at the paper's own edge, bottom margin and all: the probe puts one a foot down
  the page and Word draws it 28 points higher, ending exactly at 792.
- **A table with nothing left to carry it makes its own pages.** Sixty rows begun near the end of a
  document come out on a page of their own with nothing else on it, which is what Word does and
  what this already did for a footnote too long for its page.

A line that moves to the next page is **broken again there**. The measure it was composed against
is not always the measure it lands in — a float narrowed it on the page left behind and there may
be none here — and Word breaks such a line again rather than carrying its old shape over.

### Text down both sides of a float

A float with room on either side of it does not take the text with it: Word runs each line through
both gaps, left to right, as though the float were a hole in the paper. A line beside a table
standing in the middle of the measure begins at the margin, stops at the table, picks up again past
it, and ends at the right margin — one line, in two pieces, on one baseline.

That is what the layout does now. The free bands across a line are resolved in the order they stand
on the page, and the line is filled through all of them:

- **Each band is filled and finished on its own**, so a justified line is stretched to the edge of
  every band it passes through rather than to the last one only, which is what Word does with it.
- **Only the first band of a line is indented.** An indent is measured from the margin, and a band
  further across the page has left the margin behind.
- **A band too narrow for the next word takes nothing** rather than overflowing into whatever
  stands beside it. The first band is not held to that: a word too long for the whole measure has
  to go somewhere, and Word lets it overflow.
- A band narrower than a point is dropped before any of that: nothing can be set in it.

Measured against Word twice over, since it is the wrapping engine rather than the table that does
it — `floating-table-wrap-probe` for a table standing in the middle of the measure, and
`images-floating` for a picture doing the same. Every line of both agrees with Word's own export to
within a step of the grid, in the same places, with the same words on them.

### A clearance that reaches back

A float is not known until the flow reaches the paragraph it is anchored to, and by then the lines
above it have been written. Where its clearance reaches back over them — a table with half an inch
of daylight above it, a picture with six points — Word breaks those lines again round the float,
and so does this: the paragraph they belong to is taken off the page and laid again with the room
the float wants already spoken for.

Two things about it are Word's, not ours to choose:

- **The float stays where the flow first reached.** Breaking the lines above it can lengthen the
  paragraph they belong to and so move the flow, but the float does not follow it down. Word's
  export says so plainly: with the picture of `images-floating` standing in the middle of the
  measure, the line above it is broken round a picture that has not moved.
- **A float taking the whole measure is not treated this way.** It has no room to offer the lines
  it reaches back over, so they are moved down instead, which is what Word does with them and what
  this already did.

The paragraph the float belongs to has already made the room between itself and the one before it
by the time the float is placed, and laying that one again puts the flow back before the gap — so
the gap is made a second time, rather than left to be lost. `brochure`, whose text box keeps nine
points of clearance over a picture paragraph six points above it, is what said so: without it every
line of the paragraph sat six points high.

Only the paragraph immediately before the float is offered this, and only where it can be laid
twice — one that broke across a page cannot, since the page it left behind is not this page's to
take back. That is the case Word's own behaviour shows up in and the one a document is likely to
have; a clearance deep enough to reach back over two paragraphs leaves the further one alone.

### A dropped capital

`w:framePr` with `w:dropCap` is the big first letter a document opens a chapter with. It is not a
run but a **frame**: the letter is a paragraph of its own, and the paragraph after it makes room.
`drop-cap-probe` is written the way Word writes one — Word's own AppleScript was asked for a
dropped capital, and this is the markup that came back:

```xml
<w:pPr>
  <w:keepNext/>
  <w:framePr w:dropCap="drop" w:lines="3" w:wrap="around" w:vAnchor="text" w:hAnchor="text"/>
  <w:spacing w:after="0" w:line="827" w:lineRule="exact"/>
  <w:textAlignment w:val="baseline"/>
</w:pPr>
<w:r><w:rPr><w:position w:val="-11"/><w:sz w:val="112"/></w:rPr><w:t>T</w:t></w:r>
```

Everything needed to draw it is in that markup, and `w:lines` is not part of it:

- **`w:lines` is a record of what was asked for, not what is drawn.** Word writes the size it
  worked out onto the run and the height onto the paragraph, and the drawing follows those. A
  frame of three lines round a letter of ordinary size shortens **one** line, not three — the probe
  puts that case to Word and Word shortens one.
- **The frame is the letter's advance plus `w:hSpace`, rounded to the grid.** Word's own
  fifty-six point `T` measures 34.2167 and the lines beside it begin 34.32 in; with 180 twips of
  space beside a thirty-five point one measuring 21.3823, they begin 30.48 in.
- **Which lines make room is a matter of where the frame reaches**, not of paragraphs: a frame that
  outlasts a two-word paragraph goes on shortening the next one.
- **A cap in the margin hangs its own width to the left** and the text keeps the whole measure.
  Word writes `w:hAnchor="page"` for that one, which is the only difference between the two kinds.
- **`w:position` is the drop**, in half-points, and it is applied where every other raised or
  lowered run's shift is applied — after the line's baseline is on the grid.

Every page of the probe agrees with Word's: the letter in the same place at the same size, the same
lines shortened, and each of them beginning where Word's begins.

### Where an exact line puts its baseline

`w:lineRule="exact"` fixes the height of a line and says nothing about how the room is divided
above and below the baseline. At twelve point every reading of that is within a step of every
other, so it went unnoticed until a dropped capital asked for an exact line of forty-one points.

`exact-line-probe` settles it, and a sweep of fifty-three heights from twenty points to seventy-two
was run twice while it was written — once in fifty-six point Times, once in twenty-four point
Verdana. **Word put every baseline of the second sweep in exactly the place it put the first.** So
the share is Word's own and not the font's, which is the finding: the reading this replaced took
the share from the font's ascent and descent, and that is within a step of the truth at twelve
point and two steps out at fifty. The probe holds the same height in Times, Arial and Calibri —
whose own descents are 0.1953, 0.1897 and 0.2200 of their lines, five steps of the grid apart at
that size — and Word sets all three on one baseline.

**Four fifths of an exact line stands above the baseline.** That alone lands one step of the grid
out on about a fifth of the heights, and the last step was found by sweeping every height a twip at
a time rather than a point at a time: **865 heights from fifteen points to a hundred and fifty**, in
four exports. Two rules come out of it, and neither is derived from anything:

- The height behaves as though it were **one twip larger or smaller** before the four fifths is
  taken — a twip larger where the whole steps of the ascent leave one over four, a twip smaller
  where they leave two or three, the height itself where they divide evenly. That accounts for 779
  of the 865.
- Where the height **and its fifth both land half way** between two steps of the grid — which is
  every odd multiple of three points — Word takes a further step, at all but one such height in
  five and then one of those in five again. Written in base five, with *j* the number of such
  heights below this one: the step is taken where *j*'s last digit is under three and its next
  digit is not two.

The second is a measured pattern and nothing here explains why base five should come into it beyond
the four fifths itself, so it was checked the only way a fitted rule can be: against a second sweep
at sixty-three heights the first never reached, which it predicted every one of. Together the two
account for all 865, and `exact-line-probe` holds the nineteen heights that pin them.

**How the paragraph gets to its next line** is settled by `exact-line-advance-probe`, six pages of
twenty exact-spaced lines apiece: **Word advances by the height itself** and rounds each baseline
where it lands. A two-line sample cannot tell that from an advance of a whole number of steps — both
put the second baseline 83 steps below the first at twenty points — but twenty lines can, because
the gaps between Word's own baselines then take **two** values rather than one: 83 and 84 steps at
20.05 points, where the height is 83⅓. A rounded advance would put every gap on a page at the same
number, and would drift by up to three points over those twenty lines.

Nothing drifts either way: the last baseline of five of the six pages is exactly Word's and the
sixth is one step from it.

**How that rounding goes** is the last part of it, and it is not to the nearest step. Measured over
the same sweeps — 121 heights of up to thirty-two lines apiece — rounding to the nearest agrees with
Word on 84% of the lines under the first, and **rounding down from five twelfths of a step above**
agrees on 89%. Five twelfths is a fitted constant and nothing here derives it, so it was checked at
sixty-one heights the fitting never saw, where it agrees on 92% against the nearest's 84%. What is
left over is a last step that no rule of the height reproduces: not the font's doing either — the
same twenty lines set in twenty-four point Verdana land exactly where the twelve point Times ones do
— and about one line in ten under the first comes out a step from Word's, never further.

### A character named by its code

`w:sym` is how Word writes a tick, an arrow, or anything else from the symbol faces: the run names
a face and a code rather than carrying the character itself. Two things about it are worth stating,
both measured from the `symbols` fixture against Word's export:

- **The face belongs to the character, not to the run.** A run may carry text in one face and end
  with a character from another, which is what Word writes when a symbol is typed at the end of a
  word — so the symbol brings its own face and its own line box, and a Wingdings character in a
  line of Times makes the line as tall as Wingdings asks for.
- **The code is written in the private-use block** those faces keep their glyphs in — the tick of
  Wingdings is `F0FC` — and Word's own export strips the block back off, writing the character as
  `00FC`. It does the same with a code that never had the block on it, so `F0FC` and `00FC` come
  out identically, and both are read that way here.

Every symbol of the fixture reaches the page in the face Word set it in, at the width Word gave it
to a hundredth of a point, with the text either side falling where Word puts it.

### Which rows repeat at the top of a page

A table that runs past the foot of a page writes its heading rows again at the top of the next, and
which rows those are is four questions rather than one. `table-heading-probe` puts all four to Word
— four tables, each long enough to break, every row saying in its own text what it is:

| the table | Word's second page begins |
|---|---|
| one row marked `w:tblHeader` | that row, then the rest |
| the first two marked | both of them, then the rest |
| only the *third* marked | the rest, with no heading at all |
| every row marked | the rest, with no heading at all |

So a heading is the run of marked rows at the **top** of a table, and nothing else: a row marked
further down is not one. The last case is the one worth writing down, because the obvious reading
of the format — repeat whatever is marked — would repeat a table of headings for ever, and Word
declines to repeat any of them rather than looping. A heading that would fill the page it is
repeated on is left out for the same reason.

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
pictures), charts of columns, bars, lines, pies, areas and scatters (the plot area where the chart places it
or, where it does not, worked out from the room the labels need — including the room a label that
has to wrap takes; bars standing or lying, clustered, stacked or
stacked to the whole, sized by the gap and the overlap between them, a line curved through its
points the way Word curves one or straight where it says so, areas filled down to the axis or
banded one on another, markers of nine shapes at the size and on the grid Word draws them, a pie
centred and divided clockwise from the top; gridlines, both axis lines and their marks, and the
labels along each axis in the number format it asks for, a title over the chart and one on each
axis, a legend on any side, and a number at every point,
read from the numbers the chart part caches, with the axis scaled and the plot placed the way Word
does both where the chart leaves them to be worked out), equations (fractions stacked, slanted and
written on one line; superscripts and subscripts, alone and together, before what they belong to as
well as after; radicals with and without a degree; brackets of any character, grown to fit what they
hold out of the shapes the face keeps for the purpose; sums, integrals and the rest of the n-ary
operators with limits beside them or above and below; functions, matrices, aligned arrays, accents
and bars — set from the face's own `MATH` table, in the mathematical alphabets Unicode keeps for the
purpose, at the size of the text carrying them, on a line of their own where the markup says so), diagrams — SmartArt — drawn from the arrangement the document keeps of them (every shape
with its geometry, its fill and outline in colours named outright, by theme slot or as percentages,
and its text laid out into the rectangle the diagram set aside for it and set at its top, middle or
foot), watermarks of both kinds (a word set across every page of a section, behind the text,
stretched to fill the shape that holds it, turned, and painted see-through — which is a graphics
state of its own, since a PDF carries transparency there rather than in the colour; or a picture,
washed out to the contrast and brightness it carries), text boxes and the shapes they are a kind of, in both spellings (rectangles, rounded rectangles, ellipses
and triangles drawn as themselves and every other preset geometry as the rectangle it is bounded
by; filled and outlined in a colour named outright or taken from the theme; in the line of text or
anchored with the text flowing round them; and holding a document of their own — paragraphs and
tables, laid out into the box, clear of its insets and its outline, and set at its top, middle or
foot as it asks. A shape arrives wrapped in the compatibility element that offers the same drawing
twice over, once for a newer reader and once for an older; the newer is read and the older passed
over, so it is drawn once), table styles (all thirteen conditional formats — the whole table, the banding across
its rows and down its columns, its first and last rows, its first and last columns and its four
corner cells — resolved through the style's `basedOn` chain, gated by the table's `w:tblLook` in
either spelling, and giving a cell its borders, its shading, its margins, its alignment and the
formatting of the text inside it, with everything the table declares for itself winning over all of
it; a `TableGrid` table from a real document is ruled where Word rules it, which before this it was
not, having no borders drawn at all), tables (fixed and autofit column sizing, horizontal spans, borders, shading,
cell margins and vertical alignment, rows kept whole across page breaks), page size and margins
from `sectPr`, paragraphs and runs, `xml:space` handling,
line and page breaks, line breaking by the Unicode algorithm (so the scripts written without
spaces — Chinese, Japanese, Thai, Lao, Khmer, Burmese — wrap where Word wraps them), tabs
(left, centre, right, decimal and bar stops, with leaders), font family via theme resolution, size, bold,
italic, underline, strikethrough, colour, highlighting, caps, super/subscript, character spacing
and scaling, the background behind a paragraph or a run (`w:shd`, patterns included),
alignment including justification, indents including hanging, spacing before/after with
contextual spacing, line spacing (auto/exact/at-least), pagination, real font metrics with
`.ttc` support, and Type0/CIDFontType2 embedding with a `ToUnicode` map so text stays selectable.

Ten kinds of chart are drawn: **columns, bars, lines, pies, areas, scatters, doughnuts, bubbles,
radars and stock charts**, the bars and areas clustered, stacked or stacked to the whole. What is
there: the plot area, the bars or the line or the slices or the filled areas or the rings or the
bubbles or the web, the markers at a series' points, the lines a stock chart draws between its
series, the gridlines, the two axis lines and the marks along them, and the labels along both axes
whichever way round they run —
with the numbers read from the cache the part carries rather than from the workbook stored beside
it, and with the axes scaled and the plot placed the way Word does both where the chart leaves them
to be worked out; a title over the top and one on each axis, the one up the side turned on its end;
a legend on any of the four sides; and a number written at every point, in the format the chart asks
for and where the kind of chart puts it. What is not: trendlines and error bars, drop lines,
three-dimensional charts of any kind, and a legend or a title placed by hand rather than by side.

Both spellings of a shape are read: the `w:drawing` Word writes today and the `w:pict` it wrote
before 2007 and still writes for a watermark. The older one says in a CSS-like `style` attribute
what the newer says in elements — its size, its position, what it is anchored to — names its
geometry by the element rather than by an attribute, and defaults its fill to white and its
outline to three quarters of a point of black. Where a document offers both, in a compatibility
wrapper, the newer is read and the older passed over so the shape is drawn once.

What a shape does not do yet: turn (a rotated shape is drawn square), resize itself or its text to
fit the other (a box holding more than it has room for overflows, which is what `noAutofit` asks
for and what Word does with that setting), fill with anything but one flat colour, or carry a
shadow.

Watermarks are drawn, of both kinds: the word, in the face and colour and half-solidity it asks for,
turned the way it asks to be turned; or the picture, washed out to the gain and black level it
carries. Both go behind everything else on every page of their section. A picture in a running head
is read from that part's own relationships, so a header and a body may number their pictures alike
— which they routinely do, both calling their first one `rId1` — without either drawing the other's.

Table autofit is the one piece here that approximates rather than reproduces. Word's algorithm is
undocumented; ours measures each column's minimum (widest word) and maximum (unwrapped) width and
shares out the available space between them. It reproduces both behaviours that were measured —
content-width columns when the table fits, and a table filling the text area exactly when it does
not — and agrees with Word to 0.16pt on `table-autofit-probe`, but it is not derived from the real
algorithm the way the paragraph rules are.

A **declared cell width** (`w:tcW`) enters it as the width the column would like, which
`table-width-probe` measures five ways and this now follows exactly: widths that fit are taken as
they stand (72, 108 and 144 points come out as those); a column whose content will not fit the
width it asks for grows to hold it while its neighbours keep theirs (36/36/36 with an unbreakable
word in the middle comes out 36/142.56/36); widths adding to more than the measure are scaled down
together (three of 200 come out three of 156); a column asking for nothing is sized by its own
content beside ones that ask; and where two rows ask for different widths of one column, the wider
wins. Before this the declaration was ignored outright, which put a table of three declared columns
300 points from Word's.

A width asked for as a **share of the table** (`w:type="pct"`, in fiftieths of a percent) is a
share of whatever the table's own width came to, which `cell-percent-width-probe` settles seven
ways. Of a table stating its width in points, the share is of that; of a table stating its width as
a share of the measure, it is of the measure through it. Of a table stating nothing there is
nothing to take a share of but the contents, and Word makes such a table **as narrow as the shares
allow** — a quarter, a half and a quarter round a letter each come out 5.28, 10.8 and 5.28, the
narrowest table at which a quarter still holds its letter — capping that at the measure, so the
same table with a column of text in its middle cell fills the 468 points instead. Shares falling
short of the whole are stretched to fill it and shares beyond it are taken in order until it is
spent; a share beside a stated 72 points and a column asking for nothing comes out 162, 72 and 90,
the share taken first, the statement kept, and the remainder going to the column that asked for
nothing.

**Every column edge is on the grid**, which is what makes those numbers come out as they do:
Word's quarter of 324 points is 81.12 and the next quarter 80.88, because 81 and 243 land either
side of a step. It is the edges that are snapped and not the widths, so three columns of one
declared width need not be equal — `column-grid-probe`'s three fifty-point columns, fifty points
being 208 steps and a third, come out 49.92, 50.16 and 49.92 in Word and now here. Five of that
probe's six pages are identical to Word's: declared widths, awkward ones, a stated grid under a
fixed layout, widths scaled down to fit the measure, and halves falling the other side of a step.

All six pages are Word's exactly, and so is every column of the three other table probes. Getting
the sixth — the one whose columns are sized by their contents — took two things measured elsewhere:
that a cell's content width is rounded **up to a whole twip** before anything is shared out, and
that what a cell's text is broken against is the width the arithmetic gave rather than the width
that was drawn. A column is drawn on the grid and its text broken against the exact width, which is
why a word can end a fraction past the column it sits in.

### How far a word may overrun before it is broken

Not at all. `break-tolerance-probe` moves the measure a twip at a time — a twentieth of a point,
five times finer than the grid — past the width of a word with nowhere to break: ten capital Ms of
Times at twelve point, which are 106.6992pt wide. A measure of 106.7 holds them; 106.65 breaks
them, nine and one. A table cell is no different, and neither is a page.

That answers a question two earlier probes had raised the other way. A word had seemed to survive
in a column a tenth of a point too narrow for it, which looked like tolerance and was not: the
column was **drawn** on the grid while its text was broken against the width the arithmetic gave.
Carrying both — the snapped widths for the drawing, the exact ones for the breaking — is what makes
every column of the table probes come out Word's.

### How wide Word thinks a piece of text is

The font's own advances at the font's own resolution, and nothing else. `text-measure-probe` sets
every line against the right margin, so where a line begins is the margin less the width Word
measured, and repeats the same letter up to forty times so a single rounding is divided by forty.
Over eighty lines — Times at eleven, twelve and thirteen and a half points, and Arial at twelve —
every one of ours begins **exactly** where Word's does, the worst a ten-thousandth of a point.

The probe also lays a trap this repository walked into. A PDF records the widths it draws with in
thousandths of an em, so reading Word's own export back gives 444 thousandths for Times 'a' — 5.328
points at twelve. Word did not measure it as 5.328: the font says 909 units of 2048, which is
5.32618. Two hundredths of a point a letter is nothing on a line and a whole step of the grid
across a table column, and two commits here blamed a column that was a step out on "our measure
running a hair above Word's". It runs exactly with Word's. What is left over in a table column is
the column, not the text: Word's own page says it rounds what a *cell* wants up to a whole twip
before dividing, and that it will keep a word in a column a fiftieth of a point too narrow for it
rather than break it. Neither is implemented here, and both are written down in the backlog with
their numbers rather than guessed at.

**A table's own stated width** (`w:tblW`) is met exactly, and `table-preferred-width-probe` settles
five things about it: the width is taken whether it is wider than the contents want or narrower; a
share is a share of the **measure**, so half of a 468 point column comes out 234; a width narrower
than the contents wraps them; a width wider than the page is **not** brought back to it — Word
writes such a table straight off the paper's edge, and so does this; and the width is divided in
proportion to what each column wants, each want being its content rounded up to a whole twip.

Five of the probe's seven pages come out exactly Word's. The two that do not are the same shape —
three columns of nearly equal content — and what separates them from Word is the smallest thing in
this file. Word's first edge falls at or past 2076 twips of the 6480 the table asks for and its
second short of 4404; dividing in proportion puts them at 2075.93 and 4404.07, **seven hundredths
of a twip** outside each, and each lands on the far side of a rounding boundary. Three thousandths
of a point, one part in ninety thousand of the table, and a grid step in the drawing. Wants of
content plus a twip land that page and throw another; equal sharing, proportional-to-minimum, a
constant per cell and any blend of proportional and equal sharing are each ruled out by a page they
break. It is written up in `TablePreferredWidthTests` with the arithmetic rather than papered over.

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
remains elsewhere is Hangul, whose syllables are composed rather than shaped, and the one kind of
Apple attachment that names points on the outlines themselves rather than in a table — no face on
this machine asks for it.

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

For Apple's tables the same comparison holds: twenty-six words in ten faces for the shaping and
thirteen more in eleven for the placing, and Word agrees about the page on every line of the fixture
to four hundredths of a point. Three things are kept out of it because Word does something else with
them — or nothing at all. Asked for a line of Malayalam in a face whose positioning is Apple's, its
export holds nothing where the line should be. Asked for Thai in Thonburi, it draws the line in a
font of its own. And for one Devanagari cluster its reading of the same table comes out two points
wider than HarfBuzz's. Which of the two Apple's own engine agrees with is not a question this
machine can answer, so the difference is recorded rather than resolved.

Reading those faces turned up a fault of ours that had nothing to do with shaping. They carry their
family name several times over in several languages, and the first record of the right kind is not
the English one: Gujarati MT calls itself ગુજરાતી એચટી. A document naming "Gujarati MT" then matched
nothing at all, and its text was drawn in whichever face was borrowed for it. The English record now
wins.

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

Not every face describes its shaping in OpenType. A hundred and sixty of the ones on this machine
carry Apple's `morx` table and no `GSUB` at all: Devanagari MT, Gujarati MT, Gurmukhi MT, Thonburi,
Geeza Pro, Corsiva Hebrew, and the whole of Helvetica, Palatino and Optima. A converter that reads
only OpenType draws those scripts as rows of unjoined letters, and there is nothing in the file to
fall back on.

It is the older idea and the more general one. Where OpenType says "these glyphs in this company
become those glyphs", this says "in this state, a glyph of this class takes you to that state, and
on the way you may mark this one, swap that one, or write several as one" — one machine expressing
what OpenType needs four kinds of lookup for, and two more besides: rearranging glyphs, and
inserting ones the text never held. All five kinds are read here, and the reading is used only where
the font has no OpenType tables. A face carrying both carries the same shaping twice, and the
OpenType half is the one every other reader of the file will use.

Where those faces go on to say how far apart the glyphs go, they say that in Apple's tables too, and
those are read as well. Two quite different things live in the one table. Most of it is kerning — by
naming pairs, by naming classes of glyphs, or by a machine that keeps a stack of what it has passed
and moves several of them at once — and it is applied only where the document asks for kerning, as
everywhere else here. The rest is attachment: a machine that marks a letter and fastens what follows
to it by naming a point on each out of a table of anchors, which is how these faces put a vowel sign
on a consonant. That is applied always, because a mark that is not fastened is not merely unkerned
but in the wrong place.

Its kerning is shared between the two glyphs rather than taken out of the first one's advance: half
moves the glyph on the left and half moves the one on the right, which is drawn half a kern along as
well. OpenType expresses the same thing the other way, as a shortening of the left glyph alone. The
two give a pair the same width and put the second glyph in different places, and each face is drawn
against one of them.

Two things about it cost an afternoon each. A machine that writes several glyphs as one leaves the
others behind marked as gone, and they have to be swept up before the next machine runs rather than
at the end — left in place, they sit between the pair the next machine is looking for and the run
comes out with its shapes half made. And which way round a subtable reads the run is decided by a
flag saying so, not by comparing the flag that says "logical order" against the direction of the
text: getting that wrong shapes Arabic with every letter's join one place out.

Where a line may be broken is not a question about spaces. Chinese and Japanese are written with
none at all and break between one character and the next; Thai, Lao, Khmer and Burmese have none
between their words either; and even English has spaces that may not be broken at — the one in
"10 kg" — and places without a space where a break is allowed, as after a hyphen. A converter that
looks for spaces draws a line of Japanese straight off the edge of the page, which is what this one
did.

The Unicode line breaking algorithm decides it from a property of each character and a list of rules
about pairs, applied in order, the first that matches winning. The property is generated from the
database by `tools/make-linebreak-tables.py`, and the rules are checked against the file Unicode
publishes for exactly that purpose: 7310 of its 7654 cases, every one that does not turn on a script
needing a dictionary, and all of them pass. Half of what that file caught was rules read backwards
or applied to the wrong side of a pair — no break *before* an opening bracket rather than after one,
glue looked for past the spaces rather than beside them — and the sort of thing no amount of reading
the text again would have shown.

The 344 it does not answer are the scripts written without spaces *and* without a break between
every character. For those the algorithm says to consult a dictionary of the language, which this
converter has not got. What it does instead is what Word does with the same paragraph: break between
one syllable and the next. The marks are folded into the letters they are written on, along with the
handful of Thai and Lao vowels that are letters in their own right but sound after a consonant and
cannot begin a line; the four vowels those two scripts write *before* the consonant they are sounded
after keep hold of it, as does the Khmer sign that turns the letter following it into a subscript;
and what is left standing is a letter that may begin a syllable, and so a place a line may be
broken. It is a coarser answer than a dictionary would give — more places than a Thai reader would
choose — but every place it offers is one, and a greedy filler picks the last that fits.

Which is where the measurement is. Given the `wrapping` fixture — a paragraph of Japanese, one of
Chinese and one of Thai, none with a space in it — Word and this converter break all three into the
same lines, to a hundredth of a point in width, kinsoku and all: no line begins with a full stop, a
comma or a closing bracket, and the Thai breaks where Word breaks it, in the middle of a word.

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

Seven of them: `smartart` and `smartart-lines`, whose cached drawing is only worth comparing when
Word wrote it; `report` and `memo`; `newsletter`, a running head and a footer that counts the pages
over a body in two columns, with an address that lives only in a relationship; `notes`, whose
footnotes and endnotes are in parts Word wrote, separators and all; `minutes`, whose numbering part
Word rewrote wholesale; and `brochure`, a picture and a text box, where Word writes the drawing
twice over — the modern markup and a VML fallback beside it.

`brochure` is what the real documents are for. Every fixture written by hand sets its line spacing
to a single line, and Word's own Normal asks for 1.08 — so nothing here had ever put a picture on a
line that asked for a multiple. Word applies the multiple to the line the *text* would have made
and leaves the picture out of it, where we multiplied the whole line box and left a 96pt picture
6.8 points too low. `image-line-probe` measures it sixteen ways and `ImageLineTests` holds it.

`tools/make-real-fixtures.sh` --list shows what would be generated. Add to `RealSeeds` to cover
more; third-party templates are best avoided, since their licence terms would come with them.

## Licence

MIT. See [LICENSE](LICENSE). The package carries it, and `LibraryInvariantTests` asserts that what
the package says and what the repository holds are the same thing.

The fixtures are written by this repository rather than taken from anywhere, so nothing here comes
with terms of its own — which is why third-party templates are kept out of `Fixtures/Real`.
