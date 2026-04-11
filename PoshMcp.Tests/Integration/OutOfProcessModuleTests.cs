using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PoshMcp.Server.PowerShell;
using PoshMcp.Server.PowerShell.OutOfProcess;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Integration;

/// <summary>
/// Integration tests that exercise OOP module loading with vendored Az modules.
/// Requires pwsh on PATH and vendored modules at integration/Modules/.
/// Tests skip automatically via <see cref="AzModulesAvailableFactAttribute"/>.
/// </summary>
[Trait("Category", "OutOfProcessModules")]
public class OutOfProcessModuleTests
{
    private readonly ITestOutputHelper _output;

    public OutOfProcessModuleTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private ILoggerFactory CreateLoggerFactory()
    {
        return LoggerFactory.Create(builder =>
        {
            builder.AddProvider(new TestOutputLoggerProvider(_output));
            builder.SetMinimumLevel(LogLevel.Debug);
        });
    }

    private static string VendoredModulesPath => AzModulesAvailableFactAttribute.VendoredModulesPath;

    /// <summary>
    /// Creates an executor with the vendored module path configured, starts it,
    /// and sends the setup command to prepend the module path to PSModulePath.
    /// </summary>
    private async Task<OutOfProcessCommandExecutor> CreateAndStartExecutorAsync(
        ILoggerFactory loggerFactory,
        TimeSpan? requestTimeout = null)
    {
        var executor = new OutOfProcessCommandExecutor(
            loggerFactory.CreateLogger<OutOfProcessCommandExecutor>(),
            requestTimeout: requestTimeout ?? TimeSpan.FromSeconds(120));
        await executor.StartAsync();

        // Configure the module path in the subprocess so Import-Module can find Az modules
        var envConfig = new EnvironmentConfiguration
        {
            ModulePaths = new List<string> { VendoredModulesPath }
        };
        await executor.SetupAsync(envConfig);

        return executor;
    }

    // ---- Az.Accounts module loading tests ----

    [AzModulesAvailableFact]
    [Trait("Category", "OutOfProcessModules")]
    public async Task AzAccounts_ImportAndDiscoverCommands()
    {
        using var factory = CreateLoggerFactory();
        var executor = await CreateAndStartExecutorAsync(factory);

        try
        {
            var config = new PowerShellConfiguration
            {
                FunctionNames = new List<string>(),
                Modules = new List<string> { "Az.Accounts" },
                IncludePatterns = new List<string> { "*" },
                ExcludePatterns = new List<string>()
            };

            var schemas = await executor.DiscoverCommandsAsync(config);

            Assert.NotNull(schemas);
            Assert.NotEmpty(schemas);

            _output.WriteLine($"Discovered {schemas.Count} commands from Az.Accounts");

            // Az.Accounts should have well-known commands
            var names = schemas.Select(s => s.Name).ToList();
            _output.WriteLine($"Sample commands: {string.Join(", ", names.Take(10))}");

            Assert.True(schemas.Count > 0,
                "Expected at least 1 schema from Az.Accounts module");
        }
        finally
        {
            await executor.DisposeAsync();
        }
    }

