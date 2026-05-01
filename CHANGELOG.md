# Changelog

All notable changes to this project will be documented here.

## [0.9.2] - 2026-05-01

### Fixed
- **Security: Authentication bypass** — `AddPoshMcpAuthentication()` was not registering `AuthenticationConfiguration` with the .NET options system (`services.Configure<>()`). As a result, `IOptions<AuthenticationConfiguration>` always resolved to its default value (`Enabled = false`) regardless of `appsettings.json` settings. The middleware gates (`UseAuthentication`, `UseAuthorization`) and endpoint authorization (`RequireAuthorization("McpAccess")`) were silently skipped, allowing unauthenticated requests through even when `Authentication.Enabled: true` was configured. Fixed by adding `services.Configure<AuthenticationConfiguration>()` unconditionally in `AddPoshMcpAuthentication()`.
- Adds 3 regression tests in `AuthenticationServiceExtensionsTests` covering auth-enabled, auth-disabled, and missing-section scenarios.

## [0.9.1] - 2026-05-01

### Added
- **Authentication and identity diagnostics in doctor/troubleshooting tool** — Both `poshmcp doctor` and the `get-configuration-troubleshooting` MCP tool now include:
  - `authentication` section: enabled state, configured scheme types (JWT Bearer / API Key), authority/audience presence, key count (no secrets), default policy scopes and roles, protected resource URI, and CORS origins
  - `identity` section: caller identity when available in HTTP context — authenticated state, authentication scheme, name, scopes, and roles; `available: false` in CLI/stdio contexts where no HTTP context exists

### Removed
- `ConfigurationTroubleshootingTools.cs` dead code class — was never instantiated; real implementation lives in `CreateConfigurationTroubleshootingToolInstance` in `Program.cs`

## [0.9.0] - 2026-04-29

### Added
- **Application Insights integration** — Optional Azure Application Insights telemetry via OpenTelemetry. Enable with `ApplicationInsights.Enabled: true` in `appsettings.json`.
- `ApplicationInsightsOptions` configuration model with `Enabled`, `ConnectionString`, and `SamplingPercentage` properties.
- `ConfigureApplicationInsights()` method registers Azure Monitor OpenTelemetry when enabled, with support for connection string from config or `APPLICATIONINSIGHTS_CONNECTION_STRING` environment variable.
- Configurable sampling percentage (1–100) to control telemetry volume.
- `poshmcp doctor` validation for Application Insights configuration — validates connection string format and sampling range without network calls.
- 6 unit tests for `ConfigureApplicationInsights` covering enabled/disabled states, env var fallback, and sampling config.
- 4 integration tests for HTTP server with Application Insights enabled covering startup, logging, health endpoints, and tool discovery.
- `Azure.Monitor.OpenTelemetry.AspNetCore 1.4.0` package reference.

## [0.8.11] - 2026-04-24

### Fixed
- `poshmcp build` now works correctly when run outside the PoshMcp repository directory. The embedded Dockerfile is now materialized to a temporary file before invoking docker, preventing "Dockerfile not found" errors.

## [0.8.10] - 2026-04-24

### Added
- `--appsettings` option for `poshmcp build` command allows users to bundle a local `appsettings.json` file into their container image at build time, simplifying configuration management in containerized deployments.

## [0.8.9] - 2026-04-24

### Added
- `examples/Dockerfile.user` now includes documented PSModule paths showing where PowerShell modules are available in the container (`/usr/local/share/powershell/Modules` for AllUsers, `/opt/microsoft/powershell/7/Modules` for built-in, `/home/appuser/.local/share/powershell/Modules` for CurrentUser).
- Commented `COPY` directive examples in `Dockerfile.user` demonstrating how to easily copy local modules and startup scripts into the container.

## [0.8.8] - 2026-04-24

### Changed
- `poshmcp build --generate-dockerfile` now emits a user deployment template based on the published `ghcr.io/usepowershell/poshmcp/poshmcp` base image instead of the source build Dockerfile
- `install-modules.ps1` is now bundled in the base container image at `/app/install-modules.ps1` — generated Dockerfiles no longer require users to have this script locally
- `examples/Dockerfile.user` updated to reference the bundled script path and use the published base image

### Fixed
- Generated Dockerfile was incorrectly using the base image's own source Dockerfile as the template
