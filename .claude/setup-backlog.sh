#!/usr/bin/env bash
# Bootstrap GitHub Issues as the backlog for this repo.
#
# Run once per repository, and once on any machine that has not authenticated gh. It is
# idempotent: --force on every label means re-running only reconciles colours and descriptions.
#
# See the Backlog section of ../CLAUDE.md for what the labels mean and how they are used.
set -euo pipefail

need="2.94.0"

echo "==> gh"
command -v gh >/dev/null || {
  echo "    gh is not installed."
  echo "    brew install gh"
  exit 1
}
have=$(gh --version | head -1 | grep -oE '[0-9]+\.[0-9]+\.[0-9]+')
if [ "$(printf '%s\n%s\n' "$need" "$have" | sort -V | head -1)" = "$need" ]; then
  echo "    $have"
else
  echo "    $have — sub-issues, issue types and dependencies need >= $need."
  echo "    brew upgrade gh   (labels below work either way)"
fi

echo "==> auth"
gh auth status >/dev/null 2>&1 || { echo "    Run: gh auth login"; exit 1; }
echo "    $(gh api user -q .login)"

echo "==> repo"
echo "    $(gh repo view --json nameWithOwner -q .nameWithOwner)"

echo "==> labels"
mklabel() {
  gh label create "$1" --color "$2" --description "$3" --force >/dev/null
  printf '    %-14s %s\n' "$1" "$3"
}
mklabel "sev:critical" "b60205" "Memory corruption or arbitrary write from a crafted document"
mklabel "sev:high"     "d93f0b" "Unbounded allocation or hang from a crafted document"
mklabel "sev:medium"   "fbca04" "Wrong output where a clean failure was owed"
mklabel "sev:low"      "0e8a16" "Hardening"
mklabel "security"     "5319e7" "Reachable from untrusted input"
mklabel "audit"        "1d76db" "Filed by an audit run"
mklabel "tech-debt"    "c5def5" "Maintainability"
mklabel "needs-triage" "ededed" "Captured, not yet assessed"
mklabel "blocked"      "000000" "Cannot proceed"
mklabel "epic"         "3e4b9e" "A parent issue; children hang off it via --parent"

echo "==> issue types"
owner=$(gh repo view --json owner -q .owner.login)
if gh api "orgs/$owner/issue-types" >/dev/null 2>&1; then
  echo "    available on $owner — the --type flag works"
  echo "    (the commands do not use it; add --type Bug|Task|Feature|Epic if you want it)"
else
  echo "    not available: $owner is a user account, and issue types are an organization-level"
  echo "    setting. The commands omit --type for this reason. Sub-issues (--parent) and"
  echo "    dependencies (--blocked-by) are unaffected and do work here."
fi


cat <<'NEXT'

==> Ready.
    /audit Images     sweep one area, file what it finds
    /work 12          take an issue end to end on a linked branch
    /file <text>      quick capture

    Start with a narrow /audit before pointing it at the whole library — it is easier to
    tune the dimension list in .claude/commands/audit.md against ten issues than a hundred.
NEXT
