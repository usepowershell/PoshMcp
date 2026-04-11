using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PoshMcp.Server.PowerShell.OutOfProcess;
using Xunit;

namespace PoshMcp.Tests.Unit.OutOfProcess;

/// <summary>
/// Unit tests for OutOfProcessToolAssemblyGenerator.
/// Tests IL generation of dynamic methods that delegate to ICommandExecutor.
/// </summary>
public class OutOfProcessToolAssemblyGeneratorTests
{
    private readonly Mock<ICommandExecutor> _mockExecutor;
    private readonly ILogger _logger;

    public OutOfProcessToolAssemblyGeneratorTests()
    {
        _mockExecutor = new Mock<ICommandExecutor>();
        _logger = NullLogger.Instance;
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithMockExecutor_DoesNotThrow()
    {
        var generator = new OutOfProcessToolAssemblyGenerator(_mockExecutor.Object);
        Assert.NotNull(generator);
    }

    [Fact]
    public void Constructor_AcceptsICommandExecutor()
    {
        // Verify the constructor accepts the interface, not a concrete type
        ICommandExecutor executor = _mockExecutor.Object;
        var generator = new OutOfProcessToolAssemblyGenerator(executor);
        Assert.NotNull(generator);
    }

    [Fact]
    public void Constructor_NullExecutor_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new OutOfProcessToolAssemblyGenerator(null!));
    }

    #endregion

    #region GenerateAssembly Tests

    [Fact]
    public void GenerateAssembly_WithEmptySchemas_Succeeds()
    {
        var generator = new OutOfProcessToolAssemblyGenerator(_mockExecutor.Object);
        var schemas = new List<RemoteToolSchema>();

        var exception = Record.Exception(() => generator.GenerateAssembly(schemas, _logger));

        Assert.Null(exception);
    }

    [Fact]
    public void GenerateAssembly_WithSchemas_GeneratesType()
    {
        var generator = new OutOfProcessToolAssemblyGenerator(_mockExecutor.Object);
        var schemas = new List<RemoteToolSchema>
        {
            new RemoteToolSchema
            {
                Name = "Get-AzContext",
                Description = "Gets the Azure context",
                Parameters = new List<RemoteParameterSchema>
                {
                    new RemoteParameterSchema
                    {
                        Name = "Subscription",
                        TypeName = "System.String",
                        IsMandatory = false
                    }
                }
            }
        };

        generator.GenerateAssembly(schemas, _logger);

        // Should be able to get methods after generation
        var methods = generator.GetGeneratedMethods();
        Assert.NotEmpty(methods);
    }

    [Fact]
    public void GenerateAssembly_CalledTwice_ReturnsCached()
    {
        var generator = new OutOfProcessToolAssemblyGenerator(_mockExecutor.Object);
        var schemas = new List<RemoteToolSchema>
        {
            new RemoteToolSchema { Name = "Get-Process", Parameters = new List<RemoteParameterSchema>() }
        };

        generator.GenerateAssembly(schemas, _logger);
        generator.GenerateAssembly(schemas, _logger); // should not throw, returns cached

        var methods = generator.GetGeneratedMethods();
        Assert.Single(methods);
    }

    #endregion

    #region GetGeneratedInstance Tests

    [Fact]
    public void GetGeneratedInstance_BeforeGenerate_ThrowsInvalidOperationException()
    {
        var generator = new OutOfProcessToolAssemblyGenerator(_mockExecutor.Object);

        Assert.Throws<InvalidOperationException>(
            () => generator.GetGeneratedInstance(_logger));
    }

    [Fact]
    public void GetGeneratedInstance_AfterGenerate_ReturnsInstance()
    {
        var generator = new OutOfProcessToolAssemblyGenerator(_mockExecutor.Object);
        var schemas = new List<RemoteToolSchema>
        {
            new RemoteToolSchema { Name = "Get-Process", Parameters = new List<RemoteParameterSchema>() }
        };

        generator.GenerateAssembly(schemas, _logger);
        var instance = generator.GetGeneratedInstance(_logger);

        Assert.NotNull(instance);
    }

    [Fact]
    public void GetGeneratedInstance_CalledTwice_ReturnsSame()
    {
        var generator = new OutOfProcessToolAssemblyGenerator(_mockExecutor.Object);
        var schemas = new List<RemoteToolSchema>
        {
            new RemoteToolSchema { Name = "Get-Process", Parameters = new List<RemoteParameterSchema>() }
        };

        generator.GenerateAssembly(schemas, _logger);
        var instance1 = generator.GetGeneratedInstance(_logger);
        var instance2 = generator.GetGeneratedInstance(_logger);

        Assert.Same(instance1, instance2);
    }

    #endregion

    #region GetGeneratedMethods Tests

    [Fact]
    public void GetGeneratedMethods_BeforeGenerate_ThrowsInvalidOperationException()
    {
        var generator = new OutOfProcessToolAssemblyGenerator(_mockExecutor.Object);

        Assert.Throws<InvalidOperationException>(
            () => generator.GetGeneratedMethods());
    }

    [Fact]
    public void GetGeneratedMethods_ReturnsCorrectMethodNames()
    {
        var generator = new OutOfProcessToolAssemblyGenerator(_mockExecutor.Object);
        var schemas = new List<RemoteToolSchema>
        {
            new RemoteToolSchema { Name = "Get-AzContext", Parameters = new List<RemoteParameterSchema>() },
            new RemoteToolSchema { Name = "Set-AzContext", Parameters = new List<RemoteParameterSchema>() }
        };

        generator.GenerateAssembly(schemas, _logger);
        var methods = generator.GetGeneratedMethods();

        Assert.Contains("get_az_context", methods.Keys);
        Assert.Contains("set_az_context", methods.Keys);
        Assert.Equal(2, methods.Count);
    }

    [Fact]
    public void GetGeneratedMethods_AllReturnTaskString()
    {
        var generator = new OutOfProcessToolAssemblyGenerator(_mockExecutor.Object);
        var schemas = new List<RemoteToolSchema>
        {
            new RemoteToolSchema
            {
                Name = "Get-Process",
                Parameters = new List<RemoteParameterSchema>
                {
                    new RemoteParameterSchema { Name = "Name", TypeName = "System.String", IsMandatory = false }
                }
            }
        };

        generator.GenerateAssembly(schemas, _logger);
        var methods = generator.GetGeneratedMethods();

        foreach (var method in methods.Values)
        {
            Assert.Equal(typeof(Task<string>), method.ReturnType);
        }
    }

    #endregion

    #region Parameter Type Mapping Tests

    [Fact]
    public void GeneratedMethod_StringParam_HasCorrectType()
    {
        var generator = CreateGeneratorWithSchema(new RemoteParameterSchema
        {
            Name = "Name",
            TypeName = "System.String",
            IsMandatory = false
        });

        var methods = generator.GetGeneratedMethods();
        var method = methods.Values.First();
        var param = method.GetParameters().First(p => p.Name == "Name");

        Assert.Equal(typeof(string), param.ParameterType);
    }

    [Fact]
    public void GeneratedMethod_MandatoryStringParam_HasStringType()
    {
        var generator = CreateGeneratorWithSchema(new RemoteParameterSchema
        {
            Name = "Name",
            TypeName = "System.String",
            IsMandatory = true
        });

        var methods = generator.GetGeneratedMethods();
        var method = methods.Values.First();
        var param = method.GetParameters().First(p => p.Name == "Name");

        Assert.Equal(typeof(string), param.ParameterType);
    }

    [Fact]
    public void GeneratedMethod_IntParam_NonMandatory_IsNullable()
    {
        var generator = CreateGeneratorWithSchema(new RemoteParameterSchema
        {
            Name = "Count",
            TypeName = "System.Int32",
            IsMandatory = false
        });

        var methods = generator.GetGeneratedMethods();
        var method = methods.Values.First();
        var param = method.GetParameters().First(p => p.Name == "Count");

        Assert.Equal(typeof(int?), param.ParameterType);
    }

    [Fact]
    public void GeneratedMethod_IntParam_Mandatory_IsNotNullable()
    {
        var generator = CreateGeneratorWithSchema(new RemoteParameterSchema
        {
            Name = "Count",
            TypeName = "System.Int32",
            IsMandatory = true
        });

        var methods = generator.GetGeneratedMethods();
        var method = methods.Values.First();
        var param = method.GetParameters().First(p => p.Name == "Count");

        Assert.Equal(typeof(int), param.ParameterType);
    }

    [Fact]
    public void GeneratedMethod_BoolParam_NonMandatory_IsNullable()
    {
        var generator = CreateGeneratorWithSchema(new RemoteParameterSchema
        {
            Name = "Force",
            TypeName = "System.Boolean",
            IsMandatory = false
        });

        var methods = generator.GetGeneratedMethods();
        var method = methods.Values.First();
        var param = method.GetParameters().First(p => p.Name == "Force");

        Assert.Equal(typeof(bool?), param.ParameterType);
    }

    [Fact]
    public void GeneratedMethod_SwitchParam_NonMandatory_IsBoolNullable()
    {
        var generator = CreateGeneratorWithSchema(new RemoteParameterSchema
        {
            Name = "Force",
            TypeName = "System.Management.Automation.SwitchParameter",
            IsMandatory = false
        });

        var methods = generator.GetGeneratedMethods();
        var method = methods.Values.First();
        var param = method.GetParameters().First(p => p.Name == "Force");

        Assert.Equal(typeof(bool?), param.ParameterType);
    }

    [Fact]
    public void GeneratedMethod_UnknownType_MapsToString()
    {
        var generator = CreateGeneratorWithSchema(new RemoteParameterSchema
        {
            Name = "Data",
            TypeName = "System.Object[]",
            IsMandatory = false
        });

        var methods = generator.GetGeneratedMethods();
        var method = methods.Values.First();
        var param = method.GetParameters().First(p => p.Name == "Data");

        Assert.Equal(typeof(string), param.ParameterType);
    }

    #endregion

    #region Framework Parameters Tests

    [Fact]
    public void GeneratedMethod_HasFrameworkParameters()
    {
        var generator = CreateGeneratorWithSchema(new RemoteParameterSchema
        {
            Name = "Name",
            TypeName = "System.String",
            IsMandatory = false
        });

        var methods = generator.GetGeneratedMethods();
        var method = methods.Values.First();
        var paramNames = method.GetParameters().Select(p => p.Name).ToList();

        Assert.Contains("_AllProperties", paramNames);
        Assert.Contains("_MaxResults", paramNames);
        Assert.Contains("_RequestedProperties", paramNames);
        Assert.Contains("cancellationToken", paramNames);
    }

    [Fact]
    public void GeneratedMethod_CancellationTokenIsLast()
    {
        var generator = CreateGeneratorWithSchema(new RemoteParameterSchema
        {
            Name = "Name",
            TypeName = "System.String",
            IsMandatory = false
        });

        var methods = generator.GetGeneratedMethods();
        var method = methods.Values.First();
        var lastParam = method.GetParameters().Last();

        Assert.Equal("cancellationToken", lastParam.Name);
        Assert.Equal(typeof(CancellationToken), lastParam.ParameterType);
    }

    #endregion

    #region Method Invocation Tests

    [Fact]
    public async Task GeneratedMethod_Invoked_CallsExecutor()
    {
        _mockExecutor
            .Setup(e => e.InvokeAsync(
                "Get-Process",
                It.IsAny<IDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("[{\"Name\":\"pwsh\"}]");

        var generator = new OutOfProcessToolAssemblyGenerator(_mockExecutor.Object);
        var schemas = new List<RemoteToolSchema>
        {
            new RemoteToolSchema
            {
                Name = "Get-Process",
                Parameters = new List<RemoteParameterSchema>
                {
                    new RemoteParameterSchema { Name = "Name", TypeName = "System.String", IsMandatory = false }
                }
            }
        };

        generator.GenerateAssembly(schemas, _logger);
        var instance = generator.GetGeneratedInstance(_logger);
        var methods = generator.GetGeneratedMethods();
        var method = methods["get_process"];

        // Invoke with: Name="pwsh", _AllProperties=null, _MaxResults=null, _RequestedProperties=null, CancellationToken
        var result = await (Task<string>)method.Invoke(instance,
            new object?[] { "pwsh", null, null, null, CancellationToken.None })!;

        Assert.Equal("[{\"Name\":\"pwsh\"}]", result);
        _mockExecutor.Verify(e => e.InvokeAsync(
            "Get-Process",
            It.Is<IDictionary<string, object?>>(d =>
                d.ContainsKey("Name") && (string?)d["Name"] == "pwsh"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GeneratedMethod_NullParams_NotPassedToExecutor()
    {
        _mockExecutor
            .Setup(e => e.InvokeAsync(
                "Get-Process",
                It.IsAny<IDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("[]");

        var generator = new OutOfProcessToolAssemblyGenerator(_mockExecutor.Object);
        var schemas = new List<RemoteToolSchema>
        {
            new RemoteToolSchema
            {
                Name = "Get-Process",
                Parameters = new List<RemoteParameterSchema>
                {
                    new RemoteParameterSchema { Name = "Name", TypeName = "System.String", IsMandatory = false }
                }
            }
        };

        generator.GenerateAssembly(schemas, _logger);
        var instance = generator.GetGeneratedInstance(_logger);
        var methods = generator.GetGeneratedMethods();
        var method = methods["get_process"];

        // Invoke with all nulls
        await (Task<string>)method.Invoke(instance,
            new object?[] { null, null, null, null, CancellationToken.None })!;

        _mockExecutor.Verify(e => e.InvokeAsync(
            "Get-Process",
            It.Is<IDictionary<string, object?>>(d => d.Count == 0),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GeneratedMethod_FrameworkParams_NotPassedToExecutor()
    {
        _mockExecutor
            .Setup(e => e.InvokeAsync(
                "Get-Process",
                It.IsAny<IDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("[]");

        var generator = new OutOfProcessToolAssemblyGenerator(_mockExecutor.Object);
        var schemas = new List<RemoteToolSchema>
        {
            new RemoteToolSchema { Name = "Get-Process", Parameters = new List<RemoteParameterSchema>() }
        };

        generator.GenerateAssembly(schemas, _logger);
        var instance = generator.GetGeneratedInstance(_logger);
        var methods = generator.GetGeneratedMethods();
        var method = methods["get_process"];

        // Invoke with framework params set to non-null
        await (Task<string>)method.Invoke(instance,
            new object?[] { true, 10, new[] { "Name" }, CancellationToken.None })!;

        // Framework params should NOT appear in the dictionary
        _mockExecutor.Verify(e => e.InvokeAsync(
            "Get-Process",
            It.Is<IDictionary<string, object?>>(d =>
                !d.ContainsKey("_AllProperties") &&
                !d.ContainsKey("_MaxResults") &&
                !d.ContainsKey("_RequestedProperties")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GeneratedMethod_MultipleParams_AllPassedToExecutor()
    {
        _mockExecutor
            .Setup(e => e.InvokeAsync(
                "Set-AzContext",
                It.IsAny<IDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"ok\":true}");

        var generator = new OutOfProcessToolAssemblyGenerator(_mockExecutor.Object);
        var schemas = new List<RemoteToolSchema>
        {
            new RemoteToolSchema
            {
                Name = "Set-AzContext",
                Parameters = new List<RemoteParameterSchema>
                {
                    new RemoteParameterSchema { Name = "Subscription", TypeName = "System.String", IsMandatory = true },
                    new RemoteParameterSchema { Name = "Tenant", TypeName = "System.String", IsMandatory = false }
                }
            }
        };

        generator.GenerateAssembly(schemas, _logger);
        var instance = generator.GetGeneratedInstance(_logger);
        var methods = generator.GetGeneratedMethods();
        var method = methods["set_az_context"];

        var result = await (Task<string>)method.Invoke(instance,
            new object?[] { "sub-123", "tenant-456", null, null, null, CancellationToken.None })!;

        Assert.Equal("{\"ok\":true}", result);
        _mockExecutor.Verify(e => e.InvokeAsync(
            "Set-AzContext",
            It.Is<IDictionary<string, object?>>(d =>
                d.Count == 2 &&
                (string?)d["Subscription"] == "sub-123" &&
                (string?)d["Tenant"] == "tenant-456"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region ClearCache Tests

    [Fact]
    public void ClearCache_DoesNotThrow()
    {
        var generator = new OutOfProcessToolAssemblyGenerator(_mockExecutor.Object);

        var exception = Record.Exception(() => generator.ClearCache());

        Assert.Null(exception);
    }

    [Fact]
    public void ClearCache_CanBeCalledMultipleTimes()
    {
        var generator = new OutOfProcessToolAssemblyGenerator(_mockExecutor.Object);

        generator.ClearCache();

        var exception = Record.Exception(() => generator.ClearCache());

        Assert.Null(exception);
    }

    [Fact]
    public void ClearCache_AllowsRegeneration()
    {
        var generator = new OutOfProcessToolAssemblyGenerator(_mockExecutor.Object);
        var schemas = new List<RemoteToolSchema>
        {
            new RemoteToolSchema { Name = "Get-Process", Parameters = new List<RemoteParameterSchema>() }
        };

        generator.GenerateAssembly(schemas, _logger);
        generator.ClearCache();

        // Should throw because cache was cleared
        Assert.Throws<InvalidOperationException>(() => generator.GetGeneratedMethods());

        // But should be able to regenerate
        generator.GenerateAssembly(schemas, _logger);
        var methods = generator.GetGeneratedMethods();
        Assert.Single(methods);
    }

    #endregion

    #region ExecuteRemoteCommandAsync Static Helper Tests

    [Fact]
    public async Task ExecuteRemoteCommandAsync_SkipsFrameworkParams()
    {
        _mockExecutor
            .Setup(e => e.InvokeAsync(
                "Test-Command",
                It.IsAny<IDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("ok");

        var result = await OutOfProcessToolAssemblyGenerator.ExecuteRemoteCommandAsync(
            "Test-Command",
            new[] { "Name", "_AllProperties", "_MaxResults" },
            new object?[] { "test", true, 10 },
            _mockExecutor.Object,
            CancellationToken.None);

        Assert.Equal("ok", result);
        _mockExecutor.Verify(e => e.InvokeAsync(
            "Test-Command",
            It.Is<IDictionary<string, object?>>(d =>
                d.Count == 1 && (string?)d["Name"] == "test"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteRemoteCommandAsync_SkipsNullValues()
    {
        _mockExecutor
            .Setup(e => e.InvokeAsync(
                "Test-Command",
                It.IsAny<IDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("ok");

        await OutOfProcessToolAssemblyGenerator.ExecuteRemoteCommandAsync(
            "Test-Command",
            new[] { "Name", "Count" },
            new object?[] { null, null },
            _mockExecutor.Object,
            CancellationToken.None);

        _mockExecutor.Verify(e => e.InvokeAsync(
            "Test-Command",
            It.Is<IDictionary<string, object?>>(d => d.Count == 0),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Parameter Set Name Tests

    [Fact]
    public void GeneratedMethod_WithParameterSetName_AppendsToMethodName()
    {
        var generator = new OutOfProcessToolAssemblyGenerator(_mockExecutor.Object);
        var schemas = new List<RemoteToolSchema>
        {
            new RemoteToolSchema
            {
                Name = "Get-AzContext",
                ParameterSetName = "BySubscription",
                Parameters = new List<RemoteParameterSchema>()
            }
        };

        generator.GenerateAssembly(schemas, _logger);
        var methods = generator.GetGeneratedMethods();

        Assert.Contains("get_az_context_by_subscription", methods.Keys);
    }

    [Fact]
    public void GeneratedMethod_AllParameterSets_NoSuffix()
    {
        var generator = new OutOfProcessToolAssemblyGenerator(_mockExecutor.Object);
        var schemas = new List<RemoteToolSchema>
        {
            new RemoteToolSchema
            {
                Name = "Get-AzContext",
                ParameterSetName = "__AllParameterSets",
                Parameters = new List<RemoteParameterSchema>()
            }
        };

        generator.GenerateAssembly(schemas, _logger);
        var methods = generator.GetGeneratedMethods();

        Assert.Contains("get_az_context", methods.Keys);
    }

    #endregion

    #region Helpers

    private OutOfProcessToolAssemblyGenerator CreateGeneratorWithSchema(RemoteParameterSchema paramSchema)
    {
        var generator = new OutOfProcessToolAssemblyGenerator(_mockExecutor.Object);
        var schemas = new List<RemoteToolSchema>
        {
            new RemoteToolSchema
            {
                Name = "Test-Command",
                Parameters = new List<RemoteParameterSchema> { paramSchema }
            }
        };

        generator.GenerateAssembly(schemas, _logger);
        return generator;
    }

    #endregion
}
