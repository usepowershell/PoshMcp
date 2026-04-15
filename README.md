# PoshMcp

**Transform PowerShell into AI-consumable tools with zero code changes.**

PoshMcp is a [Model Context Protocol](https://modelcontextprotocol.io) (MCP) server that dynamically exposes PowerShell scripts, cmdlets, and modules as secure, discoverable tools for AI agents and automation platforms. Built for PowerShell experts who want to extend their automation capabilities to AI-powered workflows.

---

## What is PoshMcp?

PoshMcp bridges the gap between traditional PowerShell automation and modern AI interfaces. It provides:

- **Automatic Tool Discovery**: PowerShell commands become AI tools without manual registration
- **Persistent State**: Variables, functions, and modules persist across command executions
- **Flexible Deployment**: Run as stdio server (for MCP clients) or HTTP server (for web integration)
- **Enterprise Ready**: Built on .NET 10 with OpenTelemetry, health checks, and Azure Managed Identity support

**Perfect for:** DevOps engineers, system administrators, and PowerShell toolmakers who want to democratize access to their automation scripts.

Full documentation at https://usepowershell.github.io/PoshMcp/

---

## Quick Example

Expose a PowerShell command to an AI agent:

```powershell
# Your PowerShell command - no changes needed
Get-Service -Name "wuauserv"
```

PoshMcp automatically:
1. Discovers `Get-Service` and its parameters
2. Generates JSON schema for AI consumption
3. Exposes it as an MCP tool named `Get-Service`
4. Handles parameter validation and execution
5. Returns structured results to the AI agent

**Result**: AI agents can now check Windows service status using natural language.

---

## Key Features

### For PowerShell Experts
- **Zero Boilerplate**: Existing scripts work without modification
- **State Preservation**: Variables and custom functions persist between calls
- **Pattern-Based Filtering**: Include/exclude commands via configuration
- **Rich Metadata**: Automatic extraction from `Get-Help` and `Get-Command`

### For Operations Teams
- **Multi-User Isolation**: Separate PowerShell runspaces in web mode
- **Health Monitoring**: `/health` and `/health/ready` endpoints for Kubernetes
- **Correlation IDs**: Request tracing across distributed systems
- **OpenTelemetry**: Built-in metrics and observability

### For Developers
- **Dual Transport**: stdio (MCP clients) or HTTP (web integration)
- **Dynamic Assembly Generation**: Efficient caching of command metadata
- **Thread-Safe Execution**: Async/await throughout with proper synchronization
- **VS Code Integration**: Debug configurations and tasks included

---

## Getting Started

### Prerequisites

- [.NET 10.0 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
- PowerShell 7.x (included via Microsoft.PowerShell.SDK)

### Installation

```bash
# Install the CLI tool
dotnet tool install -g poshmcp

# Create a default config in the current directory
poshmcp create-config

# Run stdio server (for MCP clients)
poshmcp serve --transport stdio

# Run HTTP server (for web integration)
poshmcp serve --transport http --port 8080
```

From source:

```bash
git clone https://github.com/microsoft/poshmcp.git
cd poshmcp

# Build the project
dotnet build

# Run stdio server (for MCP clients)
dotnet run --project PoshMcp.Server -- serve --transport stdio

# Run HTTP server (for web integration)
dotnet run --project PoshMcp.Server -- serve --transport http
```

### Container Deployment

PoshMcp uses the `poshmcp` CLI for containerized deployment—same commands locally or in containers.

```bash
# Build container image
poshmcp build                                          # Build base runtime image
poshmcp build --modules "Az.Accounts Pester"         # Pre-install modules
poshmcp build --type custom --tag myorg/poshmcp:v1   # Custom image with tag

# Run in a container
poshmcp run --mode http --port 8080                  # Run HTTP server
poshmcp run --mode stdio --interactive               # Run stdio server with tty
poshmcp run --config /path/appsettings.json --tag myorg/poshmcp:v1

# See all options
poshmcp build --help
poshmcp run --help
```

**Performance Tip:** Pre-install PowerShell modules at build time to reduce container startup time from ~30s to <1s. See [DOCKER.md](DOCKER.md) for architecture details.

Traditional docker commands still work—use `poshmcp build/run` for the standard approach. See [DOCKER.md](DOCKER.md) for detailed container architecture, Azure deployment, and advanced scenarios.

### Azure Container Apps

Deploy to Azure with one command:

```bash
cd infrastructure/azure
./deploy.sh
```

See [infrastructure/azure/README.md](infrastructure/azure/README.md) for deployment guide.

---

## Usage

### Basic Configuration

Configure which PowerShell commands to expose in `appsettings.json`:

```json
{
  "PowerShellConfiguration": {
    "CommandNames": [
      "Get-Process",
      "Get-Service"
    ],
    "IncludePatterns": [
      "Get-*"
    ]
  }
}
```

### CLI Configuration Commands

Manage configuration files directly from the PoshMcp CLI:

```bash
# Create default appsettings.json in the current directory
poshmcp create-config

# Update the active configuration file (resolved with doctor rules)
poshmcp update-config --add-function Get-Process

# Automation-friendly update (skip interactive advanced prompts)
poshmcp update-config --non-interactive --add-module Az.Accounts

# Update config for HTTP mode
poshmcp update-config --add-function Get-Service
```

### Built-in Utility Tools

PoshMcp includes commands for working with PowerShell output:

- **`get-last-command-output`**: Retrieve cached output from the last command
- **`sort-last-command-output`**: Sort results by property
- **`filter-last-command-output`**: Filter using PowerShell expressions
- **`group-last-command-output`**: Group results by property

### Persistent State Example

Variables persist across multiple calls:

```powershell
# Call 1: Set a variable
$MyVariable = "Hello from PoshMcp"

# Call 2: Access the variable
Write-Output $MyVariable
# Output: Hello from PoshMcp
```

---

## Environment Customization

PoshMcp supports rich environment customization to tailor the PowerShell session to your needs:

### Startup Scripts

Execute custom PowerShell code during initialization:

```json
{
  "PowerShellConfiguration": {
    "Environment": {
      "StartupScript": "$Global:CompanyName = 'Acme'; Write-Host 'Ready!'",
      "StartupScriptPath": "/config/startup.ps1"
    }
  }
}
```

### Module Installation

Install modules from PowerShell Gallery at startup:

```json
{
  "PowerShellConfiguration": {
    "Environment": {
      "InstallModules": [
        {
          "Name": "Az.Accounts",
          "MinimumVersion": "2.0.0",
          "Repository": "PSGallery"
        }
      ]
    }
  }
}
```

Notes:
- `TrustPSGallery` defaults to `false` and must be explicitly enabled if you want automatic PSGallery trust.
- `SetupTimeoutSeconds` defaults to `120` and applies to out-of-process setup initialization.

### Custom Module Paths

Load modules from local directories or mounted volumes:

```json
{
  "PowerShellConfiguration": {
    "Environment": {
      "ModulePaths": ["/mnt/shared-modules", "./custom-modules"],
      "ImportModules": ["MyCustomModule"]
    }
  }
}
```

**For complete documentation and examples, see:**
- [Environment Customization Guide](docs/articles/environment.md) - Comprehensive guide with use cases
- [examples/](examples/) - Docker Compose examples and sample configurations

---

## Out-of-Process PowerShell Runtime (Advanced)

For scenarios where PowerShell modules have incompatible dependencies or conflict with each other, PoshMcp provides optional **out-of-process hosting** — PowerShell runs in an isolated subprocess instead of the main server process.

### When to Use Out-of-Process

- **Module conflicts:** Modules with incompatible version requirements
- **Platform-specific issues:** Windows-only modules failing on Linux (Group Policy, WMI)
- **Type pollution:** AppDomain incompatibilities between modules
- **Initialization conflicts:** Modules with incompatible authentication or state setup

### Quick Start

```bash
# Start in out-of-process mode
dotnet run --project PoshMcp.Server -- serve --runtime-mode out-of-process

# Or with environment variable
export POSHMCP_RUNTIME_MODE=out-of-process
dotnet run --project PoshMcp.Server

# In appsettings.json
# Set PowerShellConfiguration.RuntimeMode to "OutOfProcess"
```

### Trade-offs

| Aspect | In-Process (Default) | Out-of-Process |
|--------|----------------------|-----------------|
| Latency | ~5 ms | ~120 ms |
| Memory | ~250 MB | ~330–370 MB |
| Module isolation | Shared (one failure affects all) | Isolated |
| Complexity | Simpler | More complex |

**For complete documentation, see [docs/articles/transport-modes.md](docs/articles/transport-modes.md) and [specs/out-of-process-execution.md](specs/out-of-process-execution.md).**

---

PoshMcp is organized into two main projects:

- **PoshMcp.Server**: MCP server supporting both stdio and HTTP transport (use `--transport stdio` or `--transport http`)
- **PoshMcp.Tests**: Comprehensive test suite (unit, functional, integration)

### Core Components

- **McpToolFactoryV2**: Dynamic tool schema generation from PowerShell commands
- **PowerShellRunspaceHolder**: Thread-safe, persistent PowerShell runspace management
- **PowerShellAssemblyGenerator**: Cached dynamic assembly generation for performance
- **OperationContext**: Correlation ID tracking for distributed tracing
- **Health Checks**: PowerShell runspace, assembly generation, and configuration validation

For architectural details, see [DESIGN.md](DESIGN.md).

---

## Documentation

### Getting Started
- **[README.md](README.md)** (this file) — Project overview, quick start, and basic configuration
- **[DOCKER.md](DOCKER.md)** — Docker deployment guide with base and custom image patterns
- **[DESIGN.md](DESIGN.md)** — Architecture and design philosophy

### Deployment & Infrastructure
- **[infrastructure/azure/README.md](infrastructure/azure/README.md)** — Azure Container Apps deployment guide
  - Prerequisites, multi-tenant support, resource sizing
  - Post-deployment verification and scaling
- **[examples/README.md](examples/README.md)** — Example configurations and Dockerfile templates
  - Detailed explanations of each example Dockerfile
  - Configuration file examples and startup scripts
  - Docker Compose orchestration examples

### Configuration & Customization
- **[docs/articles/environment.md](docs/articles/environment.md)** — PowerShell environment setup (startup scripts, modules, paths)
  - Module installation and importing
  - Startup script configuration (inline and file-based)
  - Environment-specific customization
- **[examples/startup.ps1](examples/startup.ps1)** — Comprehensive startup script example
- **[examples/azure-managed-identity-startup.ps1](examples/azure-managed-identity-startup.ps1)** — Azure authentication example

### Development & Testing
- **[PoshMcp.Tests/README.md](PoshMcp.Tests/README.md)** — Test organization and development guidelines
  - Unit, functional, and integration test categories
  - Running tests by category or trait
  - Contributing test guidelines
- **[docs/articles/ai-integration.md](docs/articles/ai-integration.md)** — AI client integration guide
- **[.github/copilot-instructions.md](.github/copilot-instructions.md)** — Development guidelines and conventions

### Runtime Modes
- **[docs/articles/transport-modes.md](docs/articles/transport-modes.md)** — Stdio and HTTP transport modes, including out-of-process guidance
  - Module isolation and conflict resolution
  - Configuration via CLI, environment variables, or appsettings.json
  - Performance characteristics and trade-offs
  - Troubleshooting and best practices

### Advanced Topics
- **[docs/articles/azure-integration.md](docs/articles/azure-integration.md)** — Azure deployment and integration guidance
- **[docs/archive/AZURE-INTEGRATION-TEST-SCENARIO.md](docs/archive/AZURE-INTEGRATION-TEST-SCENARIO.md)** — Azure integration test documentation (archived)
- **[docs/archive/QUICKSTART-AZURE-INTEGRATION-TEST.md](docs/archive/QUICKSTART-AZURE-INTEGRATION-TEST.md)** — Azure test quick reference (archived)
- **[docs/archive/TRAIT-BASED-TEST-FILTERING.md](docs/archive/TRAIT-BASED-TEST-FILTERING.md)** — Test filtering by category, speed, or cost (archived)
- **[docs/archive/INTEGRATION-CHECKLIST.md](docs/archive/INTEGRATION-CHECKLIST.md)** — Integration task checklist (archived)

### Docker & Container Registry
- **[DOCKER.md](DOCKER.md)** — Complete Docker deployment (see "Resources" section above)
- **[docs/archive/DOCKER-BUILD-QUICK-REF.md](docs/archive/DOCKER-BUILD-QUICK-REF.md)** — Docker build quick reference (archived)
- **[docs/archive/DOCKER-BUILD-MODULES.md](docs/archive/DOCKER-BUILD-MODULES.md)** — Legacy module building approach (archived, see DOCKER.md)

---

## Configuration

### Stdio Mode (MCP Clients)

Configure in your MCP client settings:

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

### Web Mode (HTTP)

Runs on port 8080 by default. Configure via `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  },
  "PowerShellConfiguration": {
    "CommandNames": ["Get-Process", "Get-Service"],
    "IncludePatterns": ["Get-*"],
    "ExcludePatterns": ["*-Dangerous*"]
  }
}
```

### Environment-Specific Configuration

- **`appsettings.json`**: Default configuration
- **`appsettings.azure.json`**: Azure-specific settings with exclude patterns
- **`appsettings.modules.json`**: Module-specific configuration for importing PowerShell modules

#### PowerShellConfiguration Options

- **`CommandNames`**: Array of specific PowerShell command names to expose
- **`Modules`**: Array of PowerShell modules to import (e.g., `Microsoft.PowerShell.Management`)
- **`IncludePatterns`**: Wildcard patterns for functions to include (e.g., `Get-*`)
- **`ExcludePatterns`**: Wildcard patterns for functions to exclude

### Azure Managed Identity Support

When deployed to Azure (Container Apps, Container Instances, AKS), PoshMcp automatically supports Azure Managed Identity for secure resource access with zero code changes:

```powershell
# PowerShell commands automatically use the managed identity
Connect-AzAccount -Identity
Get-AzResource
```

---

## Development

### VS Code Integration

This project includes complete VS Code configuration:

- **`.vscode/launch.json`** - Debug configurations for both servers
- **`.vscode/tasks.json`** - Build, run, and watch tasks
- **`.vscode/settings.json`** - Project-specific settings
- **`.vscode/mcp.json`** - MCP server configuration

#### Available Tasks

- **Build**: `Ctrl+Shift+P` → "Tasks: Run Task" → "build"
- **Run MCP Server**: `Ctrl+Shift+P` → "Tasks: Run Task" → "run-mcp-server"
- **Watch**: `Ctrl+Shift+P` → "Tasks: Run Task" → "watch" (auto-rebuilds on changes)

#### Debugging

1. Set breakpoints in your code
2. Press `F5` or use "Run and Debug" → "Launch PowerShell MCP Server"

### Testing

Run the comprehensive test suite:

```bash
# Run all tests
dotnet test

# Run specific test categories
dotnet test --filter "FullyQualifiedName~Unit"
dotnet test --filter "FullyQualifiedName~Integration"
```

See [PoshMcp.Tests/README.md](PoshMcp.Tests/README.md) for test organization details.

### Interactive Test Client

Test MCP server functionality with the included CLI client:

```bash
dotnet run --project TestClient

# Interactive commands:
# init - Initialize connection
# list-tools - View available tools
# call <tool-name> - Execute a tool
# call-with-params <tool> <params> - Execute with parameters
```

---

## Contributing

Contributions are welcome! This project uses a squad-based development approach with specialized team roles.

**Before contributing:**
1. Check existing issues or create a new one
2. Follow the coding conventions in [.github/copilot-instructions.md](.github/copilot-instructions.md)
3. Add tests for new functionality (see test organization in `PoshMcp.Tests/`)
4. Ensure all tests pass: `dotnet test`

**Development workflow:**
1. Fork the repository
2. Create a feature branch: `git checkout -b feature/my-feature`
3. Make your changes with tests
4. Run tests and ensure they pass
5. Commit with clear messages
6. Push and create a Pull Request

---

## Roadmap

Current focus areas:

- **Configuration and DX improvements**: Better CLI-driven configuration workflows and docs alignment
- **Runtime resilience**: Ongoing work around execution stability, serialization safety, and observability
- **Deployment patterns**: Continued improvements for container and Azure deployment guidance

See [.squad/decisions.md](.squad/decisions.md) for architectural decisions and [.squad/quick-wins-summary.md](.squad/quick-wins-summary.md) for implementation details.

---

## Resources

- **[Model Context Protocol](https://modelcontextprotocol.io)** - MCP specification and documentation
- **[PowerShell Documentation](https://docs.microsoft.com/powershell/)** - PowerShell reference
- **[.NET 10 Documentation](https://docs.microsoft.com/dotnet/)** - .NET platform documentation
- **[Azure Container Apps](https://learn.microsoft.com/azure/container-apps/)** - Azure deployment guide

---

## Support

- **Issues**: [GitHub Issues](https://github.com/usepowershell/poshmcp/issues) - Report bugs or request features
- **Discussions**: [GitHub Discussions](https://github.com/usepowershell/poshmcp/discussions) - Ask questions and share ideas
- **Documentation**: See [docs/index.md](docs/index.md) and [infrastructure/azure/README.md](infrastructure/azure/README.md)

---

## License

This project is under active development. License information will be added soon.

---

## Acknowledgements

Built with:
- [Model Context Protocol](https://modelcontextprotocol.io) - AI-to-tool communication standard
- [Microsoft.PowerShell.SDK](https://www.nuget.org/packages/Microsoft.PowerShell.SDK/) - PowerShell automation
- [OpenTelemetry](https://opentelemetry.io/) - Observability infrastructure

---

**Transform your PowerShell expertise into AI-powered tools. Get started today!**
