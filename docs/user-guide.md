# PoshMcp User Guide

**Transform PowerShell into AI-consumable tools with zero code changes.**

This guide walks you through installing, configuring, and deploying PoshMcp. Whether you're running locally in VS Code or in production on Azure, you'll get working examples at every step.

---

## Table of Contents

1. [Getting Started](#getting-started)
2. [Installation](#installation)
3. [Configuration](#configuration)
4. [Exposing PowerShell Tools](#exposing-powershell-tools)
5. [Security](#security)
6. [Transport Modes](#transport-modes)
7. [Session Management](#session-management)
8. [AI Assistant Integration](#ai-assistant-integration)
9. [Troubleshooting](#troubleshooting)
10. [Examples](#examples)

---

## Getting Started

### Prerequisites

**Required:**
- **.NET 10 Runtime** — download from [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/10.0)

**Optional:**
- **PowerShell 7** — for out-of-process PowerShell support (see [Transport Modes](#transport-modes))
  - Enables running PowerShell in a separate process rather than in-process, providing better module isolation and preventing namespace conflicts
- **.NET 10 SDK** — only needed if building from source
- **Docker** — only needed for containerized deployments
- **Git** — only needed if cloning the repository

**Note:** PowerShell 7.x is included automatically via the `Microsoft.PowerShell.SDK` NuGet package for in-process mode.

### Quickstart: dotnet tool install (5 minutes)

Install PoshMcp as a global .NET tool and start exposing PowerShell commands in seconds.

#### 1. Install the tool

```bash
dotnet tool install --global PoshMcp \
  --add-source https://nuget.pkg.github.com/usepowershell/index.json
```

This installs `poshmcp` command globally. You're done with prerequisites.

#### 2. Create a configuration file

Generate your initial configuration with the CLI command:

```bash
poshmcp create-config
```

This creates `appsettings.json` in your current directory with a sensible default configuration.

You can customize it later using:

```bash
poshmcp update-config --add-function Get-Process --add-function Get-Service
```

#### 3. Start the server

```bash
# Stdio mode (for MCP clients like GitHub Copilot)
poshmcp serve --transport stdio

# Or HTTP mode (for testing/integration)
poshmcp serve --transport http --port 8080
```

You should see:

```
info: PoshMcp.Program[0]
      Starting MCP server on stdio...
```

#### 4. Point your MCP client at it

For **GitHub Copilot in VS Code**, add to `.vscode/cline_mcp_settings.json`:

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

Restart Copilot, and your PowerShell tools are now available as MCP tools.

### Test Your Installation (Optional)

To verify the server is running, start it in HTTP mode and test with curl:

```bash
# Terminal 1: Start the server
poshmcp serve --transport http --port 8080

# Terminal 2: Test a tool call
curl -X POST http://localhost:8080/mcp/v1/tools/call \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Get-Process",
    "arguments": {}
  }'
```

You should see JSON output with running processes.

---

## Installation

The recommended installation method is `dotnet tool install` (see **Quickstart** above). Choose an alternative below based on your use case.

### Docker Container

Best for: consistent environments, CI/CD pipelines, cloud deployment.

#### Build a Docker image

```bash
# Clone the repository first
git clone https://github.com/microsoft/poshmcp.git
cd poshmcp

# Build a base image
docker build -t poshmcp:latest .
```

#### Run the container

```bash
# HTTP mode (default)
docker run -p 8080:8080 poshmcp:latest

# Stdio mode (for MCP clients)
docker run -it poshmcp:latest poshmcp serve --transport stdio
```

#### Pre-install PowerShell modules (optional)

Faster startup by installing modules at build time:

```bash
docker build \
  --build-arg MODULES="Az.Accounts Az.Resources Az.Storage" \
  -t poshmcp:azure-ready .
```

Or using environment variables at runtime:

```bash
docker run -e POSHMCP_MODULES="Az.Accounts Az.Resources" -p 8080:8080 poshmcp:latest
```

### Building from Source

Best for: contributors, early adopters, custom extensions.

**Prerequisites:**
- .NET 10 SDK (not just runtime)
- Git

#### Clone and build

```bash
# Clone the repository
git clone https://github.com/microsoft/poshmcp.git
cd poshmcp

# Build the project
dotnet build

# Start the server locally
dotnet run --project PoshMcp.Server --config ./appsettings.json
```

#### Test with the Interactive Client

In a separate terminal:

```bash
# Start the test client
cd poshmcp
dotnet run --project TestClient

# At the prompt, type:
# init - Initialize the connection
# list-tools - See available tools
# call Get-Process - Run the tool
```

You should see available PowerShell tools listed.

### Azure Container Apps

Best for: production, multi-tenant, managed infrastructure.

See [infrastructure/azure/README.md](../infrastructure/azure/README.md) for the full deployment guide, including Managed Identity setup, monitoring, and scaling.

Quick deploy:

```bash
cd infrastructure/azure
./deploy.sh
```

---

## Configuration

### Creating and Managing Configuration

PoshMcp provides CLI commands to generate and modify your configuration without manual file editing.

#### Generate Initial Configuration

```bash
poshmcp create-config
```

This creates a default `appsettings.json` with sensible defaults. You can specify a custom path:

```bash
poshmcp create-config --path ./my-config.json
```

#### Update Configuration via CLI

Modify your configuration without editing JSON manually:

```bash
# Add specific commands to expose
poshmcp update-config --add-function Get-Process --add-function Get-Service

# Add a module
poshmcp update-config --add-module Az.Accounts

# Add module paths
poshmcp update-config --add-module-path /mnt/shared-modules

# Non-interactive mode (for scripting)
poshmcp update-config --non-interactive --add-module Az.Accounts
```

### appsettings.json Reference

The configuration file controls which PowerShell commands are exposed and how the server behaves. While you should use the CLI commands above to modify it, here's what the configuration structure looks like for reference.

#### Basic Configuration

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "PowerShellConfiguration": {
    "CommandNames": [
      "Get-Process",
      "Get-Service"
    ],
    "Modules": [],
    "IncludePatterns": [],
    "ExcludePatterns": [],
    "EnableDynamicReloadTools": false,
    "Performance": {
      "EnableResultCaching": false,
      "UseDefaultDisplayProperties": true
    }
  }
}
```

#### Configuration Options

| Option | Type | Description |
|--------|------|-------------|
| `CommandNames` | array | Specific command names to expose (e.g., `["Get-Process", "Get-Service"]`) |
| `Modules` | array | PowerShell modules to import (e.g., `["Az.Accounts", "Pester"]`) |
| `IncludePatterns` | array | Wildcard patterns to include (e.g., `["Get-*", "Set-*"]`) |
| `ExcludePatterns` | array | Wildcard patterns to exclude (e.g., `["*-Dangerous*", "Remove-*"]`) |
| `EnableDynamicReloadTools` | boolean | Auto-reload tool definitions when config changes (slow) |
| `EnableResultCaching` | boolean | Cache command output for performance |
| `UseDefaultDisplayProperties` | boolean | Use default PowerShell display properties |

### Environment Variables

Override configuration via environment variables:

```bash
# Set transport mode
export POSHMCP_TRANSPORT=http

# Set log level
export POSHMCP_LOG_LEVEL=debug

# Use custom config file
export POSHMCP_CONFIGURATION=/config/appsettings.json
```

### Startup Scripts

Run PowerShell code at server startup to configure the session. Create or edit the configuration file directly for startup scripts (these aren't yet available via CLI commands):

```json
{
  "PowerShellConfiguration": {
    "Environment": {
      "StartupScript": "$Global:CompanyName = 'Acme'; Write-Host 'Ready!'"
    }
  }
}
```

Or load from a file:

```json
{
  "PowerShellConfiguration": {
    "Environment": {
      "StartupScriptPath": "/config/startup.ps1"
    }
  }
}
```

Example startup script (`startup.ps1`):

```powershell
# Company-specific initialization
$Global:CompanyName = 'Acme'
$Global:Environment = 'Production'

# Define utility functions
function Get-CompanyInfo {
    [PSCustomObject]@{
        Company = $Global:CompanyName
        Environment = $Global:Environment
    }
}

Write-Host "✓ Environment initialized" -ForegroundColor Green
```

### Module Installation & Paths

Install modules from the PowerShell Gallery at startup using CLI commands:

```bash
# Add modules to install
poshmcp update-config --add-install-module Az.Accounts --minimum-version 2.0.0
poshmcp update-config --add-install-module Az.Accounts --repository PSGallery --scope CurrentUser

# Import modules
poshmcp update-config --add-import-module Microsoft.PowerShell.Management
poshmcp update-config --add-import-module Az.Accounts

# Add module paths
poshmcp update-config --add-module-path /mnt/shared-modules
poshmcp update-config --add-module-path ./custom-modules

# Configure PowerShell Gallery settings
poshmcp update-config --trust-psgallery
poshmcp update-config --skip-publisher-check
poshmcp update-config --install-timeout-seconds 600
```

**Configuration reference** (what the resulting `appsettings.json` looks like):

```json
{
  "PowerShellConfiguration": {
    "Environment": {
      "InstallModules": [
        {
          "Name": "Az.Accounts",
          "MinimumVersion": "2.0.0",
          "Repository": "PSGallery",
          "Scope": "CurrentUser"
        }
      ],
      "ImportModules": [
        "Microsoft.PowerShell.Management",
        "Microsoft.PowerShell.Utility",
        "Az.Accounts"
      ],
      "ModulePaths": [
        "/mnt/shared-modules",
        "./custom-modules"
      ],
      "TrustPSGallery": true,
      "SkipPublisherCheck": true,
      "InstallTimeoutSeconds": 600
    }
  }
}
```

### CLI Configuration Commands

Manage configuration files using the `poshmcp` CLI without manual editing:

```bash
# Create default appsettings.json
poshmcp create-config

# Add a specific command
poshmcp update-config --add-function Get-Process

# Add a module
poshmcp update-config --add-module Az.Accounts

# Non-interactive mode (skip prompts)
poshmcp update-config --non-interactive --add-module Az.Accounts
```

**For developers building from source:**

```bash
# Create default appsettings.json
dotnet run --project PoshMcp.Server -- create-config

# Add a specific command
dotnet run --project PoshMcp.Server -- update-config --add-function Get-Process

# Add a module
dotnet run --project PoshMcp.Server -- update-config --add-module Az.Accounts

# Non-interactive mode (skip prompts)
dotnet run --project PoshMcp.Server -- update-config --non-interactive \
  --add-module Az.Accounts
```

---

## Exposing PowerShell Tools

### How Commands Become Tools

PoshMcp discovers PowerShell commands and transforms them into AI-consumable tools automatically.

1. **Discovery** — reads available PowerShell commands via `Get-Command`
2. **Inspection** — extracts metadata: name, synopsis, parameters, output type
3. **Schema generation** — creates JSON schema for AI consumption
4. **Registration** — exposes tool via MCP protocol
5. **Execution** — invokes the command and returns structured results

Example: `Get-Process` becomes an MCP tool with parameters like `-Name`, `-Id`, etc.

### Pattern-Based Filtering

Use patterns to expose groups of commands without listing each one. Manage with CLI commands:

```bash
# Add include patterns
poshmcp update-config --add-include-pattern "Get-*"
poshmcp update-config --add-include-pattern "Set-Service"
poshmcp update-config --add-include-pattern "Restart-*"

# Add exclude patterns
poshmcp update-config --add-exclude-pattern "Get-Dangerous*"
poshmcp update-config --add-exclude-pattern "*-Credential"
poshmcp update-config --add-exclude-pattern "Remove-*"
```

This exposes all `Get-*` commands except those matching `Get-Dangerous*`, excludes anything with "Credential" in the name, and blocks all `Remove-*` commands.

**Configuration reference** (what the resulting `appsettings.json` looks like):

```json
{
  "PowerShellConfiguration": {
    "IncludePatterns": [
      "Get-*",
      "Set-Service",
      "Restart-*"
    ],
    "ExcludePatterns": [
      "Get-Dangerous*",
      "*-Credential",
      "Remove-*"
    ]
  }
}
```

### Allowlists (Whitelist Pattern)

Expose only specific commands using CLI commands:

```bash
# Add specific commands to expose
poshmcp update-config --add-function Get-Service
poshmcp update-config --add-function Restart-Service
poshmcp update-config --add-function Get-Process
poshmcp update-config --add-function Stop-Process
```

**Configuration reference** (what the resulting `appsettings.json` looks like):

```json
{
  "PowerShellConfiguration": {
    "CommandNames": [
      "Get-Service",
      "Restart-Service",
      "Get-Process",
      "Stop-Process"
    ]
  }
}
```

### Module Paths

Load modules from custom directories using CLI commands:

```bash
# Add module paths
poshmcp update-config --add-module-path "/mnt/shared-modules"
poshmcp update-config --add-module-path "C:\\CustomModules"
poshmcp update-config --add-module-path "./local-modules"

# Import modules at startup
poshmcp update-config --add-import-module MyCustomModule
poshmcp update-config --add-import-module Az.Accounts
```

**Configuration reference** (what the resulting `appsettings.json` looks like):

```json
{
  "PowerShellConfiguration": {
    "Environment": {
      "ModulePaths": [
        "/mnt/shared-modules",
        "C:\\CustomModules",
        "./local-modules"
      ],
      "ImportModules": [
        "MyCustomModule",
        "Az.Accounts"
      ]
    }
  }
}
```

### Built-In Utility Tools

PoshMcp provides utility tools for working with command output:

- **`get-last-command-output`** — retrieve cached output from the previous command
- **`sort-last-command-output`** — sort results by property
- **`filter-last-command-output`** — filter using PowerShell expressions
- **`group-last-command-output`** — group results by property

Example workflow:

```
1. AI calls: Get-Service
   Result: ~50 services returned

2. AI calls: sort-last-command-output -Property Status
   Result: Sorted by Status

3. AI calls: filter-last-command-output -FilterExpression "$_.Status -eq 'Running'"
   Result: Only running services
```

---

## Security

### Isolated Runspaces

Each session gets its own PowerShell runspace. Variables and functions persist within a session but are isolated from other sessions.

**Stdio mode** (single connection): One runspace per client connection.

**HTTP mode** (multi-user): Separate runspace per user session with automatic cleanup on disconnect.

### Command Filtering

Restrict dangerous commands via CLI configuration:

```bash
# Exclude dangerous patterns
poshmcp update-config --add-exclude-pattern "Remove-*"
poshmcp update-config --add-exclude-pattern "Disable-*"
poshmcp update-config --add-exclude-pattern "*-Credential"
poshmcp update-config --add-exclude-pattern "Format-*"
```

**Configuration reference** (what the resulting `appsettings.json` looks like):

```json
{
  "PowerShellConfiguration": {
    "ExcludePatterns": [
      "Remove-*",
      "Disable-*",
      "*-Credential",
      "Format-*",
      "ConvertTo-SecureString"
    ]
  }
}
```

### Azure Managed Identity

When deployed to Azure (Container Apps, AKS, etc.), PoshMcp automatically uses Azure Managed Identity for secure resource access—no credentials needed.

```powershell
# Automatically uses the managed identity
Connect-AzAccount -Identity
Get-AzResource
```

Configure in your startup script:

```powershell
# No explicit credentials needed
Connect-AzAccount -Identity -ErrorAction Stop
Write-Host "✓ Connected to Azure using Managed Identity"
```

### Authentication (Optional)

Enable API key authentication for HTTP mode:

```json
{
  "Authentication": {
    "Enabled": true,
    "DefaultScheme": "Bearer",
    "Schemes": {
      "Bearer": {
        "Type": "ApiKey",
        "Location": "Header",
        "HeaderName": "X-API-Key"
      }
    }
  }
}
```

Clients must provide the API key in the request header:

```bash
curl -H "X-API-Key: your-secret-key" http://localhost:8080/tools
```

### Identity Separation

In HTTP mode, each user gets isolated execution:

- Separate runspace per user/session
- Variables don't bleed between users
- Automatic cleanup on session timeout
- Audit trail via correlation IDs

---

## Transport Modes

### Stdio Mode (Default)

Best for: local development, Copilot integration, single-connection scenarios.

**Characteristics:**
- Single persistent connection
- Stream-based communication
- One runspace per connection
- Minimal overhead

**Start stdio server (using installed tool):**

```bash
poshmcp serve --transport stdio
```

Or with custom config:

```bash
poshmcp serve --transport stdio --config ./appsettings.json
```

**Configure in VS Code or MCP client:**

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

**For developers building from source:**

```bash
dotnet run --project PoshMcp.Server -- serve --transport stdio
```

### HTTP Mode

Best for: multi-user deployments, web integration, cloud infrastructure.

**Characteristics:**
- RESTful API (JSON request/response)
- Per-user isolation
- Horizontal scaling capable
- Built-in health checks

**Start HTTP server (using installed tool):**

```bash
poshmcp serve --transport http --port 8080
```

Or with custom config:

```bash
poshmcp serve --transport http --port 8080 --config ./appsettings.json
```

**For developers building from source:**

```bash
dotnet run --project PoshMcp.Server -- serve --transport http --port 8080
```

**API endpoints:**

```bash
# List available tools
curl http://localhost:8080/tools

# Call a tool
curl -X POST http://localhost:8080/call \
  -H "Content-Type: application/json" \
  -d '{
    "tool": "Get-Service",
    "arguments": {
      "Name": "wuauserv"
    }
  }'

# Health check
curl http://localhost:8080/health
curl http://localhost:8080/health/ready
```

### Environment Variable Override

Set transport via environment variable:

```bash
export POSHMCP_TRANSPORT=http
poshmcp serve
```

Or for developers:

```bash
export POSHMCP_TRANSPORT=http
dotnet run --project PoshMcp.Server
```

---

## Session Management

### Persistent State

Variables and functions persist across multiple calls within the same session.

```powershell
# Call 1: Set a variable
$MyData = @{
    Timestamp = Get-Date
    Records = Get-Process | Select-Object -First 5
}

# Call 2: Access the variable (same session)
$MyData.Timestamp
# Output: [date from Call 1]

$MyData.Records
# Output: [process list from Call 1]
```

### Per-User Isolation (HTTP Mode)

Each user maintains independent state:

```powershell
# User A's session
$Global:UserId = "user-a@company.com"
$Global:Data = @{ ... }

# User B's session (separate runspace)
$Global:UserId = "user-b@company.com"
$Global:Data = @{ ... }
# User B never sees User A's data
```

### Session Timeouts

Runspaces are cleaned up after inactivity (configurable). Default timeout is typically 30-60 minutes.

Override via environment variable:

```bash
export POSHMCP_SESSION_TIMEOUT_MINUTES=120
```

### Startup Scripts Per Session

Run custom code when a new session starts:

```json
{
  "PowerShellConfiguration": {
    "Environment": {
      "StartupScript": "$Global:SessionStartTime = Get-Date"
    }
  }
}
```

Access session metadata:

```powershell
Write-Host "Session started: $($Global:SessionStartTime)"
```

---

## AI Assistant Integration

### GitHub Copilot in VS Code

Configure PoshMcp as an MCP server for GitHub Copilot.

**1. Install PoshMcp (if not already installed):**

```bash
dotnet tool install --global PoshMcp \
  --add-source https://nuget.pkg.github.com/usepowershell/index.json
```

**2. Create/edit `.vscode/cline_mcp_settings.json` (or your MCP client config):**

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

If you prefer to use a custom configuration file:

```json
{
  "mcpServers": {
    "poshmcp": {
      "command": "poshmcp",
      "args": [
        "serve",
        "--transport", "stdio",
        "--config", "/path/to/appsettings.json"
      ]
    }
  }
}
```

**3. Open Copilot Chat:**

- Press `Ctrl+Shift+I` (or `Cmd+Shift+I` on macOS)
- Ask a question: "What services are running on this computer?"
- Copilot now has access to PoshMcp tools

**For developers building from source:**

If you're working in the poshmcp repository and want to test changes locally, use:

```json
{
  "mcpServers": {
    "poshmcp": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "C:\\Users\\YourUsername\\source\\poshmcp\\PoshMcp.Server",
        "--",
        "serve",
        "--transport",
        "stdio"
      ],
      "env": {
        "DOTNET_ENVIRONMENT": "Development"
      }
    }
  }
}
```

### Other MCP Clients

PoshMcp works with any MCP-compatible client. Configure the client to connect to the stdio server:

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

Or if building from source:

```json
{
  "mcpServers": {
    "poshmcp": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "/path/to/poshmcp/PoshMcp.Server",
        "--",
        "serve",
        "--transport",
        "stdio"
      ]
    }
  }
}
```

### Web Integration

Integrate PoshMcp's HTTP API into your web application:

```javascript
// Fetch available tools
const tools = await fetch('http://localhost:8080/tools')
  .then(r => r.json());

// Call a tool
const result = await fetch('http://localhost:8080/call', {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({
    tool: 'Get-Process',
    arguments: { Name: 'explorer' }
  })
}).then(r => r.json());
```

---

## Troubleshooting

### Server Won't Start

**Problem:** Server fails to start or crashes immediately.

**Solution (using installed tool):**

```bash
# Check installation
poshmcp --version

# Start with debug logging
poshmcp serve --transport stdio --log-level debug

# View your configuration file
cat ./appsettings.json
```

**For developers (building from source):**

```bash
# Check .NET installation
dotnet --version

# Verify the SDK is 10.0+
dotnet --list-sdks

# Clean and rebuild
dotnet clean
dotnet build

# Start with debug output
dotnet run --project PoshMcp.Server -- serve --transport stdio --log-level debug
```

### No Tools Discovered

**Problem:** `list-tools` returns empty list or no tools are available.

**Solution (using installed tool):**

```bash
# View your configuration file
cat ./appsettings.json

# Verify PowerShell can access the commands
pwsh -Command "Get-Command Get-Process"

# Use CLI to add commands
poshmcp update-config --add-function Get-Process
```

**For developers (building from source):**

```bash
# Evaluate tools with verbose output
dotnet run --project PoshMcp.Server -- evaluate-tools --verbose

# View the configuration file
cat PoshMcp.Server/appsettings.json

# Verify CommandNames are correct
# (PowerShell is case-insensitive, but config must match)

# Use CLI to add commands
dotnet run --project PoshMcp.Server -- update-config --add-function Get-Process
```

### Tool Call Fails

**Problem:** Calling a tool returns an error.

**Solution (using installed tool):**

```bash
# Enable debug logging
poshmcp serve --transport http --port 8080 --log-level debug

# View exclude patterns in configuration
cat ./appsettings.json

# Test the command manually
pwsh -Command "Get-Process"

# Use CLI to check/adjust exclude patterns
poshmcp update-config --remove-exclude-pattern "Get-*"
```

**For developers (building from source):**

```bash
# Enable debug logging
export POSHMCP_LOG_LEVEL=debug
dotnet run --project PoshMcp.Server

# Check for excluded patterns in configuration
# Verify the command isn't in ExcludePatterns in appsettings.json

# Test the command manually
pwsh -Command "Get-Process"

# Use CLI to adjust patterns
dotnet run --project PoshMcp.Server -- update-config --remove-exclude-pattern "Get-*"
```

### Module Installation Fails

**Problem:** Modules don't install at startup.

**Solution (using installed tool or building from source):**

```bash
# Check the startup logs with debug level
poshmcp serve --transport stdio --log-level debug
# or
export POSHMCP_LOG_LEVEL=debug
dotnet run --project PoshMcp.Server

# Test module installation manually
pwsh -Command "Install-Module Az.Accounts -Force"

# Use CLI to configure module installation
poshmcp update-config --add-install-module Az.Accounts

# Increase the installation timeout using CLI
poshmcp update-config --install-timeout-seconds 600
```

### Slow Performance

**Problem:** Tool calls are slow.

**Solution:**

- **Enable result caching using CLI:**
  ```bash
  poshmcp update-config --enable-result-caching true
  ```

- **Configuration reference** (what gets written to `appsettings.json`):
  ```json
  {
    "Performance": {
      "EnableResultCaching": true
    }
  }
  ```

- **Pre-install modules in Docker (for containerized deployments):**
  ```bash
  docker build --build-arg MODULES="Az.Accounts Az.Resources" -t poshmcp:fast .
  ```

- **Use out-of-process mode for module isolation (requires PowerShell 7):**
  ```bash
  export POSHMCP_RUNTIME_MODE=out-of-process
  poshmcp serve
  ```
  Out-of-process mode runs PowerShell in a separate process, preventing module namespace conflicts and providing better isolation between sessions.

### Connection Issues

**Problem:** Client can't connect to the server.

**Solution (using installed tool):**

```bash
# Check server is running on the specified port
Get-NetTCPConnection -LocalPort 8080  # Windows
# or
lsof -i :8080  # Linux/Mac

# Start HTTP server and test
poshmcp serve --transport http --port 8080

# Test connectivity
curl http://localhost:8080/health
```

**For developers (building from source):**

```bash
# Check server is running
lsof -i :8080  # Linux/Mac
Get-NetTCPConnection -LocalPort 8080  # Windows

# Check server logs
dotnet run --project PoshMcp.Server -- serve --transport http --log-level trace

# Test connectivity
curl http://localhost:8080/health
```

### Docker Container Won't Start

**Problem:** `docker run` exits immediately.

**Solution:**

```bash
# Check logs
docker run --rm poshmcp:latest 2>&1 | tail -20

# Run in interactive mode
docker run -it poshmcp:latest bash

# Inside the container, test the server
poshmcp serve --transport http --log-level debug
```

---

## Examples

### Example 1: Service Health Check Tool

Create a tool that reports service status.

**Setup using CLI commands:**

```bash
# Create initial configuration
poshmcp create-config

# Add commands to expose
poshmcp update-config --add-function Get-Service
poshmcp update-config --add-function Restart-Service

# Add exclude patterns for safety
poshmcp update-config --add-exclude-pattern "*-Dangerous*"
```

**What this writes to `appsettings.json`:**

```json
{
  "PowerShellConfiguration": {
    "CommandNames": [
      "Get-Service",
      "Restart-Service"
    ],
    "ExcludePatterns": [
      "*-Dangerous*"
    ],
    "Environment": {
      "StartupScript": "$Global:CriticalServices = @('wuauserv', 'spooler', 'w32time')"
    }
  }
}
```

**Usage:**

```bash
# AI asks: "Are the Windows Update and Print Spooler services running?"

# PoshMcp calls: Get-Service
# Returns: Service objects including Status property

# AI processes and responds: "Both services are running."
```

### Example 2: Azure Resource Explorer

Expose Azure resource management commands.

**Setup using CLI commands:**

```bash
# Create initial configuration
poshmcp create-config

# Add Azure commands
poshmcp update-config --add-function Get-AzResource
poshmcp update-config --add-function Get-AzResourceGroup
poshmcp update-config --add-function Get-AzVM

# Install Azure modules
poshmcp update-config --add-install-module Az.Accounts --minimum-version 2.0.0
poshmcp update-config --add-install-module Az.Resources --minimum-version 6.0.0

# Import modules
poshmcp update-config --add-import-module Az.Accounts
poshmcp update-config --add-import-module Az.Resources
```

**What this writes to `appsettings.json`:**

```json
{
  "PowerShellConfiguration": {
    "CommandNames": [
      "Get-AzResource",
      "Get-AzResourceGroup",
      "Get-AzVM"
    ],
    "Environment": {
      "InstallModules": [
        {
          "Name": "Az.Accounts",
          "MinimumVersion": "2.0.0"
        },
        {
          "Name": "Az.Resources",
          "MinimumVersion": "6.0.0"
        }
      ],
      "StartupScript": "Connect-AzAccount -Identity -ErrorAction Stop"
    }
  }
}
```

**Docker build with modules pre-installed:**

```bash
docker build \
  --build-arg MODULES="Az.Accounts Az.Resources Az.Compute" \
  -t poshmcp:azure .
```

**Usage:**

```bash
# AI asks: "How many VMs do we have in the East US region?"

# PoshMcp calls: Get-AzVM -Location "East US" | Measure-Object
# Returns: Count of VMs

# AI responds: "You have 12 VMs in East US."
```

### Example 3: Custom Utility Functions

Define company-specific tools via startup script.

**Startup script (`startup.ps1`):**

```powershell
# Custom utility functions
function Get-HealthCheck {
    <#
    .SYNOPSIS
    Run health checks on critical services
    
    .DESCRIPTION
    Checks Windows Update, Print Spooler, and Time Sync services
    #>
    
    param()
    
    $critical = @('wuauserv', 'spooler', 'w32time')
    
    $critical | ForEach-Object {
        $service = Get-Service -Name $_
        [PSCustomObject]@{
            Service = $service.Name
            Status = $service.Status
            StartType = $service.StartType
        }
    }
}

function Get-SystemInfo {
    <#
    .SYNOPSIS
    Get system information
    #>
    
    [PSCustomObject]@{
        ComputerName = $env:COMPUTERNAME
        OSVersion = [System.Environment]::OSVersion.VersionString
        PowerShellVersion = $PSVersionTable.PSVersion.ToString()
        InstallTime = (Get-Item "C:\Windows\System32\kernel32.dll").CreationTime
    }
}

Write-Host "✓ Custom utilities loaded" -ForegroundColor Green
```

**Setup using CLI commands:**

```bash
# Create initial configuration
poshmcp create-config

# Add custom functions to expose
poshmcp update-config --add-function Get-HealthCheck
poshmcp update-config --add-function Get-SystemInfo
poshmcp update-config --add-function Get-Process
poshmcp update-config --add-function Get-Service

# Configure startup script path (must edit appsettings.json or use file-based config)
# In appsettings.json, add:
# "Environment": { "StartupScriptPath": "./startup.ps1" }
```

**What this writes to `appsettings.json`:**

```json
{
  "PowerShellConfiguration": {
    "CommandNames": [
      "Get-HealthCheck",
      "Get-SystemInfo",
      "Get-Process",
      "Get-Service"
    ],
    "Environment": {
      "StartupScriptPath": "./startup.ps1"
    }
  }
}
```

**Usage:**

```bash
# AI asks: "Run a health check on critical services"

# PoshMcp calls: Get-HealthCheck
# Returns:
# Service     Status StartType
# -------     ------ ---------
# wuauserv   Running Automatic
# spooler    Running Automatic
# w32time    Running Automatic

# AI responds: "All critical services are healthy."
```

### Example 4: Multi-Module Configuration

Expose tools from multiple modules with module-specific settings.

**Setup using CLI commands:**

```bash
# Create initial configuration
poshmcp create-config

# Add include patterns
poshmcp update-config --add-include-pattern "Get-AzVM"
poshmcp update-config --add-include-pattern "Get-AzResource"
poshmcp update-config --add-include-pattern "Get-AzStorageAccount"
poshmcp update-config --add-include-pattern "Get-Service"
poshmcp update-config --add-include-pattern "Get-Process"
poshmcp update-config --add-include-pattern "Restart-Computer"

# Add exclude patterns for safety
poshmcp update-config --add-exclude-pattern "Remove-*"
poshmcp update-config --add-exclude-pattern "*-Credential"
poshmcp update-config --add-exclude-pattern "Invoke-*"

# Install modules
poshmcp update-config --add-install-module Az.Accounts --minimum-version 2.0.0 --scope CurrentUser
poshmcp update-config --add-install-module Az.Compute --minimum-version 8.0.0 --scope CurrentUser
poshmcp update-config --add-install-module Az.Storage --repository PSGallery --scope CurrentUser

# Import modules
poshmcp update-config --add-import-module Microsoft.PowerShell.Management
poshmcp update-config --add-import-module Microsoft.PowerShell.Utility
poshmcp update-config --add-import-module Az.Accounts
poshmcp update-config --add-import-module Az.Compute
poshmcp update-config --add-import-module Az.Storage

# Configure module discovery
poshmcp update-config --trust-psgallery
poshmcp update-config --skip-publisher-check
poshmcp update-config --install-timeout-seconds 900

# Enable result caching for performance
poshmcp update-config --enable-result-caching true
```

**What this writes to `appsettings.modules.json`:**

```json
{
  "PowerShellConfiguration": {
    "IncludePatterns": [
      "Get-AzVM",
      "Get-AzResource",
      "Get-AzStorageAccount",
      "Get-Service",
      "Get-Process",
      "Restart-Computer"
    ],
    "ExcludePatterns": [
      "Remove-*",
      "*-Credential",
      "Invoke-*"
    ],
    "Environment": {
      "InstallModules": [
        {
          "Name": "Az.Accounts",
          "MinimumVersion": "2.0.0",
          "Scope": "CurrentUser"
        },
        {
          "Name": "Az.Compute",
          "MinimumVersion": "8.0.0",
          "Scope": "CurrentUser"
        },
        {
          "Name": "Az.Storage",
          "Repository": "PSGallery",
          "Scope": "CurrentUser"
        }
      ],
      "ImportModules": [
        "Microsoft.PowerShell.Management",
        "Microsoft.PowerShell.Utility",
        "Az.Accounts",
        "Az.Compute",
        "Az.Storage"
      ],
      "TrustPSGallery": true,
      "SkipPublisherCheck": true,
      "InstallTimeoutSeconds": 900
    },
    "Performance": {
      "EnableResultCaching": true,
      "UseDefaultDisplayProperties": true
    }
  }
}
```

**Start with custom config:**

```bash
dotnet run --project PoshMcp.Server -- serve \
  --config ./appsettings.modules.json --transport http --port 8080
```

### Example 5: Container Deployment with Custom Configuration

Deploy in Docker with pre-installed modules and startup script.

**Dockerfile:**

```dockerfile
FROM poshmcp:latest

# Copy custom configuration
COPY appsettings.json /app/appsettings.json

# Copy startup script
COPY startup.ps1 /app/startup.ps1

# Create volume mount points
RUN mkdir -p /config /mnt/modules

# Set environment variables
ENV POSHMCP_CONFIGURATION=/app/appsettings.json
ENV DOTNET_ENVIRONMENT=Production

ENTRYPOINT ["poshmcp", "serve", "--transport", "http"]
```

**Build and run:**

```bash
# Build
docker build -t myorg/poshmcp:production .

# Run
docker run -d \
  -p 8080:8080 \
  -e POSHMCP_LOG_LEVEL=info \
  --name poshmcp-prod \
  myorg/poshmcp:production

# Verify
curl http://localhost:8080/health
```

---

## Next Steps

- **Deploy to Azure:** Follow [infrastructure/azure/README.md](../infrastructure/azure/README.md)
- **Deep dive on Docker:** See [DOCKER.md](../DOCKER.md)
- **Environment customization:** Read [ENVIRONMENT-CUSTOMIZATION.md](ENVIRONMENT-CUSTOMIZATION.md)
- **Contribute:** Check [.github/copilot-instructions.md](../.github/copilot-instructions.md)

**Questions?** Open an issue on GitHub or check the [discussions](../../discussions).

---

**Transform your PowerShell expertise into AI-powered tools.**
