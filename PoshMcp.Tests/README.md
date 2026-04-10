# PoshMcp Test Organization and Guidelines

This document describes the organization of tests in the PoshMcp project, how to run them, and guidelines for contributing new tests.

## Overview

Tests are organized into three main categories based on their scope and dependencies:

- **Unit Tests**: Fast, isolated tests of individual components
- **Functional Tests**: Feature-level tests with limited external dependencies  
- **Integration Tests**: End-to-end tests validating interactions between components

Each category resides in its own directory under `PoshMcp.Tests/` with shared infrastructure in the `Shared/` folder.

## Directory Structure

```
PoshMcp.Tests/
├── Unit/                              # Isolated component tests
│   ├── ParameterTypeTests.cs
│   ├── OutputTypeTests.cs
│   ├── SimpleAssemblyTests.cs
│   └── ...
├── Functional/                        # Feature-level tests
│   ├── AssemblyGenerationTests.cs
│   ├── MethodExecutionTests.cs
│   ├── PowerShellCommandExecutionTests.cs
│   └── ...
├── Integration/                       # End-to-end tests
│   ├── McpServerIntegrationTests.cs
│   ├── IntegrationTests.cs
│   ├── CommandLineTests.cs
│   └── ...
├── Shared/                            # Shared test infrastructure
│   ├── PowerShellTestBase.cs
│   ├── TestOutputLogger.cs
│   └── ...
└── README.md                          # This file
```

## Test Categories

### 📁 Unit Tests (`Unit/` directory)

**Purpose**: Verify individual components in isolation with minimal dependencies.

**Characteristics**:
- Fast execution (< 100ms per test)
- No external dependencies (no PowerShell runspaces, file system, or network)
- Use mocks, stubs, and test doubles as needed
- Focus on boundary conditions and edge cases

**Files and Coverage**:
- **`SimpleAssemblyTests.cs`** - Basic test framework validation and simple assembly tests
- **`ParameterTypeTests.cs`** - PowerShell parameter type handling and validation
- **`OutputTypeTests.cs`** - PowerShell output type determination and processing

**Run unit tests only:**
```bash
dotnet test --filter "FullyQualifiedName~Unit"
```

### 📁 Functional Tests (`Functional/` directory)

**Purpose**: Verify complete features and workflows, but still focus on specific functionality.

**Characteristics**:
- Medium execution time (100ms - 1s per test)
- Limited external dependencies (may use real PowerShell instances but isolated)
- Test complete workflows end-to-end
- Can use file system or other test-specific resources

**Files and Coverage**:
- **`AssemblyGenerationTests.cs`** - Dynamic assembly generation from PowerShell commands
- **`MethodExecutionTests.cs`** - Generated method execution and behavior
- **`AdvancedMethodExecutionTests.cs`** - Complex method execution scenarios
- **`PowerShellCommandExecutionTests.cs`** - PowerShell command execution and caching
- **`ReturnTypeIntegrationTests.cs`** - Return type handling and integration
- **`McpServerSetupTests.cs`** - MCP server configuration and setup

**Run functional tests only:**
```bash
dotnet test --filter "FullyQualifiedName~Functional"
```

### 📁 Integration Tests (`Integration/` directory)

**Purpose**: Verify end-to-end scenarios and interactions between multiple components.

**Characteristics**:
- Longer execution time (1s+ per test)
- May have external dependencies (processes, Docker, Azure resources)
- Test complete workflows across the entire system
- Validate MCP protocol compliance
- May be marked with traits for selective execution (Category, Speed, Cost)

**Files and Coverage**:
- **`McpServerIntegrationTests.cs`** - Full MCP server integration with external clients
- **`IntegrationTests.cs`** - General component interactions
- **`ComplexIntegrationTests.cs`** - Complex end-to-end scenarios
- **`CommandLineTests.cs`** - Command-line interface and CLI integration
- **`AzureDeploymentIntegrationTests.cs`** - Azure deployment validation (when applicable)

**Run integration tests only:**
```bash
dotnet test --filter "FullyQualifiedName~Integration"
```

