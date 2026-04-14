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
poshmcp update-config --add-function Get-Process
poshmcp update-config --add-function Get-Service
```

**Option B: Include patterns**
```bash
poshmcp update-config --add-include-pattern "Get-*"
```

**Option C: Modules**
```bash
poshmcp update-config --add-module Az.Accounts
poshmcp update-config --add-import-module Az.Accounts
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

# Increase installation timeout
poshmcp update-config --install-timeout-seconds 600
```

---

**Ready for more?** → [Configuration Guide](configuration.md)
