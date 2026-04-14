# Trait-based test filtering

The Azure integration tests use xUnit traits for fine-grained control over test execution. This allows you to run specific subsets of tests based on categories, speed, cost, or requirements.

## Available Traits

### Category Traits

Tests are tagged with one or more categories:

| Category | Description | Tests |
|----------|-------------|-------|
| `Integration` | All integration tests | All 3 tests |
| `Docker` | Requires Docker Desktop | BuildBaseImage, BuildCustom |
| `Azure` | Requires Azure CLI & credentials | BuildCustom, Deploy |

### Speed Traits

Tests are classified by execution time:

| Speed | Duration | Tests |
|-------|----------|-------|
| `Slow` | 2-5 minutes | BuildBaseImage, BuildCustom |
| `VerySlow` | 5-10 minutes | DeployToAzure |

### Cost Traits

Tests that incur Azure costs:

| Cost | Estimated | Tests |
|------|-----------|-------|
| `Expensive` | ~$0.50-1.00 | DeployToAzure |

### Requirement Traits

Explicit prerequisites needed:

| Requires | Description | Tests |
|----------|-------------|-------|
| `Docker` | Docker Desktop running | All 3 tests |
| `BaseImage` | Base image already built | BuildCustom |
| `AzureCLI` | Azure CLI installed | Deploy |
| `AzureCredentials` | Azure authentication | Deploy |

## Using Traits with dotnet test

### Filter by Category

```bash
# Run only Docker tests
dotnet test --filter "Category=Docker"

# Run Azure tests
dotnet test --filter "Category=Azure"

# Run integration tests (all)
dotnet test --filter "Category=Integration"
```

### Filter by Speed

```bash
# Run only fast tests (exclude slow)
dotnet test --filter "Speed!=Slow&Speed!=VerySlow"

# Run slow tests only
dotnet test --filter "Speed=Slow|Speed=VerySlow"
```

### Filter by Cost

```bash
# Exclude expensive tests (free tests only)
dotnet test --filter "Cost!=Expensive"

# Run only expensive tests
dotnet test --filter "Cost=Expensive"
```

### Combine Multiple Filters

```bash
# Docker tests that aren't expensive
dotnet test --filter "Category=Docker&Cost!=Expensive"

# Integration tests that are fast
dotnet test --filter "Category=Integration&Speed!=Slow&Speed!=VerySlow"

# Azure tests that aren't very slow
dotnet test --filter "Category=Azure&Speed!=VerySlow"
```

## Using Traits with Helper Script

The `run-azure-integration-tests.ps1` script supports trait filtering:

### Filter by Category

```powershell
# Run only Docker-related tests
.\run-azure-integration-tests.ps1 -Category "Docker"

# Run integration tests excluding Azure deployment
.\run-azure-integration-tests.ps1 -Category "Integration" -ExcludeCategory "Azure"
```

### Speed Filtering

```powershell
# Run only fast tests
.\run-azure-integration-tests.ps1 -FastOnly

# Run all tests (including slow)
.\run-azure-integration-tests.ps1
```

### Cost Filtering

```powershell
# Exclude expensive tests (no Azure costs)
.\run-azure-integration-tests.ps1 -ExcludeExpensive

# Run specific test without expensive tests
.\run-azure-integration-tests.ps1 -TestName Base -ExcludeExpensive
```

### Combined Filtering

```powershell
# Fast Docker tests only
.\run-azure-integration-tests.ps1 -Category "Docker" -FastOnly

# Integration tests that don't cost money
.\run-azure-integration-tests.ps1 -Category "Integration" -ExcludeExpensive

# Multiple categories
.\run-azure-integration-tests.ps1 -Category "Docker","Integration" -ExcludeCategory "Azure"
```

## Test Matrix

| Test | Categories | Speed | Cost | Requires |
|------|-----------|-------|------|----------|
| **BuildBaseImage** | Integration, Docker | Slow | Free | Docker |
| **BuildCustom** | Integration, Docker, Azure | Slow | Free | Docker, BaseImage |
| **DeployToAzure** | Integration, Azure, Docker | VerySlow | Expensive | Docker, AzureCLI, AzureCredentials |

## CI/CD Examples

### GitHub Actions

```yaml
# Run only free tests in PR builds
- name: Run Free Integration Tests
  run: |
    dotnet test --filter "Category=Integration&Cost!=Expensive"

# Run full suite on main branch
- name: Run All Integration Tests
  if: github.ref == 'refs/heads/main'
  run: |
    dotnet test --filter "Category=Integration"
```

### Azure DevOps

```yaml
# Fast feedback loop
- task: DotNetCoreCLI@2
  displayName: 'Run Fast Tests'
  inputs:
    command: 'test'
    arguments: '--filter "Speed!=Slow&Speed!=VerySlow"'

# Nightly comprehensive tests
- task: DotNetCoreCLI@2
  displayName: 'Run All Integration Tests'
  condition: eq(variables['Build.Reason'], 'Schedule')
  inputs:
    command: 'test'
    arguments: '--filter "Category=Integration"'
```

## VS Code Test Explorer

Traits appear as test properties in the Test Explorer. You can filter by trait directly in the UI:

1. Open Test Explorer (Testing icon in Activity Bar)
2. Use the filter box: `@trait:Category=Docker`
3. Or filter by speed: `@trait:Speed=Slow`

## Best Practices

### For Local Development

```powershell
# Quick feedback - fast tests only
.\run-azure-integration-tests.ps1 -FastOnly

# Pre-commit check - free tests
.\run-azure-integration-tests.ps1 -ExcludeExpensive
```

### For Pull Requests

```bash
# Run Docker tests (no Azure costs)
dotnet test --filter "Category=Docker&Cost!=Expensive"
```

### For Main Branch / Nightly

```bash
# Run everything
dotnet test --filter "Category=Integration"
```

### For Cost-Conscious CI

```bash
# Exclude expensive Azure deployments
dotnet test --filter "Category=Integration&Cost!=Expensive"
```

## Adding New Tests

When creating new integration tests, add appropriate traits:

```csharp
[Fact]
[Trait("Category", "Integration")]
[Trait("Category", "Docker")]
[Trait("Speed", "Slow")]
[Trait("Requires", "Docker")]
public async Task MyNewTest_ShouldSucceed()
{
    // Test implementation
}
```

### Trait Guidelines

**Category:**
- Always include `Integration` for integration tests
- Add specific categories: `Docker`, `Azure`, `Kubernetes`, etc.

**Speed:**
- `Fast`: < 30 seconds
- `Slow`: 30s - 5 minutes
- `VerySlow`: > 5 minutes

**Cost:**
- `Expensive`: Creates billable resources (>$0.10)
- Omit if free

**Requires:**
- List explicit dependencies: `Docker`, `AzureCLI`, `Kubernetes`, etc.

## See also

- [xUnit Trait Documentation](https://xunit.net/docs/running-tests-in-parallel#traits)
- [Azure integration test README](../PoshMcp.Tests/Integration/README.azure-integration.md) — full test documentation
- [Quick start guide](QUICKSTART-AZURE-INTEGRATION-TEST.md) — quick command reference
- [Test organization](../PoshMcp.Tests/README.md) — overall test structure
