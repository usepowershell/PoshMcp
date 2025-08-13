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

### Authentication Configuration (PoshMcp.Web)

The web server supports Entra ID (Azure AD) authentication for enterprise security. Authentication is disabled by default for development scenarios.

#### `appsettings.json` Authentication Configuration
```json
{
  "EntraId": {
    "Enabled": false,
    "TenantId": "your-tenant-id",
    "ClientId": "your-client-id",
    "RequireHttpsMetadata": true,
    "RequiredScopes": [],
    "ValidateIssuer": true,
    "ValidateAudience": true,
    "ValidateLifetime": true
  }
}
```

#### EntraId Configuration Options

- **`Enabled`**: Whether authentication is enabled (default: `false`)
- **`TenantId`**: Azure AD tenant ID (required when enabled)
- **`ClientId`**: Azure AD application client ID (required when enabled)
- **`Audience`**: Token audience for validation (defaults to ClientId if not specified)
- **`Authority`**: Custom authority URL (defaults to `https://login.microsoftonline.com/{TenantId}/v2.0`)
- **`RequireHttpsMetadata`**: Whether to require HTTPS for metadata discovery (default: `true`)
- **`RequiredScopes`**: Array of required scopes for access (e.g., `["api.read", "api.write"]`)
- **`ValidateIssuer`**: Whether to validate token issuer (default: `true`)
- **`ValidateAudience`**: Whether to validate token audience (default: `true`)
- **`ValidateLifetime`**: Whether to validate token lifetime (default: `true`)

#### Setting up Entra ID Authentication

1. **Register an application in Azure AD**:
   - Go to Azure Portal → Azure Active Directory → App registrations
   - Create a new registration with appropriate redirect URIs
   - Note the Application (client) ID and Directory (tenant) ID

2. **Configure the application**:
   ```json
   {
     "EntraId": {
       "Enabled": true,
       "TenantId": "12345678-1234-1234-1234-123456789012",
       "ClientId": "87654321-4321-4321-4321-210987654321",
       "RequiredScopes": ["api://your-app-id/access"]
     }
   }
   ```

3. **Obtain access tokens**:
   - Use Azure CLI: `az account get-access-token --resource api://your-client-id`
   - Use browser-based flows for interactive scenarios
   - Use client credentials flow for service-to-service scenarios

4. **Make authenticated requests**:
   ```bash
   curl -X POST http://localhost:5001 \
     -H "Authorization: Bearer YOUR_ACCESS_TOKEN" \
     -H "Content-Type: application/json" \
     -d '{"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {...}}'
   ```

#### Development vs Production

- **Development**: Set `RequireHttpsMetadata: false` for local testing
- **Production**: Always use `RequireHttpsMetadata: true` and ensure HTTPS endpoints
- **Testing**: Authentication can be completely disabled by setting `Enabled: false`



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
