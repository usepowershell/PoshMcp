# Integration Test Assets

`integration/Modules` is a **vendored test-asset corpus** used **locally** to validate module loading and out-of-process hosting scenarios. This directory is **NOT part of the product runtime** and should not be used in production deployments.

If `./integration/Modules` (from the projecte root) does not exist:

```powershell
if (-not (Test-Path './integration/Modules/')) {
  mkdir integration/Modules
  push-location ./integration/Modules
  Save-Module Az -Path . -Force
  Save-Module Microsoft.Graph -path . -Force
}
```

---

## Scope & Usage Rules

### What `integration/Modules` Contains

- Partial, vendored PowerShell module fixtures
- Examples:
  - `Az.Accounts/` — Azure authentication module (incomplete, test-only)
  - `Microsoft.Graph/` — Microsoft Graph API (partial fixture)
  - Other vendor modules used by integration tests

### What `integration/Modules` is NOT

- ❌ NOT complete production modules
- ❌ NOT included in shipping/published artifacts
- ❌ NOT meant for end-user configurations
- ❌ NOT subject to semantic versioning constraints

### When to Use `integration/Modules`

✅ **Local validation only:**
- Testing out-of-process module loading behavior
- Running integration test suites (`[Trait("Category", "Integration")]`)
- Validating the local development environment
- Debugging module discovery logic

### When NOT to Use `integration/Modules`

❌ **Never in production:**
- Do not configure production servers to use `integration/Modules`
- Do not reference it in end-user documentation
- Do not rely on it for deployed services

---

## Local Development Workflow

### Testing Out-of-Process Runtime

```bash
# Use the provided test configuration
dotnet run --project PoshMcp.Server -- serve \
  --config examples/appsettings.outofprocess.integration-modules.json

# Or manually with environment variables
export POSHMCP_RUNTIME_MODE=out-of-process
export POSHMCP_MODULE_PATH=integration/Modules
dotnet run --project PoshMcp.Server
```

### Running Integration Tests

```bash
# Run only integration tests (uses integration/Modules)
dotnet test PoshMcp.Tests --filter "Category=Integration"

# Run with verbose output
dotnet test PoshMcp.Tests --filter "Category=Integration" -v d
```

---

## Maintenance & Development

### Updating the Corpus

When integration tests require new module fixtures:

1. Add/update the module folder under `integration/Modules/{ModuleName}`
2. Include only the minimal files needed for tests
3. Update the test that references the new module
4. Document the change in the test commit message

**Example:** Adding a new module fixture
```
integration/Modules/
├── Az.Accounts/          ← Add here for testing
│   ├── Az.Accounts.psd1
│   └── Az.Accounts.psm1
└── Microsoft.Graph/
    ├── Microsoft.Graph.psd1
    └── Microsoft.Graph.psm1
```

### Refactoring the Corpus

Scope rules for `integration/Modules` work:
- Changes under `integration/Modules/` should be limited to refreshing test fixtures
- Do not treat as part of product runtime content
- Use for end-to-end validation of out-of-process discovery, not production module management
- Keep fixtures minimal to reduce repository size

---

## References

- **Out-of-Process Documentation:** [docs/OUT-OF-PROCESS.md](../docs/OUT-OF-PROCESS.md)
- **Production Module Deployment:** [docs/ENVIRONMENT-CUSTOMIZATION.md](../docs/ENVIRONMENT-CUSTOMIZATION.md)
- **Integration Test Guidelines:** [PoshMcp.Tests/README.md](../PoshMcp.Tests/README.md)

## Current Integration Test Workstreams

### Runtime Stream
- Runtime-mode configuration and CLI wiring
- Startup ownership for out-of-process subprocess executor
- Discovery/execution integration with isolated runspaces
- End-to-end test coverage for out-of-process mode

### Corpus Stream
- Maintain the `integration/Modules` fixture set for local validation
- Ensure realistic module layouts for testing
- Keep fixtures minimal and focused on test scenarios
- Update fixtures when new out-of-process tests are added