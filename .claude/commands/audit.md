---
description: Audit n8PDF and file every finding as a GitHub issue
argument-hint: [scope — e.g. "Images", "Fonts/OpenType", "Packaging", or blank for all]
allowed-tools: Bash(gh:*), Bash(git:*), Bash(rg:*), Bash(dotnet:*), Read, Grep, Glob, Task
---

Audit and file the results as GitHub issues on `nathanpond/n8PDF`.

**Scope:** $ARGUMENTS
(Blank means the whole library, prioritizing what changed recently:
`git log --since="30 days ago" --name-only --pretty=format: -- src | sort -u`.)

## The threat model, which is the point

n8PDF parses hostile input by design. A `.docx` is a zip full of XML written by someone else,
carrying fonts and images written by someone else again, and every parser that reads it here was
written from scratch with no library between it and the bytes. There is no `System.Drawing`
absorbing a malformed PNG, no `DocumentFormat.OpenXml` bounds-checking a relationship, no
FreeType rejecting a corrupt `loca` table. `Packaging/`, `Ooxml/`, `Images/` and `Fonts/` are the
attack surface, and they are the whole attack surface.

Weight the audit accordingly. A missing null check in `Layout/` is a bug; an unchecked length
prefix in `Images/` is the finding this command exists for.

## Rules

- **File everything before fixing anything.** This command writes no source. Even a one-line fix
  gets filed — the human decides what is worked.
- Follow the Backlog section of CLAUDE.md: search first, fingerprint, one finding per issue.
- Report a finding only if you can state a concrete failure: an input that reaches it and what
  happens. "This could overflow" without a path to it is noise, and noise filed as issues is
  worse than a missed `sev:low`.

## Steps

1. **Orient.**
   ```
   gh repo view --json nameWithOwner,defaultBranchRef
   gh label list --limit 100
   gh issue list --state all --label audit --limit 300 --json number,title,state,body
   ```
   Keep those fingerprints for step 3.

2. **Sweep.** One subagent per dimension when the scope is more than a couple of files.

   **Untrusted input — the priority tier**
   - `Packaging/` — zip bombs and declared-vs-actual sizes, path traversal out of the OPC
     container (`../` in part names), relationship targets pointing outside the package or at
     external URIs, duplicate or contradictory content-type declarations.
   - `Ooxml/` — XML entity expansion and external entity resolution (confirm `XmlResolver` is
     null and DTD processing is prohibited everywhere `System.Xml.Linq` is entered), unbounded
     nesting depth, attribute counts, and element repetition; integer parsing of `w:` measurement
     attributes that trusts the document's arithmetic.
   - `Images/` — every decoder: width × height × bpp multiplied before a bounds check, length
     prefixes trusted against remaining buffer, RLE and LZW runs writing past a scanline, GIF
     and TIFF loops that a crafted file can make unbounded, EMF record lengths, JPEG marker
     lengths. These six decoders are hand-written and each one is a separate audit.
   - `Fonts/` — SFNT table directory offsets and lengths against the file, `loca` entries against
     `glyf`, `cmap` subtable formats and their segment arrays, CFF INDEX offsets and charstring
     interpreter depth and stack limits, composite glyph recursion depth. A subsetter that
     believes a hostile `loca` writes wherever the table says.

   **Correctness**
   - Unhandled or swallowed exceptions across the parse boundary; anything that turns a malformed
     document into a wrong PDF rather than a clean failure.
   - Resource leaks — streams and `MemoryStream`s that escape a `using`, especially in the
     decoders and the zip reader.
   - Boundary arithmetic in `Layout/` where a measurement can be negative, zero, or NaN.

   **Operational**
   - Unbounded allocation or work driven by document-stated values: page counts, column counts,
     list nesting, footnote chains, table dimensions. What is the largest allocation a 10KB
     `.docx` can ask for?
   - Anything that can loop without a bound on malformed input.

   **Repo invariants** (from CLAUDE.md — these are cheap to check and easy to drift)
   - Any `PackageReference` that has appeared in `src/n8PDF`.
   - Public surface beyond the six documented types.
   - Hand-edits to generated tables that `tools/make-*-tables.py` would overwrite.
   - Suppressions — `#pragma warning disable`, `TreatWarningsAsErrors=false` left in a project
     file — that hide what the warnings-as-errors rule is meant to catch.

   Skip generic dependency scanning: `src/n8PDF` has no packages by design. Do check the test
   project's references.

3. **Dedupe.** Compute each candidate's fingerprint (`<rule-id>|<path>|<type-or-member>`) and
   compare against step 1. Open match → comment. Closed match → reopen and note the regression.
   No match → file.

4. **Verify before filing.** Read the surrounding code, not just the pattern match. For a parser
   finding, trace the input from `Converter` down to the offending line and state that path in
   the issue. Discard anything you cannot substantiate.

5. **File.**
   ```
   gh issue create --title "<area>: <specific problem>" --body-file <tmpfile> \
     --label audit --label security --label "sev:<level>"
   ```
   Titles are specific:
   "Images/PngDecoder: IHDR width*height*bpp multiplied before bounds check" —
   not "Potential overflow in PNG handling".

   Severity here: `sev:critical` is memory corruption or arbitrary write from a crafted document.
   `sev:high` is unbounded allocation or a hang from one. `sev:medium` is a wrong PDF where a
   clean failure was owed. `sev:low` is hardening.

6. **Report** as a table — number, severity, area, one line. Then: total filed, total deduped
   into existing issues, and anything deliberately skipped with the reason.

Do not modify source. Do not open a pull request.
