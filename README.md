# PowerShell MCP Server

This is a Model Context Protocol (MCP) server that provides access to a persistent PowerShell runspace, allowing commands to maintain state across multiple invocations.

## Features

- **Persistent PowerShell Runspace**: Variables, functions, and modules persist across command executions
- **Dynamic Tool Discovery**: Automatically discovers PowerShell commands and exposes them as MCP tools
- **State Management**: Maintains session state including variables and custom functions
- **Proper Cleanup**: Automatically disposes resources on application shutdown

## Getting Started

### Prerequisites

- .NET 8.0 SDK
- PowerShell 7.x (included via Microsoft.PowerShell.SDK package)

### Building the Project

```bash
dotnet build
```

### Running the MCP Server

```bash
dotnet run
```

The server will start and listen for MCP protocol messages via stdio.


### Running the MCP Server (Web)

```bash
dotnet run --project PoshMCP.Web
```

The server will start and listen for MCP protocol messages via HTTP.

### Development in VS Code

This project includes VS Code configuration files:

- `.vscode/launch.json` - Debug configurations
- `.vscode/tasks.json` - Build and run tasks
- `.vscode/settings.json` - Project-specific settings
- `.vscode/mcp.json` - MCP server configuration

#### Available VS Code Tasks

- **Build**: `Ctrl+Shift+P` → "Tasks: Run Task" → "build"
- **Run MCP Server**: `Ctrl+Shift+P` → "Tasks: Run Task" → "run-mcp-server"
- **Watch**: `Ctrl+Shift+P` → "Tasks: Run Task" → "watch" (auto-rebuilds on changes)

#### Debugging

1. Set breakpoints in your code
2. Press `F5` or use "Run and Debug" → "Launch PowerShell MCP Server"

## Usage

Once the MCP server is running, it exposes PowerShell commands as tools that can be called by MCP clients. The server automatically discovers available PowerShell commands and includes several built-in utility tools.

### Core Utility Tools

- `get-last-command-output` - Retrieves cached output from the last executed PowerShell command
- `sort-last-command-output` - Sorts cached command output using Sort-Object with optional property and descending parameters
- `filter-last-command-output` - Filters cached command output using Where-Object with PowerShell filter scripts
- `group-last-command-output` - Groups cached command output using Group-Object with property-based grouping

### Persistent State

The PowerShell runspace maintains state between calls:

```powershell
# Set a variable in one call
$MyVariable = "Hello World"

# Access it in another call
Write-Output $MyVariable  # Outputs: Hello World
```

## Architecture

The project is organized into several key components:

### Core Components