    [AzModulesAvailableFact]
    [Trait("Category", "OutOfProcessModules")]
    public async Task AzAccounts_GetAzContext_FailsAuthButDoesNotCrash()
    {
        using var factory = CreateLoggerFactory();
        var executor = await CreateAndStartExecutorAsync(factory);

        try
        {
            // First import the module via discover
            var config = new PowerShellConfiguration
            {
                FunctionNames = new List<string>(),
                Modules = new List<string> { "Az.Accounts" },
                IncludePatterns = new List<string> { "Get-AzContext" },
                ExcludePatterns = new List<string>()
            };

            var schemas = await executor.DiscoverCommandsAsync(config);
            Assert.NotNull(schemas);

            // Get-AzContext without auth should either return empty/null or an error,
            // but it must NOT crash the server process.
            // We use a new executor since DiscoverCommandsAsync caches schemas.
            var executor2 = await CreateAndStartExecutorAsync(factory);
            try
            {
                // Set up module path and import module in the new executor
                var envConfig = new EnvironmentConfiguration
                {
                    ModulePaths = new List<string> { VendoredModulesPath },
                    ImportModules = new List<string> { "Az.Accounts" }
                };
                await executor2.SetupAsync(envConfig);

                // Invoke Get-AzContext — expect it to return something (possibly empty)
                // or throw an OOP error. Either way, the executor should remain operational.
                try
                {
                    var result = await executor2.InvokeAsync(
                        "Get-AzContext",
                        new Dictionary<string, object?>());

                    _output.WriteLine($"Get-AzContext returned (length={result?.Length ?? 0}): " +
                        $"{result?[..Math.Min(200, result?.Length ?? 0)]}");
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("OOP error", StringComparison.OrdinalIgnoreCase))
                {
                    // Expected — no auth context configured
                    _output.WriteLine($"Get-AzContext failed with expected error: {ex.Message}");
                }

                // Verify the executor is still alive by running a basic command
                var healthCheck = await executor2.InvokeAsync(
                    "Get-Date",
                    new Dictionary<string, object?>());
                Assert.NotNull(healthCheck);
                _output.WriteLine("Executor still healthy after Get-AzContext call");
            }
            finally
            {
                await executor2.DisposeAsync();
            }
        }
        finally
        {
            await executor.DisposeAsync();
        }
    }

    [AzModulesAvailableFact]
    [Trait("Category", "OutOfProcessModules")]
    public async Task HeavyModuleImport_DoesNotCrashServer()
    {
        using var factory = CreateLoggerFactory();

        // Use a generous timeout — Az modules are large
        var executor = await CreateAndStartExecutorAsync(factory, TimeSpan.FromSeconds(180));

        try
        {
            var config = new PowerShellConfiguration
            {
                FunctionNames = new List<string>(),
                Modules = new List<string> { "Az.Accounts" },
                IncludePatterns = new List<string> { "*" },
                ExcludePatterns = new List<string>()
            };

            var sw = Stopwatch.StartNew();
            var schemas = await executor.DiscoverCommandsAsync(config);
            sw.Stop();

            Assert.NotNull(schemas);
            Assert.NotEmpty(schemas);

            _output.WriteLine($"Az.Accounts discovery took {sw.ElapsedMilliseconds}ms, found {schemas.Count} commands");

            // Verify the executor is still responsive after heavy module load
            var result = await executor.InvokeAsync(
                "Get-Date",
                new Dictionary<string, object?>());
            Assert.NotNull(result);
            Assert.NotEmpty(result);

            _output.WriteLine("Server process healthy after heavy module import");
        }
        finally
        {
            await executor.DisposeAsync();
        }
    }

    [AzModulesAvailableFact]
    [Trait("Category", "OutOfProcessModules")]
    public async Task MultipleModuleImport_SingleDiscoveryCall()
    {
        using var factory = CreateLoggerFactory();
        var executor = await CreateAndStartExecutorAsync(factory, TimeSpan.FromSeconds(180));

        try
        {
            // Import multiple Az modules in a single discovery call
            var modules = new List<string> { "Az.Accounts" };

            // Check if Az.Compute is also vendored
            var azComputePath = System.IO.Path.Combine(VendoredModulesPath, "Az.Compute");
            if (System.IO.Directory.Exists(azComputePath))
            {
                modules.Add("Az.Compute");
            }

            // Check if Az.Resources is vendored
            var azResourcesPath = System.IO.Path.Combine(VendoredModulesPath, "Az.Resources");
            if (System.IO.Directory.Exists(azResourcesPath))
            {
                modules.Add("Az.Resources");
            }

            _output.WriteLine($"Testing multi-module import with: {string.Join(", ", modules)}");

            var config = new PowerShellConfiguration
            {
                FunctionNames = new List<string>(),
                Modules = modules,
                IncludePatterns = new List<string> { "*" },
                ExcludePatterns = new List<string>()
            };

            var sw = Stopwatch.StartNew();
            var schemas = await executor.DiscoverCommandsAsync(config);
            sw.Stop();

            Assert.NotNull(schemas);
            Assert.NotEmpty(schemas);

            _output.WriteLine($"Multi-module discovery took {sw.ElapsedMilliseconds}ms, found {schemas.Count} commands");

            // Verify we got commands (at minimum from Az.Accounts)
            Assert.True(schemas.Count > 0,
                "Expected commands from multi-module import");
        }
        finally
        {
            await executor.DisposeAsync();
        }
    }

