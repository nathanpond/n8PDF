# n8PDF

Converts `.docx` to PDF, written from scratch: no third-party DOCX or PDF library, no headless
Word, LibreOffice, browser engine or sidecar. A consumer adds one assembly reference and calls
one method.

`README.md` is a short landing page — mission, install, security, wiki links, licence. The full
account lives in the **GitHub wiki** (`nathanpond/n8PDF.wiki`, a separate git repo — clone or read
it on the web). Open the page you need:

| Wiki page | For |
|---|---|
| Functionality / Known Gaps | **what is implemented** — check here before calling something missing |
| Matching Word | the fidelity method — how closely it matches, and where the tolerances are |
| The API | the eight public types and what a version promises |
| Architecture | the directory map and the direction data flows |
| Validation | what the suite proves, and the external checkers |
| Security / Threat Model | the attack surface and what is defended |
| Installation / Prerelease Considerations | getting it from NuGet, and what pre-1.0 means |

The wiki pages summarise; the deepest detail (the full fidelity measurements, the exhaustive scope
list) was in the README's history before it was slimmed and is recoverable from git if needed.

## Shape

.NET 10. One shipping library, one test project.

```
src/n8PDF/          Converter.cs is the API; everything else is internal
  Packaging/        OPC container: zip, content types, relationships
  Ooxml/            WordprocessingML model and parsers, plus Units
  Styling/          the formatting cascade, producing Resolved*Format
  Fonts/            SFNT parsing, metrics, resolution, shaping (+ OpenType/, Aat/)
  Text/             the bidirectional algorithm and its Unicode tables
  Images/           PNG, GIF, BMP, TIFF, EMF and JPEG decoding
  Layout/           measurement, line breaking, page composition, list counters
  Pdf/              object model, writer, content streams, Type0 embedding
  Diagnostics/      LayoutTrace — the testing spine
tests/n8PDF.Tests/  fixtures (Minimal, Real, Reference) and committed Golden traces
tools/              generators for the Unicode tables and the reference PDFs
```

Data flows one direction: **Packaging → Ooxml → Styling → Layout → Pdf**, with `Fonts` serving
Layout and Pdf. Word's page origin is top-left and PDF's is bottom-left; the flip happens once,
in `PdfRenderer`, and nowhere else.

## Invariants

These are load-bearing. Do not breach one without being asked to, by name.

- **`src/n8PDF` carries zero `PackageReference` entries.** `LibraryInvariantTests` fails the
  build if that changes. Never solve a problem in the library by taking a dependency —
  `System.IO.Compression` and `System.Xml.Linq` are the whole of what is available. A task that
  seems to need a package needs a conversation instead. Semgrep, like qpdf and the other checkers,
  is a tool the CI runner installs, not a package the library references — the static-analysis
  workflow leaves this invariant untouched.
- **The public surface is eight types.** `PublicApiTests` writes it out in full and fails on
  anything that grows it. Making a type or member public is a deliberate act with a diff to show
  for it; propose it, do not simply do it.
- **Warnings are errors**, from `Directory.Build.props`, for every project and every build. While
  working you may pass `-p:TreatWarningsAsErrors=false`, but never commit or close an issue in
  that state.
- **Generated tables are generated.** The Unicode tables under `Text/` and `Fonts/` are the
  output of `tools/make-*-tables.py`. Fix the generator and re-run it; never hand-edit its
  output, and never add a one-off exception in the consuming code for something the table got
  wrong.
- **`tools/` means build-input generators.** Everything in it produces something the project
  consumes. Do not put general-purpose scripts there.

## Running

```
dotnet build n8PDF.sln --configuration Release -warnaserror
dotnet test  n8PDF.sln --configuration Release
```

The suite leans on three second opinions, each an implementation with nothing in common with
this one: **qpdf** validates the cross-reference tables and object graphs, **fontTools** reads
the subset fonts back, **FriBidi** resolves the bidi algorithm its own way. They are optional
locally and required in CI. If they are installed here, run with them on, because CI will:

```
N8PDF_REQUIRE_QPDF=1 N8PDF_REQUIRE_FONTTOOLS=1 N8PDF_REQUIRE_FRIBIDI=1 \
  dotnet test n8PDF.sln --configuration Release
```

46 of the 143 comparison fixtures are set in faces Word brings with it. A hosted runner has not
got them, so `ci.yml` skips those and says so out loud; `full.yml` would run them but has no
runner and is started by hand. On a Mac with Word installed, a plain `dotnet test` covers all
143 — which makes that machine the only place the full comparison actually runs. Bear that in
mind before trusting a green CI on anything touching font selection or metrics.

`artifacts/` is gitignored test output — converted PDFs and diff images for eyeballing, not
assertions.

One more checker reads the source rather than the output: **Semgrep** runs as its own workflow
(`.github/workflows/semgrep.yml`) on every push and PR, flagging the shapes the audit keeps
re-finding — unchecked casts, allocation off the wire, XML on the framework's DTD defaults, an
additive length bound that overflows. It is advisory (findings go to code scanning, not to failing
the build) and installed on the runner, not referenced by the library. Its repo-specific rules and
the suppression policy are in `.semgrep/README.md`; run it locally with
`semgrep scan --config .semgrep --config p/csharp --config p/security-audit --config p/secrets src/`.

## Backlog