### 📁 Shared Infrastructure (`Shared/` directory)

**Purpose**: Provide common utilities and base classes for all test types.

**Components**:
- **`PowerShellTestBase.cs`** - Base class for PowerShell-related tests
  - Common PowerShell runspace setup
  - Test lifecycle management
  - Logging infrastructure
  - Use: `public class MyTests : PowerShellTestBase { }`

- **`TestOutputLogger.cs`** - Logger implementation for capturing test output
  - Integrates with xUnit test output
  - Captures logs from test execution
  - Use: Inherit from PowerShellTestBase to get automatic logging

## Running Tests

### Run All Tests

```bash
# All tests across all categories
dotnet test

# With verbose output
dotnet test --verbosity normal

# With detailed output including test names
dotnet test --verbosity detailed

# Stop after first failure
dotnet test --no-build --stop-on-first-failure
```

### Run Tests by Category

```bash
# Unit tests only (fastest)
dotnet test --filter "FullyQualifiedName~Unit"

# Functional tests only  
dotnet test --filter "FullyQualifiedName~Functional"

# Integration tests only
dotnet test --filter "FullyQualifiedName~Integration"

# Skip the slowest tests
dotnet test --filter "Speed!=VerySlow"
```

### Run Specific Test Files

```bash
# Run tests from one file
dotnet test --filter "FullyQualifiedName~AssemblyGenerationTests"

# Run tests from multiple files
dotnet test --filter "FullyQualifiedName~AssemblyGenerationTests | FullyQualifiedName~ParameterTypeTests"

# Run specific test method
dotnet test --filter "FullyQualifiedName=PoshMcp.Tests.Unit.ParameterTypeTests.TestMethod"
```

### Run Tests by Trait

Traits allow fine-grained filtering (mainly used for integration tests):

```bash
# Filter by category
dotnet test --filter "Category=Docker"
dotnet test --filter "Category=Azure"

# Filter by speed
dotnet test --filter "Speed!=Slow&Speed!=VerySlow"  # Exclude slow tests
dotnet test --filter "Speed=Slow"                    # Only slow tests

# Filter by cost (integration tests that incur Azure charges)
dotnet test --filter "Cost!=Expensive"  # Free tests only

# Combine filters
dotnet test --filter "Category=Integration&Speed!=VerySlow&Cost!=Expensive"
```

### Run Tests in Release Mode

For performance testing and more realistic deployment scenarios:

```bash
# Build in Release mode first (faster startup in tests)
dotnet build -c Release

# Run tests with Release build
dotnet test --no-build -c Release
```

## Test Development Guidelines

### File Placement

1. **Single component in isolation?** → Use `Unit/`
2. **Complete feature or workflow within one library?** → Use `Functional/`
3. **Multiple components or system-wide feature?** → Use `Integration/`
4. **Unsure?** → Ask reviewers or discuss in PR

### Naming Conventions

Use clear, descriptive test names that explain what is being tested and the expected outcome:

```csharp
// ❌ Poor - Not specific
public void Test1() { }
public void TestAssembly() { }

// ✅ Good - Clear what's being tested
public void AssemblyGeneration_WithValidCommand_GeneratesCorrectMethod() { }
public void ParameterType_WithComplexObject_ResolvesAllProperties() { }
public void ExecuteCommand_WithTimeout_ThrowsTimeoutException() { }
```

### Assertion Guidelines

Use clear, expressive assertions:

```csharp
// Use framework's assert methods
Assert.NotNull(result);
Assert.Equal(expected, actual);
Assert.True(condition, message);
Assert.Contains(item, collection);

// For PowerShell-specific assertions, consider adding helpers
AssertPowerShellCommand(command, expectedException: typeof(RuntimeException));
```

### Logging in Tests

Use the shared PowerShell test base for consistent logging:

```csharp
public class MyTests : PowerShellTestBase
{
    public void MyTest()
    {
        // Logging is available through base class
        _testOutputHelper.WriteLine("Debug message");
        
        // Test code...
    }
}
```

