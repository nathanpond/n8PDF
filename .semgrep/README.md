# `.semgrep/` — repo threat-model rules and scan policy

These rules encode the shapes the audit keeps re-finding in n8PDF's from-scratch parsers. They run
in CI (`.github/workflows/semgrep.yml`) ahead of the registry shortlist (`p/csharp`,
`p/security-audit`, `p/secrets`), and each ships a passing+failing fixture beside it —
`semgrep --test --config <rule>.yaml <rule>.cs` is green for every one.

| rule | flags | historical finding |
|---|---|---|
| `unchecked-round-cast` | `(int)Math.Round(document value)` with no range guard — wraps to `int.MinValue` | #148, #204 |
| `alloc-sized-by-unbounded-read` | an array sized straight from a `Read*()` call, no bound | unbounded-allocation tier (preventive) |
| `xml-read-without-hardened-settings` | `XDocument.Load`/`Parse`/`LoadXml` not routed through a DTD-prohibited `XmlReader` | OpcPackage hardening |
| `additive-length-bound-overflow` | `pos + count > buf.Length` with a variable addend — overflows and passes | #181 |

## Running locally

```
semgrep scan --config .semgrep --config p/csharp --config p/security-audit --config p/secrets src/
semgrep --test --config .semgrep/unchecked-round-cast.yaml .semgrep/unchecked-round-cast.cs   # per rule
```

## Baseline (diff-aware on PRs)

The scan is **advisory**: findings upload to Security → Code scanning and never fail the build (until
#234 flips high/critical to blocking). `main` is not clean under the custom rules — it carries a
standing baseline of real instances that predate the rules. Rather than freeze that into a baseline
file, PR scans are **diff-aware**: the workflow passes `--baseline-commit <PR base>`, so a PR's
diff-aware step reports only findings on the code that PR changed. The standing baseline is worked
down through normal triage, not suppressed en masse.

## Suppression policy

Inline suppression is allowed but narrow, in keeping with the repo's "no one-off exceptions" ethos
(CLAUDE.md):

- **Name the rule and give a reason.** The only accepted form is
  `// nosemgrep: <rule-id> — <reason>`. The rule id makes the suppression specific; the reason makes
  it reviewable.
- **No blanket `// nosemgrep`.** A bare suppression silences *every* rule on the line and hides
  whatever lands there next. It is not accepted in review.
- **A suppression is a claim, checked like any other.** "This `(int)Math.Round` is bounded because
  the value is a page count ≤ 10⁴" is a reviewable statement; "false positive" alone is not. If the
  safe form is cheap — route through `SafeInt`, subtract instead of add — prefer fixing over
  suppressing.
- **Reviewer expectation.** A PR that adds a suppression explains why in the PR body, not only the
  comment. A suppression without a rule id or without a reason is a change request.

The generated Unicode tables under `Text/` are generator output and are excluded from the scan
entirely (a hand-fix there is wrong, per CLAUDE.md); they are never suppressed line-by-line.
