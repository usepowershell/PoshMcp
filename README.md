# PoshMcp

**Transform PowerShell into AI-consumable tools with zero code changes.**

PoshMcp is a [Model Context Protocol](https://modelcontextprotocol.io) (MCP) server that dynamically exposes PowerShell scripts, cmdlets, and modules as secure, discoverable tools for AI agents and automation platforms. Built for PowerShell experts who want to extend their automation capabilities to AI-powered workflows.

---

## What is PoshMcp?

PoshMcp bridges the gap between traditional PowerShell automation and modern AI interfaces. It provides:

- **Automatic Tool Discovery**: PowerShell commands become AI tools without manual registration
- **Persistent State**: Variables, functions, and modules persist across command executions
- **Flexible Deployment**: Run as stdio server (for MCP clients) or HTTP server (for web integration)
- **Enterprise Ready**: Built on .NET 8 with OpenTelemetry, health checks, and Azure Managed Identity support

**Perfect for:** DevOps engineers, system administrators, and PowerShell toolmakers who want to democratize access to their automation scripts.

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

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- PowerShell 7.x (included via Microsoft.PowerShell.SDK)

### Installation

```bash
# Clone the repository
git clone https://github.com/yourusername/poshmcp.git
cd poshmcp

# Build the project
dotnet build

# Run stdio server (for MCP clients)
dotnet run --project PoshMcp.Server

# OR run web server (for HTTP access)
dotnet run --project PoshMcp.Web
```

### Docker Deployment

```bash
# Build and run web server
./docker.sh build
./docker.sh run web

# Build with pre-installed PowerShell modules (faster startup)
./docker.sh build --modules "Pester PSScriptAnalyzer"
./docker.sh run web

# Access at http://localhost:8080
curl http://localhost:8080/health | jq
```

**Performance Tip:** Pre-install PowerShell modules at build time to reduce container startup time from ~30s to <1s. See [DOCKER.md](DOCKER.md) for details.

See [DOCKER.md](DOCKER.md) for detailed Docker configuration, including custom image patterns and module pre-installation.

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
    "FunctionNames": [
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
dotnet run --project PoshMcp.Server -- create-config

# Update the active configuration file (resolved with doctor rules)
dotnet run --project PoshMcp.Server -- update-config --add-function Get-Process

# Automation-friendly update (skip interactive advanced prompts)
dotnet run --project PoshMcp.Server -- update-config --non-interactive --add-module Az.Accounts
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
- [Environment Customization Guide](docs/ENVIRONMENT-CUSTOMIZATION.md) - Comprehensive guide with use cases
- [examples/](examples/) - Docker Compose examples and sample configurations

---

## Architecture

PoshMcp is organized into three main projects:

- **PoshMcp.Server**: stdio-based MCP server for direct client integration
- **PoshMcp.Web**: HTTP-based server with multi-user isolation
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

- **[DESIGN.md](DESIGN.md)** — Architecture and design philosophy
- **[DOCKER.md](DOCKER.md)** — Docker deployment guide
- **[docs/ENVIRONMENT-CUSTOMIZATION.md](docs/ENVIRONMENT-CUSTOMIZATION.md)** — Environment customization guide
- **[docs/IMPLEMENTATION-GUIDE.md](docs/IMPLEMENTATION-GUIDE.md)** — Implementation guide for developers
- **[infrastructure/azure/](infrastructure/azure/README.md)** — Azure deployment documentation
- **[PoshMcp.Tests/README.md](PoshMcp.Tests/README.md)** — Test organization and guidelines
- **[examples/](examples/)** — Configuration examples and Docker Compose files

---

## Configuration

### Stdio Mode (MCP Clients)

Configure in your MCP client settings:

```json
{
  "mcpServers": {
    "poshmcp": {
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/PoshMcp.Server"]
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
    "FunctionNames": ["Get-Process", "Get-Service"],
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

- **`FunctionNames`**: Array of specific PowerShell function names to expose
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

- ✅ **Phase 1 Complete**: Health checks and correlation IDs
- 🔄 **Phase 2 In Progress**: Structured error codes and configuration validation
- 📋 **Planned**: Command timeouts, circuit breakers, enhanced metrics

See [.squad/decisions.md](.squad/decisions.md) for architectural decisions and [.squad/quick-wins-summary.md](.squad/quick-wins-summary.md) for implementation details.

---

## Resources

- **[Model Context Protocol](https://modelcontextprotocol.io)** - MCP specification and documentation
- **[PowerShell Documentation](https://docs.microsoft.com/powershell/)** - PowerShell reference
- **[.NET 8 Documentation](https://docs.microsoft.com/dotnet/)** - .NET platform documentation
- **[Azure Container Apps](https://learn.microsoft.com/azure/container-apps/)** - Azure deployment guide

---

## Support

- **Issues**: [GitHub Issues](../../issues) - Report bugs or request features
- **Discussions**: [GitHub Discussions](../../discussions) - Ask questions and share ideas
- **Documentation**: See the `infrastructure/azure/` directory for deployment guides

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
