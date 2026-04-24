---
uid: getting-started
title: Getting Started with PoshMcp
---

# Getting Started with PoshMcp

Transform PowerShell into AI-consumable tools in minutes. This guide walks you through installing, configuring, and running PoshMcp.

## Prerequisites

**Required:**
- **.NET 10 Runtime** — download from [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/10.0)

**Optional:**
- **PowerShell 7** — for out-of-process PowerShell support (better module isolation)
- **.NET 10 SDK** — only needed if building from source
- **Docker** — only needed for containerized deployments
- **Git** — only needed if cloning the repository

## Installation (5 minutes)

### Option 1: .NET Global Tool (Recommended)

Install PoshMcp as a global .NET tool from nuget.org:

```bash
dotnet tool install -g poshmcp
```

Verify installation:

```bash
poshmcp --version
```

For a specific version:

```bash
dotnet tool install -g poshmcp --version 0.5.5
```

### Option 2: Docker Container

For consistent environments and cloud deployment:

```bash
# Build the image
docker build -t poshmcp:latest https://github.com/microsoft/poshmcp.git

# Run the container
docker run -p 8080:8080 poshmcp:latest
```

### Option 3: Building from Source

For developers and contributors:

```bash
git clone https://github.com/microsoft/poshmcp.git
cd poshmcp
dotnet build
dotnet run --project PoshMcp.Server -- serve --transport stdio
```

### Installing Preview Builds

PoshMcp preview builds are published to GitHub Packages on every commit to main, allowing you to try the latest features before a stable release. Preview packages require GitHub authentication.

#### Prerequisites

1. **GitHub account** — You need to be logged in to GitHub
2. **GitHub CLI or Personal Access Token (PAT)** — Choose one approach:

   **Option A — Using GitHub CLI (Recommended):**
   - Install [GitHub CLI](https://cli.github.com/)
   - Run `gh auth login` and authenticate
   - No additional setup needed—the commands below will work automatically

   **Option B — Using a Personal Access Token:**
   - Generate a PAT at https://github.com/settings/tokens
   - Select **"Classic token"** and check **`read:packages`** scope
   - Store it somewhere safe (you'll use it in the next step)

#### Add GitHub Packages as a NuGet Source

Choose your setup method:

**With GitHub CLI (Simplest):**

```bash
# macOS / Linux
dotnet nuget add source "https://nuget.pkg.github.com/usepowershell/index.json" \
  --name "github-poshmcp" \
  --username "$(gh api user --jq .login)" \
  --password "$(gh auth token)" \
  --store-password-in-clear-text
```

**PowerShell (Windows/cross-platform):**

```powershell
dotnet nuget add source "https://nuget.pkg.github.com/usepowershell/index.json" `
  --name "github-poshmcp" `
  --username (gh api user --jq .login) `
  --password (gh auth token) `
  --store-password-in-clear-text
```

**With a Manual PAT:**

```bash
dotnet nuget add source "https://nuget.pkg.github.com/usepowershell/index.json" \
  --name "github-poshmcp" \
  --username YOUR_GITHUB_USERNAME \
  --password YOUR_PAT \
  --store-password-in-clear-text
```

Replace:
- `YOUR_GITHUB_USERNAME` with your GitHub login
- `YOUR_PAT` with your personal access token

> **Note:** The `--store-password-in-clear-text` flag is required by the .NET CLI for basic authentication with GitHub Packages. The credentials are stored in your NuGet config file at `~/.nuget/NuGet/NuGet.Config` (not a keychain).

#### Install the Preview Build

```bash
# Install the latest preview version
dotnet tool install -g poshmcp --prerelease --source github-poshmcp

# Or install a specific preview version
dotnet tool install -g poshmcp --version 0.6.0-preview.42 --source github-poshmcp
```

#### Update Between Stable and Preview

```bash
# Update to the latest preview
dotnet tool update -g poshmcp --prerelease --source github-poshmcp

# Switch back to stable
dotnet tool update -g poshmcp --source nuget.org
```

#### Find Available Preview Versions

Browse published preview packages at:
**https://github.com/usepowershell/PoshMcp/packages**

Preview versions are named `0.6.0-preview.{run_number}` (e.g., `0.6.0-preview.42`), where the run number corresponds to the CI/CD pipeline execution.

## First Run (3 steps)

### Step 1: Create Configuration

```bash
poshmcp create-config
```

This creates `appsettings.json` with sensible defaults.

### Step 2: Expose Tools

Choose how to expose PowerShell commands:

**Option A: Specific commands**
```bash
poshmcp update-config --add-command Get-Process
poshmcp update-config --add-command Get-Service
```

**Option B: Include patterns**
```bash
poshmcp update-config --add-include-pattern "Get-*"
```

**Option C: Modules**
```bash
poshmcp update-config --add-module Az.Accounts
```

### Step 3: Start the Server

**For local development (VS Code, GitHub Copilot):**
```bash
poshmcp serve --transport stdio
```

**For HTTP API (testing, integration):**
```bash
poshmcp serve --transport http --port 8080
```

## Next: Configure for Your Use Case

- **[Configuration Guide](configuration.md)** — detailed configuration options
- **[Entra ID Authentication](authentication.md)** — secure with OAuth 2.1
- **[Docker Deployment](docker.md)** — containerize for production
- **[Examples](examples.md)** — real-world scenarios

## Testing Your Setup

### With Stdio Mode

Add this to your MCP client configuration (e.g., `.vscode/cline_mcp_settings.json`):

```json
{
  "mcpServers": {
    "poshmcp": {
      "command": "poshmcp",
      "args": ["serve", "--transport", "stdio"]
    }
  }
}
```

### With HTTP Mode

Test with curl:

```bash
# List available tools
curl http://localhost:8080/tools

# Call a tool
curl -X POST http://localhost:8080/call \
  -H "Content-Type: application/json" \
  -d '{
    "tool": "Get-Process",
    "arguments": {}
  }'

# Health check
curl http://localhost:8080/health
```

## Troubleshooting

**Problem:** No tools discovered
```bash
# Check configuration file
cat ./appsettings.json

# Verify PowerShell access
pwsh -Command "Get-Command Get-Process"
```

**Problem:** Server won't start
```bash
# Check .NET version
dotnet --version

# Check for port conflicts (HTTP mode)
lsof -i :8080  # macOS/Linux
Get-NetTCPConnection -LocalPort 8080  # Windows
```

**Problem:** Module installation fails
```bash
# Test module installation manually
pwsh -Command "Install-Module Az.Accounts -Force"

# Add module to discovery list
poshmcp update-config --add-module Az.Accounts
```

---

**Ready for more?** → [Configuration Guide](configuration.md)
