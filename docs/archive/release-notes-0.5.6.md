# PoshMcp v0.5.6

## Highlights

- Fixed authorization override resolution so generated MCP tool names (including snake_case and parameter-set suffix variants) resolve correctly to `PowerShellConfiguration.FunctionOverrides` entries.
- Added dedicated unit coverage for authorization override lookup precedence and ambiguous-name resolution.
- Aligned authentication and security documentation to consistently describe Entra ID (`JwtBearer`) and API key (`ApiKey`) configuration and precedence.
- Bumped tool package version from `0.5.5` to `0.5.6` in `PoshMcp.Server` package metadata.

## Security/Behavior Changes

- `AuthorizationHelpers.GetToolOverride` now evaluates multiple candidate keys for a tool call instead of only exact or underscore-to-hyphen normalization.
- Matching now includes:
  - Raw tool name from MCP request.
  - Underscore-to-hyphen normalized name.
  - Command-name reconstruction from snake_case (using configured command names).
  - Progressive suffix trimming for hyphenated names.
- Behavior impact: per-tool authorization settings in `FunctionOverrides` now apply more reliably to generated tool names, reducing mismatches between configured policy and runtime tool resolution.

## Documentation

- Updated authentication docs to use a unified "Authentication Guide" structure with explicit mode selection:
  - Entra ID (OAuth 2.1 / JwtBearer)
  - API key (ApiKey)
- Updated configuration and security docs with complete API key examples, including:
  - `Authentication.DefaultPolicy`
  - Per-key `Keys` role/scope claims
  - `PowerShellConfiguration.FunctionOverrides` precedence over default policy
- Tightened cross-links between authentication, configuration, and security articles for clearer operator guidance.

## Tests/Validation

- Added `PoshMcp.Tests/Unit/AuthorizationHelpersTests.cs` with targeted unit tests for:
  - Exact-name override precedence.
  - Generated tool name to configured command resolution.
  - Ambiguous command-name handling (prefers longer configured command match).
  - Legacy function-name fallback behavior.
  - Null result when no override applies.
- Release notes content is based on commit-level changes in:
  - `31fa637` (authorization behavior + tests)
  - `0d26be6` (documentation alignment)
  - `df8fcff` (version bump)

## Packaging/Distribution

- Updated `PoshMcp.Server/PoshMcp.csproj` package version:
  - From: `0.5.5`
  - To: `0.5.6`
- No new package ID or command-name changes in this patch; distribution remains the `poshmcp` .NET tool package.

## Upgrade Notes

- Recommended for deployments relying on per-tool authorization (`FunctionOverrides`) where generated MCP tool names include suffixes or naming transformations.
- No schema migration is required for this patch release.
- If you maintain custom auth policy mappings, validate expected override behavior for critical tools after upgrading.

## Known Gaps

- This release note reflects repository commits and added unit tests; no additional full integration test execution evidence is included here.