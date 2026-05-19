---
name: "nuget-github-packages"
description: "Publish .NET NuGet packages to GitHub Packages using GitHub PAT authentication — covering gh auth token retrieval, write:packages scope verification, dotnet pack/push workflow, and owner resolution from git remote. WHEN: publishing a NuGet package to GitHub Packages, debugging 401 on dotnet nuget push, adding a GitHub Packages NuGet source, or resolving write:packages scope errors."
domain: "devops-deployment"
confidence: "high"
source: "observed"
tools:
  - name: "gh auth token"
    description: "Retrieve GitHub authentication token with required scopes"
    when: "Setting up or refreshing GitHub authentication for NuGet operations"
  - name: "dotnet nuget"
    description: "NuGet source management and package pushing"
    when: "Adding NuGet sources or pushing packages to registries"
---

## Context
PoshMcp (and other .NET tools) need to publish NuGet packages to GitHub Packages for distribution within the organization. GitHub Packages provides a private NuGet registry backed by GitHub authentication rather than separate API keys. This skill captures Amy's established pattern for version bumping, packing, and publishing .NET packages to GitHub Packages.

## Patterns

### Authentication Setup
- **GitHub Packages feed URL:** `https://nuget.pkg.github.com/{owner}/index.json` (where `{owner}` is the GitHub org/user)
- **Token source:** Use `gh auth token` to retrieve a GitHub PAT with `write:packages` scope
- **Scope requirement:** Token must have `write:packages` permission (not just default read scope)
- **Refresh scope if needed:** `gh auth refresh -s write:packages` adds missing scope to existing token
- **Important:** GitHub Packages uses GitHub authentication—NOT NuGet.org API keys. NuGet.org requires a separate key from nuget.org/account/apikeys

### Owner/Organization Resolution
- **From git remote:** Parse owner from `git remote get-url origin` (GitHub HTTPS: `https://github.com/{owner}/{repo}.git`)
- **From gh CLI:** `gh api user --jq .login` retrieves authenticated user/org login
- **Preferred:** Use git remote parsing for repo-specific publishers; use gh API for dynamic owner discovery

### Adding GitHub Packages Source (One-time per machine)

> Code: see `REFERENCE.md` § "Adding GitHub Packages Source" and "One-time Source Setup"

- `--store-password-in-clear-text` stores credentials in `~/.nuget/NuGet.config`; consider security implications
- Running again with same `--name github` updates existing source
- NuGet stores config in platform-specific location (Windows: `%AppData%\NuGet\NuGet.config`)

### Publishing Workflow (Amy's Pattern)

**1. Update version** in `.csproj` `<Version>` property.

**2. Pack:** `dotnet pack --configuration Release --output ./nupkg`

**3. Resolve owner:** parse from `git remote get-url origin` or use `gh api user --jq .login`

**4. Get token:** `gh auth token`

**5. Push:** `dotnet nuget push "./nupkg/{Package}.{Version}.nupkg" --api-key "$TOKEN" --source "https://nuget.pkg.github.com/$OWNER/index.json"`

> Full scripts (Bash + PowerShell): see `REFERENCE.md`

## Examples

> See `REFERENCE.md` for complete publish scripts (Bash, PowerShell, one-time source setup).

## Anti-Patterns
- ❌ Using NuGet.org API keys for GitHub Packages (GitHub Packages requires GitHub PAT with `write:packages`)
- ❌ Storing token credentials in scripts or commits instead of using `gh auth token` dynamically
- ❌ Failing to verify token has `write:packages` scope (causes cryptic 401 errors during push)
- ❌ Hardcoding owner/org in scripts instead of resolving from git remote or `gh api user`
- ❌ Packing with Debug configuration instead of Release
- ❌ Pushing without verifying `.nupkg` file exists (leads to confusing error messages)
- ❌ Mixing GitHub Packages and NuGet.org in same package without separate credentials/sources

## Notes / Gotchas
- **Token refresh:** If you've recently added permissions to your PAT, run `gh auth refresh -s write:packages` to refresh the cached token
- **Source storage:** NuGet stores source configurations in a machine-level config file; add sources once per development machine
- **Credential visibility:** `--store-password-in-clear-text` writes token to `NuGet.config` in plaintext; ensure your `%AppData%\NuGet\` permissions are restricted
- **GitHub org repos:** The owner segment in the feed URL should be the GitHub organization that owns the repo (parse from remote URL)
- **Private packages:** GitHub Packages inherits repo permissions; consumers need repo read access + GitHub PAT with appropriate scopes
- **Version semantics:** NuGet follows semver; ensure version in `.csproj` matches what you intend to publish
