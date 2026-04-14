---
name: "nuget-github-packages"
description: "Pattern for publishing .NET NuGet packages to GitHub Packages registry with GitHub PAT authentication."
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
```bash
dotnet nuget add source \
  --username {gh-username} \
  --password {token} \
  --store-password-in-clear-text \
  --name github \
  "https://nuget.pkg.github.com/{owner}/index.json"
```
- **Note:** `--store-password-in-clear-text` stores credentials in `~/.nuget/NuGet.config`; consider security implications
- **Idempotent:** Running again with same `--name github` updates existing source
- **Credential storage:** NuGet stores config in platform-specific location (Windows: `%AppData%\NuGet\NuGet.config`)

### Publishing Workflow (Amy's Pattern)

**1. Update version in .csproj:**
```xml
<PropertyGroup>
  <Version>1.2.3</Version>
  <!-- other properties -->
</PropertyGroup>
```

**2. Pack with Release configuration:**
```bash
dotnet pack --configuration Release --output ./nupkg
```
Output: `./nupkg/PoshMcp.1.2.3.nupkg`

**3. Resolve owner (choose one approach):**
```bash
# Option A: From git remote (recommended for repos)
OWNER=$(git remote get-url origin | sed -E 's|.*github.com[/:](.*)/.*\.git|\1|')

# Option B: From gh CLI (for dynamic discovery)
OWNER=$(gh api user --jq .login)
```

**4. Get authentication token:**
```bash
TOKEN=$(gh auth token)
```

**5. Push to GitHub Packages:**
```bash
dotnet nuget push "./nupkg/PoshMcp.1.2.3.nupkg" \
  --api-key "$TOKEN" \
  --source "https://nuget.pkg.github.com/$OWNER/index.json"
```

## Examples

### Complete Publish Script (Bash)
```bash
#!/bin/bash
set -e

# Resolve project and version
PROJECT="PoshMcp.Server"
VERSION="1.2.3"
OWNER=$(git remote get-url origin | sed -E 's|.*github.com[/:](.*)/.*\.git|\1|')
TOKEN=$(gh auth token)
NUPKG_PATH="./nupkg/${PROJECT}.${VERSION}.nupkg"

# Pack
echo "Packing ${PROJECT} v${VERSION}..."
dotnet pack --configuration Release --output ./nupkg

# Verify package was created
if [ ! -f "$NUPKG_PATH" ]; then
  echo "ERROR: Package not found at $NUPKG_PATH"
  exit 1
fi

# Push to GitHub Packages
echo "Publishing to GitHub Packages (owner: $OWNER)..."
dotnet nuget push "$NUPKG_PATH" \
  --api-key "$TOKEN" \
  --source "https://nuget.pkg.github.com/$OWNER/index.json"

echo "✓ Published $NUPKG_PATH successfully"
```

### PowerShell Script (Windows/Cross-platform)
```powershell
$Project = "PoshMcp.Server"
$Version = "1.2.3"
$Owner = & git remote get-url origin | % { $_ -match 'github\.com[/:](.+)/.+' | Out-Null; $matches[1] }
$Token = & gh auth token
$NupkgPath = "./nupkg/${Project}.${Version}.nupkg"

Write-Host "Packing $Project v$Version..."
& dotnet pack --configuration Release --output ./nupkg

if (-not (Test-Path $NupkgPath)) {
    throw "Package not found: $NupkgPath"
}

Write-Host "Publishing to GitHub Packages (owner: $Owner)..."
& dotnet nuget push $NupkgPath `
    --api-key $Token `
    --source "https://nuget.pkg.github.com/$Owner/index.json"

Write-Host "✓ Published $NupkgPath successfully"
```

### One-time Source Setup (Windows)
```powershell
$Owner = "github-org-name"
$Username = "your-gh-username"
$Token = & gh auth token

Write-Host "Adding GitHub Packages source..."
& dotnet nuget add source `
    --username $Username `
    --password $Token `
    --store-password-in-clear-text `
    --name github `
    "https://nuget.pkg.github.com/$Owner/index.json"

Write-Host "✓ GitHub Packages source added"
```

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