### Test Data and Fixtures

Keep test data close to tests:

```csharp
// ✅ Good - Test data immediately visible
[Theory]
[InlineData("Get-Process")]
[InlineData("Get-Service")]
public void GetCommandSchemaTests(string commandName)
{
    // Test implementation
}

// For complex fixtures, use PowerShellTestBase fixtures method
private PowerShell CreateTestRunspace()
{
    return SetupRunspace(); // From base class
}
```

## Test Development API

### Inherited from PowerShellTestBase

```csharp
public class MyTests : PowerShellTestBase
{
    // Logger available for xUnit output
    protected ITestOutputHelper _testOutputHelper { get; }
    
    // Create initialized PowerShell runspace
    protected PowerShell CreateRunspace() { }
    
    // Shared logging
    protected void LogInfo(string message) { }
}
```

### Shared Test Utilities

```csharp
using PoshMcp.Tests.Shared;

// Create test configuration
var config = TestConfigurationBuilder.Create()
    .WithFunctionNames("Get-Process", "Get-Service")
    .Build();

// Test PowerShell execution
var result = await TestPowerShellHelper.ExecuteCommand(
    "Get-Process", 
    timeout: TimeSpan.FromSeconds(10)
);
```

## Current Test Statistics

- **Total Tests**: 53+ (organized across three categories)
- **Unit Tests**: ~15-20 tests
- **Functional Tests**: ~20-25 tests
- **Integration Tests**: ~10-15 tests
- **Average Runtime**: ~5-10 minutes for full suite

(Statistics as of last project restructure; run `dotnet test --list-tests` for current count)

## Continuous Integration

Tests are run automatically on:
- **Pull Requests**: All tests must pass
- **Main branch commits**: Full test suite runs
- **Release builds**: Tests run in Release configuration for performance validation

See `.github/workflows/` for CI/CD configuration.

## Troubleshooting

### Tests Hang or Timeout

```bash
# Run with explicit timeout
dotnet test --logger "console;verbosity=detailed" -- RunConfiguration.TestTimeout=30000

# Check for resource leaks in PowerShell runspaces
# Review PowerShellTestBase cleanup in test output
```

### PowerShell Runspace Issues

```bash
# Verbose logging for PowerShell setup
dotnet test --filter "FullyQualifiedName~PowerShellCommandExecutionTests" --verbosity detailed
```

### Integration Test Prerequisites Missing

```bash
# Skip expensive integration tests when Docker not available
dotnet test --filter "Category!=Docker"

# Skip Azure tests without credentials
dotnet test --filter "Category!=Azure"
```

## Contributing Tests

When contributing new functionality:

1. **Add tests first** (TDD approach) or alongside implementation
2. **Place in appropriate category** (Unit/Functional/Integration)
3. **Use descriptive names** that explain what and why
4. **Add inline comments** for complex test logic
5. **Run full suite before submitting** PR

```bash
# Pre-commit check
dotnet test --verbosity minimal
dotnet test --filter "FullyQualifiedName~Unit" --verbosity minimal

# Before pushing for review
dotnet test --verbosity normal
```

## See Also

- **[../README.md](../README.md)** — Project overview
- **[../docs/IMPLEMENTATION-GUIDE.md](../docs/IMPLEMENTATION-GUIDE.md)** — Developer guide
- **[xUnit Documentation](https://xunit.net/)** — Testing framework reference



## Benefits of This Organization

1. **Clear Separation of Concerns** - Easy to understand test scope and purpose
2. **Selective Test Execution** - Run only the tests you need during development
3. **Better CI/CD Pipeline Support** - Different test categories can have different execution strategies
4. **Improved Maintainability** - Easier to locate and update tests
5. **Clearer Dependencies** - Understand which tests require external systems

---

## See also

- [Azure integration test documentation](Integration/README.azure-integration.md) — Azure deployment test details
- [Trait-based test filtering](../docs/TRAIT-BASED-TEST-FILTERING.md) — filter integration tests by category, speed, or cost
- [Main README](../README.md) — project overview and getting started
