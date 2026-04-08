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

## Docker & Container Support

PoshMcp supports containerized deployment using a **two-tier architecture pattern**.

### Architecture
- **Base Image (`poshmcp:latest`)** — Contains only the MCP server runtime, no modules
- **Derived Images** — User-created images that extend the base with modules, config, and startup scripts

This separation ensures clean boundaries: base = runtime, derived = customization.

### Local Build & Run

**Prerequisites:** Docker or Podman, Docker Compose or Podman Compose

**Build the base image:**
```bash
# Using helper script (Windows)
.\docker.ps1 build

# Or direct podman/docker
podman build -t poshmcp:latest .
```

**Run as web server (HTTP on port 8080):**
```bash
podman run -d -p 8080:8080 -e POSHMCP_MODE=web --name poshmcp-web poshmcp:latest
curl http://localhost:8080/health
```

**Run as stdio server:**
```bash
podman run -it -e POSHMCP_MODE=stdio poshmcp:latest
```

**Using docker-compose:**
```bash
# Start web server
podman-compose --profile web up -d

# Start stdio server
podman-compose --profile stdio up -d

# View logs
podman-compose logs -f

# Stop all
podman-compose down
```

### Building Custom Images

Create a derived image with pre-installed modules (reduces startup time):

```dockerfile
FROM poshmcp:latest
USER root
COPY install-modules.ps1 /tmp/
ENV INSTALL_PS_MODULES="Pester PSScriptAnalyzer Az.Accounts"
RUN pwsh /tmp/install-modules.ps1 && rm /tmp/install-modules.ps1
COPY my-appsettings.json /app/web/appsettings.json
USER appuser
```

Build with:
```bash
podman build -f examples/Dockerfile.user -t poshmcp-custom:latest .
podman run -d -p 8080:8080 -e POSHMCP_MODE=web poshmcp-custom:latest
```

### Environment Variables
- `POSHMCP_MODE=web` — Run HTTP server (default)
- `POSHMCP_MODE=stdio` — Run stdio MCP server
- `ASPNETCORE_ENVIRONMENT=Production` — Production mode
- `INSTALL_PS_MODULES` — Space-separated module names with optional version constraints (e.g., `"Pester@>=5.0.0 Az.Accounts"`)

### Key Files
- **`Dockerfile`** — Base image definition
- **`docker-compose.yml`** — Orchestration configuration with `web` and `stdio` profiles
- **`docker.ps1`** — Windows helper script (build, run, stop, logs, clean commands)
- **`install-modules.ps1`** — PowerShell module installer (used in derived images)
- **`examples/Dockerfile.*`** — Template Dockerfiles (user, azure, custom patterns)

### Common Tasks
- **Inspect modules in container:** `podman run --rm poshmcp:latest pwsh -Command 'Get-Module -ListAvailable'`
- **Run with custom config:** `podman run -d -v /path/to/appsettings.json:/app/web/appsettings.json poshmcp:latest`
- **Monitor startup (web mode):** Watch logs until `Application started` appears; health endpoint available at `http://localhost:8080/health`

### Documentation
- **[DOCKER.md](../DOCKER.md)** — Complete Docker deployment guide
- **[examples/](../examples/)** — Docker Compose examples and sample Dockerfiles

When working on this codebase, focus on maintaining the separation between MCP protocol handling and PowerShell execution, ensure proper error handling and logging, add comprehensive tests for any new functionality, and maintain the two-tier Docker architecture pattern for containerized deployments.
