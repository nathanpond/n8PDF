# Contributing to n8PDF

n8PDF converts `.docx` to PDF from scratch — no third-party DOCX or PDF library, no headless Word,
no browser engine, no sidecar. That constraint is the project, and it shapes what a contribution can
be. This page is the short version; the [Developers](https://github.com/nathanpond/n8PDF/wiki/Developers)
wiki page has the full account of building, testing, and the invariants.

## The fastest way to help

You do not have to write code. The project's method is measuring against Word, so **a real document
that n8PDF gets wrong is worth as much as a fix** — it becomes a test that guards the fix forever.
Open a [bug report](https://github.com/nathanpond/n8PDF/issues/new?template=bug_report.yml) with the
smallest `.docx` that shows the problem (rebuilt with placeholder text, never a confidential
document) and Word's own PDF export of that same file.

Found a document that exhausts memory, hangs, or crashes the process? That's a security report —
send it privately through the repository's **Security** tab → **Report a vulnerability**, not as a
public issue. See [SECURITY.md](SECURITY.md).

## Building and testing

```bash
dotnet build n8PDF.sln --configuration Release -warnaserror
dotnet test  n8PDF.sln --configuration Release
```

The suite leans on three independent second opinions — **qpdf** (cross-reference tables and object
graphs), **fontTools** (reads the subset fonts back), and **FriBidi** (resolves the bidi algorithm
its own way). They are optional locally and required in CI. If they're installed, run with them on,
because CI will:

```bash
N8PDF_REQUIRE_QPDF=1 N8PDF_REQUIRE_FONTTOOLS=1 N8PDF_REQUIRE_FRIBIDI=1 \
  dotnet test n8PDF.sln --configuration Release
```

Note that the full Word-comparison suite (143 fixtures) only runs on a machine that has Word
installed; hosted CI skips the 46 fixtures set in faces Word ships. A green CI is not the whole
story for anything touching font selection or metrics — see the
[Validation](https://github.com/nathanpond/n8PDF/wiki/Validation) page.

## The invariants — load-bearing, do not breach without being asked

- **`src/n8PDF` carries zero `PackageReference` entries.** `LibraryInvariantTests` fails the build if
  that changes. `System.IO.Compression` and `System.Xml.Linq` are the whole of what's available; a
  task that seems to need a package needs an issue and a conversation instead.
- **The public surface is exactly eight types.** `PublicApiTests` writes it out and fails on anything
  that grows it. Making a type or member public is a deliberate act with a diff to show for it —
  propose it in an issue first.
- **Warnings are errors**, for every project and every build. You may pass
  `-p:TreatWarningsAsErrors=false` while working, but never commit in that state.
- **Generated tables are generated.** The Unicode/font tables under `Text/` and `Fonts/` are the
  output of `tools/make-*-tables.py`. Fix the generator and re-run it; never hand-edit its output.
- **A layout, metrics, or font change needs its golden trace reviewed, not merely regenerated** — a
  golden updated to match new output proves nothing.

## Working an issue

The backlog is [GitHub Issues](https://github.com/nathanpond/n8PDF/issues), nothing else. To pick one
up:

1. Comment a one-line plan on the issue before writing code.
2. Branch from the issue with `gh issue develop <n> --checkout` (GitHub records the link, and CI runs
   on every branch).
3. Reference the issue in commits as `Refs #<n>` — **never** `Fixes`/`Closes`/`Resolves #<n>`. Those
   auto-close keywords only fire on the default branch and silently do nothing from a feature branch.
4. Open a PR (the template walks the verification checklist). PRs are squash-merged.

New contributors: issues labelled with a low `sev:` or a small `feature` are the gentlest entry
points. If something is unclear, ask on the issue — a question is cheaper than a wrong guess.

## Licence

By contributing, you agree that your contributions are licensed under the [MIT License](LICENSE),
the same terms as the project.
