---
name: "worktree-pr-merge"
description: "Patterns for merging PRs sequentially from git worktrees, including rebase timing, dotnet restore requirements, exit code interpretation, and branch push configuration."
domain: "git-workflow"
confidence: "medium"
source: "observed"
---

## Context
Git worktrees enable parallel PR development by creating isolated branch checkouts tied to the main worktree. When merging PRs sequentially to main, each worktree must coordinate its state with the previous merge. This skill covers the recurring patterns that prevent build failures, false exit code errors, and branch push failures.

## Patterns
- **Rebase timing:** First PR in a worktree starts up-to-date automatically. Subsequent PRs require a live rebase after each preceding PR merges to main. Check if rebase is needed before running tests.
- **Dotnet restore on cold worktrees:** Always run `dotnet restore` before `dotnet test --no-restore` when a worktree has never been built. The `--no-restore` flag fails with error `NETSDK1004` on cold checkouts. Run restore once; subsequent test runs can use `--no-restore`.
- **gh pr merge exit codes are misleading:** `gh pr merge --delete-branch` returns non-zero exit even when the squash merge succeeds on GitHub. The actual merge is successful; the failure is from the local branch-delete step (`fatal: 'main' is already used by worktree`). Inspect GitHub to confirm the merge landed. Do not treat non-zero exit as a merge failure.
- **Force-push with explicit remote branch:** When a worktree branch has no upstream tracking, force-push requires explicit remote reference: `git push --force-with-lease origin <branch>`. Omitting the remote name causes "no upstream configured" errors.
- **Sequential merge for file conflicts:** When multiple PRs touch the same file (e.g., `Program.cs`), they must merge to main sequentially. Parallel merges cause file conflicts on subsequent worktree rebases. Enforce sequential ordering in CI/automation.

## Examples
**Rebase after merge:**
```powershell
# After previous PR merges to main, rebase the current worktree branch
git rebase origin/main
```

**Dotnet restore on first run:**
```bash
dotnet restore
dotnet test --no-restore
```

**Inspect GitHub despite non-zero exit:**
```bash
gh pr merge <pr-number> --squash --delete-branch
# Returns exit 128 even if merge succeeded. Verify on GitHub before failing.
```

**Force-push from worktree:**
```bash
git push --force-with-lease origin feature-branch  # Explicit remote required
```

**Sequential PR merge strategy:**
```
PR-A (touches Program.cs) → merge to main → rebase PR-B → merge to main
PR-B (touches Program.cs)
PR-C (different file) → can be parallel to A or B
```

## Anti-Patterns
- ❌ Running `dotnet test --no-restore` on a cold worktree without prior `dotnet restore` → NETSDK1004 error
- ❌ Treating non-zero exit from `gh pr merge --delete-branch` as a GitHub merge failure
- ❌ Omitting the remote name in `git push --force-with-lease` from a worktree without upstream tracking
- ❌ Merging multiple PRs that touch the same file in parallel
- ❌ Skipping rebase on subsequent worktree PRs after main has advanced
