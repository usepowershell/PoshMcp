# Copilot Instructions for PoshMcp

## Project Overview
PoshMcp is a Model Context Protocol (MCP) server implementation that exposes PowerShell functions as MCP tools. The server dynamically generates MCP tool schemas from PowerShell functions and handles execution through stdio communication.

## Architecture

### Core Components
- **PoshMcp.Server/**: Main MCP server implementation
  - `Program.cs`: Entry point and MCP server setup
  - `McpToolFactoryV2.cs`: Dynamic tool schema generation from PowerShell functions
  - `appsettings.json`: Configuration for available PowerShell functions
- **PoshMcp.Tests/**: Comprehensive test suite
  - `Integration/`: End-to-end tests with real MCP server processes
  - `Unit/`: Unit tests for individual components
  - `Functional/`: Feature-specific functional tests
- **TestClient/**: Simple test client for manual testing

### Key Technologies
- **.NET 8**: Primary runtime and framework
- **PowerShell SDK**: For executing PowerShell commands and introspection
- **Model Context Protocol**: JSON-RPC communication standard
- **Newtonsoft.Json**: JSON serialization and JToken manipulation
- **xUnit**: Testing framework with shared test infrastructure

## Development Guidelines

### PowerShell Function Integration
- Functions are configured in `appsettings.json` under `FunctionNames` array
- The server dynamically discovers function signatures using PowerShell reflection
- Parameter types are mapped to JSON Schema types automatically
- Use `McpToolFactoryV2` for adding new PowerShell function support

### JSON Handling Patterns
- Use `JObject`, `JArray`, and `JValue` from Newtonsoft.Json for response parsing
- Always check JSON token types before casting (JValue vs JObject)
- MCP responses can vary in structure - implement defensive parsing
- Use `JsonConvert.SerializeObject()` for proper JSON string serialization

### Testing Best Practices
- Integration tests use `InProcessMcpServer` with `ExternalMcpClient` for realistic stdio testing
- Extend `PowerShellTestBase` for consistent logging and test infrastructure
- Use shared server/client instances in integration tests for performance
- Add comprehensive debug logging for MCP communication troubleshooting

### MCP Protocol Implementation
- Follow JSON-RPC 2.0 specification strictly
- Implement proper error handling with MCP error codes
- Use stdio for client-server communication (not HTTP)
- Support standard MCP methods: `initialize`, `tools/list`, `tools/call`

### Configuration Management
- PowerShell functions are configured declaratively in JSON
- Support runtime configuration updates via MCP tools
- Implement configuration reload without server restart
- Validate configuration changes before applying

## Common Patterns

### Adding New PowerShell Functions
1. Add function name to `appsettings.json` `FunctionNames` array
2. The server will automatically discover and expose the function
3. Add integration tests to verify the new function works via MCP
4. Update documentation if the function has special requirements

### Error Handling
- PowerShell execution errors should be wrapped in MCP error responses
- Use appropriate HTTP-like status codes in MCP errors
- Log detailed error information for debugging
- Provide user-friendly error messages in MCP responses

### Performance Considerations
- PowerShell runspace creation is expensive - reuse when possible
- Cache function metadata to avoid repeated reflection
- Use background processes for long-running PowerShell operations
- Implement proper cleanup for PowerShell resources

## File Naming Conventions
- Use PascalCase for C# classes and methods
- Use kebab-case for configuration keys where appropriate
- Test files should end with `Tests.cs`
- Integration tests should be in `Integration/` namespace

## Debugging Tips
- Enable detailed logging in tests for MCP communication troubleshooting
- Use `TestOutputHelper` to capture logs in test output
- Check PowerShell execution contexts and runspace state
- Verify JSON serialization/deserialization with proper type checking

## Dependencies to Understand
- **Microsoft.Extensions.Hosting**: For background service hosting
- **Microsoft.Extensions.Logging**: Comprehensive logging infrastructure
- **Microsoft.PowerShell.SDK**: PowerShell automation and scripting
- **ModelContextProtocol.Server**: Core MCP server implementation
- **Newtonsoft.Json**: JSON manipulation and serialization

## File Standards
- No trailing whitespace

When working on this codebase, focus on maintaining the separation between MCP protocol handling and PowerShell execution, ensure proper error handling and logging, and add comprehensive tests for any new functionality.