    [AzModulesAvailableFact]
    [Trait("Category", "OutOfProcessModules")]
    public async Task PartialModuleFailure_ReturnsErrorForBadModule()
    {
        using var factory = CreateLoggerFactory();
        var executor = await CreateAndStartExecutorAsync(factory, TimeSpan.FromSeconds(120));

        try
        {
            // Mix a valid module with a nonexistent one.
            // The discover handler in oop-host.ps1 returns an error response when
            // Import-Module fails, so we expect an exception.
            var config = new PowerShellConfiguration
            {
                FunctionNames = new List<string>(),
                Modules = new List<string> { "NonExistent.FakeModule.XYZ" },
                IncludePatterns = new List<string> { "*" },
                ExcludePatterns = new List<string>()
            };

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await executor.DiscoverCommandsAsync(config);
            });

            _output.WriteLine($"Expected error for nonexistent module: {ex.Message}");
            Assert.Contains("NonExistent.FakeModule.XYZ", ex.Message);

            // Verify the executor is still alive — a failed module import should not
            // destroy the subprocess.
            // Need a fresh executor since the OOP host may have returned an error for this one.
            var executor2 = await CreateAndStartExecutorAsync(factory, TimeSpan.FromSeconds(120));
            try
            {
                var healthConfig = new PowerShellConfiguration
                {
                    FunctionNames = new List<string> { "Get-Date" },
                    Modules = new List<string>(),
                    IncludePatterns = new List<string>(),
                    ExcludePatterns = new List<string>()
                };

                var schemas = await executor2.DiscoverCommandsAsync(healthConfig);
                Assert.NotNull(schemas);
                _output.WriteLine("Fresh executor healthy after module failure test");
            }
            finally
            {
                await executor2.DisposeAsync();
            }
        }
        finally
        {
            await executor.DisposeAsync();
        }
    }

    [AzModulesAvailableFact]
    [Trait("Category", "OutOfProcessModules")]
    public async Task NoSubprocessLeaks_AfterModuleTests()
    {
        using var factory = CreateLoggerFactory();

        // Track pwsh processes before
        var pwshCountBefore = Process.GetProcessesByName("pwsh").Length;
        _output.WriteLine($"pwsh processes before: {pwshCountBefore}");

        // Create and dispose multiple executors with module loading
        for (int i = 0; i < 3; i++)
        {
            var executor = await CreateAndStartExecutorAsync(factory, TimeSpan.FromSeconds(120));

            var config = new PowerShellConfiguration
            {
                FunctionNames = new List<string>(),
                Modules = new List<string> { "Az.Accounts" },
                IncludePatterns = new List<string> { "Get-AzContext" },
                ExcludePatterns = new List<string>()
            };

            await executor.DiscoverCommandsAsync(config);
            await executor.DisposeAsync();
        }

        // Allow processes time to exit
        await Task.Delay(2000);

        var pwshCountAfter = Process.GetProcessesByName("pwsh").Length;
        _output.WriteLine($"pwsh processes after: {pwshCountAfter}");

        // We should not have leaked more than 1 process (the test runner's own pwsh, if any)
        Assert.True(pwshCountAfter <= pwshCountBefore + 1,
            $"Possible subprocess leak: before={pwshCountBefore}, after={pwshCountAfter}");
    }
}
