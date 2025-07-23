# PoshMcp Test Organization

This document describes the organization of tests in the PoshMcp project.

## Directory Structure

The tests are organized into three main categories, each in its own directory:

### 📁 Unit/ - Unit Tests
Tests that verify individual components in isolation with minimal dependencies.

- **`SimpleAssemblyTests.cs`** - Basic test framework validation and simple assembly tests
- **`ParameterTypeTests.cs`** - Tests for PowerShell parameter type handling and validation
- **`OutputTypeTests.cs`** - Tests for PowerShell output type determination and processing

### 📁 Functional/ - Functional Tests
Tests that verify complete features and workflows, but still focus on specific functionality.

- **`AssemblyGenerationTests.cs`** - Tests for dynamic assembly generation from PowerShell commands
- **`MethodExecutionTests.cs`** - Tests for generated method execution and behavior
- **`AdvancedMethodExecutionTests.cs`** - Tests for complex method execution scenarios
- **`PowerShellCommandExecutionTests.cs`** - Tests for PowerShell command execution and caching
- **`ReturnTypeIntegrationTests.cs`** - Tests for return type handling and integration
- **`McpServerSetupTests.cs`** - Tests for MCP server configuration and setup

### 📁 Integration/ - Integration Tests
Tests that verify end-to-end scenarios and interactions between multiple components.

- **`McpServerIntegrationTests.cs`** - Full MCP server integration tests with external clients
- **`IntegrationTests.cs`** - General integration tests for component interactions
- **`ComplexIntegrationTests.cs`** - Complex end-to-end integration scenarios
- **`CommandLineTests.cs`** - Command-line interface integration tests

### 📁 Shared/ - Shared Test Infrastructure
Common test utilities and base classes used across all test types.

- **`PowerShellTestBase.cs`** - Base class for PowerShell-related tests with common setup
- **`TestOutputLogger.cs`** - Logger implementation for capturing test output

## Test Classification Guidelines

### Unit Tests
- Test individual classes or methods in isolation
- Use mocks/stubs for dependencies
- Fast execution (< 100ms per test)
- No external dependencies (file system, network, PowerShell runspace)

### Functional Tests
- Test complete features or workflows
- May use real PowerShell instances but isolated
- Medium execution time (100ms - 1s per test)
- Limited external dependencies

### Integration Tests
- Test interactions between multiple components
- Use real external systems when necessary
- Longer execution time (1s+ per test)
- May have external dependencies (processes, network)

## Running Tests

### Run All Tests
```bash
dotnet test
```

### Run Tests by Category
```bash
# Unit tests only
dotnet test --filter "FullyQualifiedName~Unit"

# Functional tests only
dotnet test --filter "FullyQualifiedName~Functional"

# Integration tests only
dotnet test --filter "FullyQualifiedName~Integration"
```

### Run Specific Test File
```bash
# Example: Run only assembly generation tests
dotnet test --filter "FullyQualifiedName~AssemblyGenerationTests"
```

## Test Development Guidelines

1. **Place tests in the appropriate directory** based on their scope and dependencies
2. **Use descriptive test names** that clearly indicate what is being tested
3. **Keep unit tests fast and isolated** - they should run without external dependencies
4. **Use the shared test infrastructure** (`PowerShellTestBase`, `TestOutputLogger`) when appropriate
5. **Document complex test scenarios** with inline comments
6. **Follow the existing naming conventions** and patterns established in the codebase

## Current Test Statistics

- **Total Tests**: 53 (as of reorganization)
- **Unit Tests**: 3 files
- **Functional Tests**: 6 files  
- **Integration Tests**: 4 files
- **Shared Infrastructure**: 2 files

## Benefits of This Organization

1. **Clear Separation of Concerns** - Easy to understand test scope and purpose
2. **Selective Test Execution** - Run only the tests you need during development
3. **Better CI/CD Pipeline Support** - Different test categories can have different execution strategies
4. **Improved Maintainability** - Easier to locate and update tests
5. **Clearer Dependencies** - Understand which tests require external systems
