---
name: "release-process"
description: "Repo release workflow: version bump, changelog, quality gates (format+test), Leela-owned release notes, commit/push, CI gate, then tag and push tags"
domain: "release-management"
confidence: "high"
source: "manual"
---

## Context

Use this skill when preparing and shipping a release from `main` in this repository. It standardizes the release order and enforces a hard CI gate before tags are created.

## Patterns

1. **Start from updated main**
- `git checkout main`
- `git fetch origin`
- `git pull --ff-only origin main`

2. **Bump version first**
- Update version in the canonical repo locations for the release.
- Keep version values consistent across all touched files.

3. **Update changelog**
- Add the release entry in `CHANGELOG.md` with date, version, and notable changes.
- Ensure the changelog reflects exactly what is being released.

4. **Run quality gates (MANDATORY)**
- `dotnet format --verify-no-changes` — verify formatting; fix any issues before proceeding.
- `dotnet test` — all tests must pass; do NOT proceed if any test fails.
- If either command fails, fix the issue first and restart from step 2.

5. **Leela owns release notes**
- Leela prepares release notes content (summary, highlights, breaking changes, migration notes if applicable).
- Do not publish release tags until Leela's release notes are finalized.

6. **Commit release prep together**
- Stage version + changelog + release notes artifacts in one release-prep commit.
- Suggested message format: `chore(release): vX.Y.Z`.

7. **Push release prep commit**
- `git push origin main`

8. **Hard CI gate**
- Wait until CI for `main` is fully green.
- No tag creation while any required check is pending or failed.

9. **Create and push tag only after CI green**
- `git tag -a vX.Y.Z -m "vX.Y.Z"`
- `git push origin vX.Y.Z`
- If multiple tags are intentionally used, push explicitly or with `git push --tags`.

10. **Post-tag verification**
- Confirm tag exists remotely and points to the intended release commit.

## Examples

```bash
git checkout main
git fetch origin
git pull --ff-only origin main

# edit version + CHANGELOG + release notes (Leela)

dotnet format --verify-no-changes
dotnet test

git add <version-files> CHANGELOG.md <release-notes-files>
git commit -m "chore(release): vX.Y.Z"
git push origin main

# wait for CI green
git tag -a vX.Y.Z -m "vX.Y.Z"
git push origin vX.Y.Z
```

## Anti-Patterns

- ❌ Tagging before CI is green
- ❌ Splitting version/changelog/release-notes across multiple unrelated commits
- ❌ Pushing tags from a branch other than updated `main`
- ❌ Publishing tags before Leela finalizes release notes
- ❌ Using broad destructive git commands to force release state
- ❌ Pushing a release without running `dotnet test` first
