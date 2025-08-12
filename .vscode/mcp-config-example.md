# MCP Client Configuration Example

To use this PowerShell MCP server with an MCP client, you can configure it like this:

## Claude Desktop Configuration

Add this to your Claude Desktop configuration file:

```json
{
  "mcpServers": {
    "powershell-server": {
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/PoshMcp/PoshMcp.csproj"],
      "env": {
        "DOTNET_ENVIRONMENT": "Development"
      }
    }
  }
}
```

## Generic MCP Client Configuration

```json
{
  "name": "powershell-server",
  "command": "dotnet",
  "args": ["run", "--project", "/path/to/PoshMcp/PoshMcp.csproj"],
  "cwd": "/path/to/PoshMcp",
  "env": {
    "DOTNET_ENVIRONMENT": "Development"
  }
}
```

## Running Locally for Testing

```bash
cd /home/stmuraws/source/PoshMcp
dotnet run
```

The server will communicate via stdio using the MCP protocol.

## Interactive Testing

Use the included test client for interactive testing and debugging:

```bash
# Run the interactive test client
./run-test-client.sh

# Or run via VS Code task: Ctrl+Shift+P → "Tasks: Run Task" → "run-test-client"
```

The test client provides commands like:
- `init` - Initialize the MCP connection
- `list-tools` - See available PowerShell tools
- `call Get-SomeData` - Test the sample function
- `help` - See all available commands