- **Program.cs**: Main entry point and MCP server setup
- **McpToolFactoryV2.cs**: Factory for creating MCP tools from PowerShell commands
- **PowerShell/** directory contains the PowerShell integration layer:

### PowerShell Integration Layer

- **PowerShellRunspaceHolder**: Singleton pattern for persistent PowerShell runspace management
- **PowerShellAssemblyGenerator**: Dynamic assembly generation for PowerShell commands with caching, sorting, and filtering capabilities
- **PowerShellCleanupService**: Hosted service for proper resource cleanup and state management
- **PowerShellConfiguration**: Configuration and setup for PowerShell runspace
- **IPowerShellRunspace & PowerShellRunspaceImplementations**: Thread-safe PowerShell execution interfaces
- **PowerShellSchemaGenerator**: JSON schema generation for PowerShell command parameters
- **PowerShellParameterUtils & PowerShellObjectSerializer**: Parameter processing and object serialization utilities

### Key Features

- **Thread-Safe Execution**: Proper async synchronization for concurrent PowerShell operations
- **State Persistence**: Variables, functions, and modules persist across command executions
- **Dynamic Tool Discovery**: Automatically discovers PowerShell commands and exposes them as MCP tools
- **Caching & Utilities**: Built-in sort and filter operations on cached command output

## Configuration

The server can be configured through:

- **Configuration files**: `appsettings.json` and environment-specific variants
- **Environment variables** (e.g., `DOTNET_ENVIRONMENT`)
- **PowerShell execution policy** (set to Bypass in the runspace)
- **Custom initialization scripts** in the PowerShell runspace

### Configuration Files

The server uses standard .NET configuration files:

#### `appsettings.json` (Default Configuration)
```json
{
    "Logging": {
        "LogLevel": {
            "Default": "Information",
            "Microsoft.Hosting.Lifetime": "Information"
        }
    },
    "PowerShellConfiguration": {
        "FunctionNames": [
            "Get-SomeData",
            "Get-Process",
            "Get-Service",
            "Get-ChildItem"
        ],
        "IncludePatterns": [
            "Get-*"
        ]
    }
}
```

#### Environment-Specific Configuration Files

- **`appsettings.azure.json`**: Azure-specific configuration with exclude patterns for sensitive Azure commands
- **`appsettings.modules.json`**: Module-based configuration that imports specific PowerShell modules

#### PowerShellConfiguration Options

- **`FunctionNames`**: Array of specific PowerShell function names to expose as MCP tools
- **`Modules`**: Array of PowerShell modules to import (e.g., `"Microsoft.PowerShell.Management"`)
- **`IncludePatterns`**: Array of wildcard patterns for functions to include (e.g., `"Get-*"`)
- **`ExcludePatterns`**: Array of wildcard patterns for functions to exclude from exposure
- **`EnableDynamicReloadTools`**: Boolean to enable/disable configuration reload tools (default: `false`)
- **`InitializationScriptPath`**: Path to a PowerShell script (.ps1) to run when initializing runspaces (optional)

### Custom Initialization Scripts

You can specify a custom PowerShell initialization script that runs when PowerShell runspaces are created. This allows you to set up your environment, define custom functions, import modules, and configure session state.

#### Configuration

Add the `InitializationScriptPath` property to your `appsettings.json`:

```json
{
    "PowerShellConfiguration": {
        "FunctionNames": ["Get-Process", "Get-Service"],
        "InitializationScriptPath": "scripts/my-init.ps1"
    }
}
```

The path can be:
- **Absolute**: `/full/path/to/script.ps1`
- **Relative**: `scripts/init.ps1` (relative to the application directory)

If the script file doesn't exist or is not specified, the server will use the default initialization script.

#### Example Initialization Script

See [scripts/example-init.ps1](scripts/example-init.ps1) for a comprehensive example. Here's a simple example:

```powershell
# my-init.ps1 - Custom initialization script

# Import required modules
Import-Module Az.Accounts -ErrorAction SilentlyContinue

# Set up global variables
$global:MyApiUrl = "https://api.example.com"
$global:SessionStartTime = Get-Date

# Define custom helper functions
function Get-MyEnvironmentInfo {
    return @{
        ApiUrl = $global:MyApiUrl
        StartTime = $global:SessionStartTime
        WorkingDirectory = (Get-Location).Path
    }
}

# Configure preferences
$ErrorActionPreference = 'Stop'

Write-Host "Custom environment initialized" -ForegroundColor Green
```

#### How It Works

1. **Startup**: The script is loaded once at application startup and cached for performance
2. **Stdio Server**: Uses a single shared runspace with the initialization script
3. **Web Server**: Each HTTP session gets its own isolated runspace, all using the same initialization script
4. **State Persistence**: Variables and functions defined in the script persist for the lifetime of each runspace

#### Use Cases

- **Module Loading**: Pre-import frequently used PowerShell modules
- **Environment Setup**: Configure API endpoints, connection strings, or credentials
- **Custom Functions**: Define utility functions available to all MCP tools
- **Default Settings**: Set error preferences, output formatting, or working directories
- **Authentication**: Initialize authentication contexts (e.g., Azure, AWS credentials)

### Azure Deployment and Managed Identity

When running the server in Azure (e.g., Azure Container Instances, Azure Container Apps, or Azure Kubernetes Service), the container automatically supports Azure Managed Identity for secure access to Azure resources. No additional code configuration is required - the PowerShell commands executed within the container can leverage the managed identity assigned to the Azure resource.

For example:
```powershell
# PowerShell commands automatically use the container's managed identity
Connect-AzAccount -Identity
Get-AzResource
```

See [DOCKER.md](DOCKER.md) for container deployment instructions.

## Testing and Debugging

### Interactive Test Client

The project includes an interactive CLI test client that allows you to test and debug the MCP server:

```bash
# Run the test client (builds and starts automatically)
./run-test-client.sh

# Or manually
cd TestClient
dotnet run
```

#### Test Client Commands

The test client provides an interactive shell with the following commands:

- `help` - Show available commands
- `init` - Send initialize request to server
- `list-tools` - List all available tools from the server
- `ping` - Send a ping request
- `call <tool-name>` - Call a tool without parameters
- `call-with-params <tool> <params>` - Call a tool with parameters
- `raw <json>` - Send raw JSON message
- `quit`/`exit` - Exit the client

#### Example Test Session

```
MCP> init
MCP> list-tools
MCP> call get-child-item-items
MCP> call get-last-command-output
MCP> call sort-last-command-output
MCP> call-with-params filter-last-command-output '{"filterScript": "$_.Name -like \'dotnet\'"}'
MCP> call-with-params group-last-command-output '{"property": "Extension"}'
MCP> quit
```

The test client automatically:
- Starts the MCP server as a subprocess
- Formats and displays JSON requests/responses
- Handles MCP protocol communication
- Provides error handling and logging

## Development
