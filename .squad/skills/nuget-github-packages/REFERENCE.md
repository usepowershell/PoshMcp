# NuGet GitHub Packages — Code Reference

## Complete Publish Script (Bash)

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

## PowerShell Script (Windows/Cross-platform)

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

## One-time Source Setup (Windows)

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

## Adding GitHub Packages Source (Bash)

```bash
dotnet nuget add source \
  --username {gh-username} \
  --password {token} \
  --store-password-in-clear-text \
  --name github \
  "https://nuget.pkg.github.com/{owner}/index.json"
```