The backlog is **GitHub Issues** on `nathanpond/n8PDF`, not memory files, not a section of the
README, not a TODO list. If a piece of work is worth remembering, it is an issue. Read and write
it with `gh`.

Issues are server-side and independent of local git state. You do **not** need to push, commit,
or have a clean tree to read or file them. The repo only needs its GitHub remote, which it has.

### Vocabulary

- **Severity** (findings): `sev:critical`, `sev:high`, `sev:medium`, `sev:low`
- **Source**: `security`, `audit`, `tech-debt`, `needs-triage`, `blocked`, `epic`,
  `documentation`
- **Kind**: `feature` — a capability that does not exist yet, as against a defect in one that
  does. It carries no severity: a thing that was never built cannot be a `sev:` of anything, and
  what an absent feature costs is a judgement about the product rather than a measurement of a
  failure. Distinct from `tech-debt`, which is about code that exists being harder to work with
  than it should be.
- **That list is complete.** GitHub's stock labels (`bug`, `enhancement`, `duplicate`,
  `invalid`, `question`, `wontfix`, `good first issue`, `help wanted`) were deleted rather
  than left to sit alongside it: `bug` and `enhancement` overlap the scheme above, and a
  severity signal split across two vocabularies is worse than either one alone. `feature` is not
  `enhancement` returning under another name — `enhancement` was deleted for straddling defects
  and absences, which is the distinction this one exists to draw.
- **No `--type`.** Issue types are an organization-level setting and `nathanpond` is a user
  account, so `Bug`/`Task`/`Feature`/`Epic` are not available here. The labels above carry the
  same information. Sub-issues (`--parent`) and dependencies (`--blocked-by`) are *not*
  org-gated and do work — use them.
- **Never invent a label.** `gh label list` first. If one is missing, ask.

### Filing

1. **Search before filing.** Duplicates are the main failure mode of a repeated audit.
   ```
   gh issue list --state all --search "<distinctive terms>" --json number,title,state
   ```
2. Every audit-filed issue ends with a stable fingerprint so the next run can find it exactly:
   ```
   <!-- fingerprint: <rule-id>|<relative/path.cs>|<type-or-member> -->
   ```
3. A matching **open** issue → comment the new evidence, do not file again.
   A matching **closed** issue → `gh issue reopen` and comment that it regressed.
4. One finding per issue. Never batch.
5. Body: what, where (`path:lines`), why it matters, reproduction or evidence, suggested fix.
   For anything in a parser or decoder, "why it matters" states what a hostile `.docx` gets out
   of it — see the audit command for why that framing is the right one here.

### Working an issue

1. `gh issue view <n> --comments` — all of it, including comments and linked issues.
2. `gh issue develop <n> --checkout`. Use this rather than naming a branch by hand; GitHub
   records the link and it survives across machines. `ci.yml` runs on every branch, so pushing
   gets you the hosted tier for free.

   **A linked branch closes its issue when a PR from it merges.** That is GitHub's doing, not a
   keyword's, and nothing in the commit or the PR body can prevent it — see the note under step 4.
3. Comment a one-line plan on the issue **before** writing code.
4. Implement. Reference the issue in commits as `Refs #<n>` — **never** `Fixes #<n>` or any
   auto-closing keyword. Those only fire on the default branch, so on a feature branch they
   silently do nothing.

   That rule is about keywords and it does **not** cover the other way an issue closes itself.
   `gh issue develop` records the branch as a *linked branch*, and GitHub closes the linked issue
   the moment a PR from that branch merges — no keyword involved, and `Refs #<n>` will not save
   you. The issue's timeline shows it as a `connected` event when the PR opens and a `closed`
   event at the merge.

   So **merging work that does not finish an issue will close it anyway.** When that happens:

   ```
   gh issue reopen <n> --comment "Reopened: closed by the linked branch on merge, not by me. <what is left>"
   ```

   Check it after every merge — `gh issue view <n> --json state` — rather than assuming, because
   the close is silent and an issue wrongly marked done is worse than one left open. Partial work
   is normal on the harder stories and is the case this bites.
5. Verify: the build is warning-clean and the suite passes, with the three checkers on. A change
   to layout, metrics or font handling needs its golden trace reviewed, not merely regenerated —
   a golden updated to match new output proves nothing.
6. **Push before closing.**
   ```
   gh issue close <n> --reason completed \
     --comment "Done in <branch> @ <sha>. <what changed>"
   ```
   Issue state syncs across machines instantly; unpushed code does not. A closed issue with no
   code on the remote is a lie told to the other computer.
7. Cannot finish? Leave it open, comment what is blocking,
   `gh issue edit <n> --add-label blocked`.

### Plan mode

Plan output is issues, not prose in the transcript. On approval:

- One parent issue for the effort, labelled `epic`; each unit of work a child created with
  `--parent <n>`.
- Every child carries **acceptance criteria** as a checklist. If they cannot be written as
  something testable, the story is too vague — split it or ask.
- Encode ordering with `--add-blocked-by`, not prose.
- Stories small enough that one fits in a single session.
- A feature story says which fixture proves it. Work that cannot be demonstrated against a
  fixture or a golden trace should say so explicitly and explain how it will be checked instead.

### Never

- Never close an issue you did not personally verify as done.
- Never close an issue as part of a bulk cleanup unless asked.
- Never touch issues you did not create unless directed to that number.
- Never use `--reason "not planned"` on your own initiative — that is a human's triage call.
