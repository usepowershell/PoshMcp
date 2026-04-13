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

- **.NET 10 SDK** — download from [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/10.0)
- **PowerShell 7.x** — included automatically via `Microsoft.PowerShell.SDK` NuGet package
- **Docker** (optional, for containerized deployments) — [docker.com](https://docker.com)
- **Git** — [git-scm.com](https://git-scm.com)

### First 5 Minutes

Start the MCP server locally and test a command in 5 minutes.

```bash
# Clone the repository
git clone https://github.com/microsoft/poshmcp.git
cd poshmcp

# Build the project
dotnet build

# Start stdio server (for MCP clients like Copilot)
dotnet run --project PoshMcp.Server
```

You should see output like:

```
info: PoshMcp.Program[0]
      Starting MCP server...
```

The server is now listening for MCP tool requests. Press `Ctrl+C` to stop.

### Test with the Interactive Client

In a new terminal:

```bash
# Start the test client
cd poshmcp
dotnet run --project TestClient

# At the prompt, type:
# init - Initialize the connection
# list-tools - See available tools
# call Get-Process - Run the tool
```

You should see output like:

```
[Tool] Get-Process
[Tool] Get-Service
[Tool] ... (more tools)
```

Now your first MCP server is running.

---

## Installation

### Local Development

Best for: testing, development, debugging locally.

```bash
# Clone and build
git clone https://github.com/microsoft/poshmcp.git
cd poshmcp
dotnet build

# Run stdio server
dotnet run --project PoshMcp.Server
```

### Docker Container

Best for: consistent environments, CI/CD pipelines, cloud deployment.

#### Build a base image

```bash
# From the repository root
docker build -t poshmcp:latest .
```

#### Run the container

```bash
# HTTP mode (default)
docker run -p 8080:8080 poshmcp:latest

# Stdio mode (tty)
docker run -it poshmcp:latest poshmcp serve --transport stdio
```

#### Pre-install PowerShell modules

Faster startup (reduces ~30s to <1s by installing modules at build time):

```bash
docker build \
  --build-arg MODULES="Az.Accounts Az.Resources Az.Storage" \
  -t poshmcp:azure-ready .
```

Or using the poshmcp CLI:

```bash
# Build with modules pre-installed
poshmcp build --modules "Az.Accounts Az.Resources" --tag myorg/poshmcp:prod

# Run with custom config
poshmcp run --mode http --port 8080 --tag myorg/poshmcp:prod \
  --config /config/appsettings.json
```

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

### appsettings.json Reference

The default configuration file controls which PowerShell commands are exposed and how the server behaves.

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

Run PowerShell code at server startup to configure the session:

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

Install modules from the PowerShell Gallery at startup:

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

Manage configuration files without manual editing:

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

Use patterns to expose groups of commands without listing each one:

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

This exposes all `Get-*` commands except those matching `Get-Dangerous*`, excludes anything with "Credential" in the name, and blocks all `Remove-*` commands.

### Allowlists (Whitelist Pattern)

Expose only specific commands:

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

Load modules from custom directories:

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

Restrict dangerous commands via configuration:

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

**Start stdio server:**

```bash
dotnet run --project PoshMcp.Server -- serve --transport stdio
```

**Configure in VS Code (`settings.json`):**

```json
{
  "mcpServers": {
    "poshmcp": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "C:\\path\\to\\poshmcp\\PoshMcp.Server",
        "--",
        "serve",
        "--transport",
        "stdio"
      ]
    }
  }
}
```

### HTTP Mode

Best for: multi-user deployments, web integration, cloud infrastructure.

**Characteristics:**
- RESTful API (JSON request/response)
- Per-user isolation
- Horizontal scaling capable
- Built-in health checks

**Start HTTP server:**

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

**1. Create/edit `.vscode/settings.json`:**

```json
{
  "github.copilot.advanced": {
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
}
```

**2. Start VS Code:**

```bash
cd C:\Users\YourUsername\source\poshmcp
code .
```

**3. Open the Copilot Chat panel:**
- Press `Ctrl+Shift+I` (or `Cmd+Shift+I` on macOS)
- Ask a question: "What services are running on this computer?"
- Copilot now has access to PoshMcp tools

### Other MCP Clients

PoshMcp works with any MCP-compatible client. Configure the client to connect to the stdio server:

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

**Problem:** `dotnet run` fails immediately.

**Solution:**
```bash
# Check .NET installation
dotnet --version

# Verify the SDK is 10.0+
dotnet --list-sdks

# Clean and rebuild
dotnet clean
dotnet build
```

### No Tools Discovered

**Problem:** `list-tools` returns empty list.

**Solution:**

```bash
# Evaluate tools with verbose output
dotnet run --project PoshMcp.Server -- evaluate-tools --verbose

# Check the configuration file
cat PoshMcp.Server/appsettings.json

# Verify CommandNames are correct
# (PowerShell is case-insensitive, but config must match)
```

### Tool Call Fails

**Problem:** Calling a tool returns an error.

**Solution:**

```bash
# Enable debug logging
export POSHMCP_LOG_LEVEL=debug
dotnet run --project PoshMcp.Server

# Check for excluded patterns
# Verify the command isn't in ExcludePatterns in appsettings.json

# Test the command manually
pwsh -Command "Get-Process"
```

### Module Installation Fails

**Problem:** Modules don't install at startup.

**Solution:**

```bash
# Check the startup logs
export POSHMCP_LOG_LEVEL=debug
dotnet run --project PoshMcp.Server

# Test module installation manually
pwsh -Command "Install-Module Az.Accounts -Force"

# Increase the installation timeout
# Set InstallTimeoutSeconds in appsettings.json to 600+
```

### Slow Performance

**Problem:** Tool calls are slow.

**Solution:**

- **Enable result caching:**
  ```json
  {
    "Performance": {
      "EnableResultCaching": true
    }
  }
  ```

- **Pre-install modules in Docker:**
  ```bash
  docker build --build-arg MODULES="Az.Accounts Az.Resources" -t poshmcp:fast .
  ```

- **Use out-of-process mode for module isolation:**
  ```bash
  export POSHMCP_RUNTIME_MODE=out-of-process
  dotnet run --project PoshMcp.Server
  ```

### Connection Issues

**Problem:** Client can't connect to the server.

**Solution:**

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

**Configuration (`appsettings.json`):**

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

**Configuration (`appsettings.json`):**

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

**Configuration (`appsettings.json`):**

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

**Configuration (`appsettings.modules.json`):**

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
