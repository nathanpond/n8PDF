---
description: Capture something as a GitHub issue without losing your place
argument-hint: <one-line description of the bug or task>
allowed-tools: Bash(gh:*), Bash(git:*), Bash(rg:*), Read, Grep, Glob
---

Capture this as a GitHub issue: **$ARGUMENTS**

Be fast. Do not start working on it, and do not investigate beyond finding the anchor.

1. Search for an existing match:
   ```
   gh issue list --state all --search "<key terms>" --json number,title,state
   ```
   If one exists, comment on it rather than filing a duplicate, and say which.

2. Locate the relevant code so the issue has a real anchor — `path:lines`, and the pipeline stage
   it sits in (Packaging, Ooxml, Styling, Fonts, Text, Images, Layout, Pdf). A minute, not ten.

3. File it:
   ```
   gh issue create --title "<area>: <specific problem>" --body-file <tmpfile> \
     --label needs-triage
   ```
   Body: what, where, why it matters, how it was noticed. Add `--label security` and a `sev:`
   label if it is reachable from a crafted document.

4. Reply with the issue number and URL. One line.
