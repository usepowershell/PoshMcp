# PR #211 Test Fixtures

Integration test fixtures for validating WinPSCompat proxy cmdlet support end-to-end.

## Overview

PR #211 introduces changes to PowerShell proxy detection and high-parameter method handling:

- **PowerShellParameterUtils.cs**: Proxy detection (IsImplicitRemotingProxy) and object→string type coercion (EffectiveParameterType)
- **PowerShellAssemblyGenerator.cs**: Proxy-aware method generation for dynamic assemblies
- **McpToolFactoryV2.cs**: Cached delegate emission for methods with >16 parameters

These fixtures provide synthetic test commands that exercise both code paths in end-to-end scenarios.

## Fixtures

### ProxyTestFixtures.cs

Static factory methods for creating synthetic commands:

- **CreateProxyStyledCommand()** → CommandInfo marked as ImplicitRemoting proxy
  - Tests proxy detection logic (PrivateData marker, Description, RootModule)
  - Validates object-typed parameters are handled correctly
  
- **CreateHighParameterCommand()** → CommandInfo with 17 parameters
  - Tests the cached delegate emit path in McpToolFactoryV2.GetDelegateTypeForMethod()
  - Ensures BCL Func<> fast path is bypassed (only goes to Func`17 for 16 params)
  - Validates cache reuses emitted delegate types
  
- **CreateObjectParameterCommand()** → CommandInfo with [object] parameters on proxy module
  - Tests EffectiveParameterType() coercion (object→string for proxy params)
  - Validates schema generation correctly types object parameters

### Pr211IntegrationFixtureSetup.cs

Test infrastructure class for integration tests. Provides:

- **GetFixtureCommands()** → List<CommandInfo> with all fixture commands
  - Creates and caches fixture commands
  - Logs creation progress for debugging
  
- **ValidateFixtureSchemas()** → Helper to validate generated MCP tool schemas
  - Confirms parameter counts are preserved
  - Notes where proxy coercion will occur
  - Documents high-parameter paths being tested

- **Collection Fixture** → Xunit collection definition for shared setup
  - Use `[Collection("PR #211 Proxy & High-Parameter Tests")]` on test classes
  - Ensures fixtures are created once per collection

## Usage in Integration Tests

```csharp
// Inherit from PowerShellTestBase (provides toolFactory, logger, etc.)
[Collection("PR #211 Proxy & High-Parameter Tests")]
public class Pr211ToolSchemaIntegrationTests : PowerShellTestBase
{
    private readonly Pr211IntegrationFixtureSetup _fixtureSetup;
    
    public Pr211ToolSchemaIntegrationTests(
        ITestOutputHelper output, 
        Pr211IntegrationFixtureSetup fixtureSetup) 
        : base(output)
    {
        _fixtureSetup = fixtureSetup;
    }

    [Fact]
    public void ProxyAndHighParamCommands_GenerateValidToolSchemas()
    {
        // Arrange
        var commands = _fixtureSetup.GetFixtureCommands();
        
        // Act
        var schemas = ToolFactory.GetToolSchemas(commands, Logger);
        
        // Assert
        Assert.NotEmpty(schemas);
        _fixtureSetup.ValidateFixtureSchemas(commands, ToolFactory, Logger);
        
        // Validate proxy command has string params (not object)
        var proxySchema = schemas.FirstOrDefault(s => s.Name.Contains("Proxy"));
        Assert.NotNull(proxySchema);
        Assert.All(
            proxySchema!.InputSchema.Properties,
            prop => Assert.True(
                prop.Value.Type == "string",
                $"Proxy command param {prop.Key} should be string, not object"));
        
        // Validate high-param command schema is complete
        var highParamSchema = schemas.FirstOrDefault(s => s.Name.Contains("HighParam"));
        Assert.NotNull(highParamSchema);
        Assert.Equal(17, highParamSchema!.InputSchema.Properties.Count);
    }
}
```

## Test Coverage

These fixtures validate:

1. ✓ Proxy detection (all four signal types: PrivateData, Description, RootModule, null Module)
2. ✓ Type coercion for object parameters on proxy commands
3. ✓ Cached delegate emission for >16-parameter methods
4. ✓ End-to-end MCP tool schema generation
5. ✓ Schema parameter types match effective types (not raw types)

## Notes for Fry (Test Implementation)

When writing the integration test using these fixtures:

- The fixtures are **pre-created** and ready to pass to McpToolFactoryV2
- No need to mock or stub PowerShell — these are real CommandInfo objects
- Use **Pr211IntegrationFixtureSetup** collection fixture to avoid recreating commands multiple times
- Validate both the **method generation** (delegate types) AND the **schema output** (parameter types)
- Log parameter counts before/after schema generation to catch silent failures

Date: 2026-05-11
