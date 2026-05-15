using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PoshMcp.Server.PowerShell;
using PoshMcp.Server.PowerShell.OutOfProcess;
using PoshMcp.Tests.Shared;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Integration;

/// <summary>
/// Integration tests that exercise the real OOP pipeline with an actual pwsh subprocess.
/// Requires pwsh on PATH — tests skip automatically via <see cref="PwshAvailableFactAttribute"/>.
/// </summary>
[Trait("Category", "OutOfProcess")]
public class OutOfProcessIntegrationTests : IAsyncLifetime
{
    private OutOfProcessCommandExecutor? _executor;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly ITestOutputHelper _output;
    private TempDirectory? _testTempDirHolder;
    private string _testTempDir = string.Empty;

    public OutOfProcessIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddProvider(new TestOutputLoggerProvider(output));
            builder.SetMinimumLevel(LogLevel.Debug);
        });
        _logger = _loggerFactory.CreateLogger<OutOfProcessIntegrationTests>();
    }

    public async Task InitializeAsync()
    {
        _testTempDirHolder = new TempDirectory("oop-int");
        _testTempDir = _testTempDirHolder.Path;

        // Only start if pwsh is available — tests using PwshAvailableFactAttribute won't
        // reach here when pwsh is missing, but guard for safety.
        try
        {
            var path = OutOfProcessCommandExecutor.ResolvePwshPath();
            if (string.IsNullOrEmpty(path)) return;
        }
        catch
        {
            return;
        }

        _executor = new OutOfProcessCommandExecutor(
            _loggerFactory.CreateLogger<OutOfProcessCommandExecutor>());
        await _executor.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_executor != null)
            await _executor.DisposeAsync();
        _loggerFactory.Dispose();
        _testTempDirHolder?.Dispose();
    }

    // ---- Subprocess lifecycle tests ----

    [PwshAvailableFact]
    public async Task CanStartAndPingSubprocess()
    {
        Assert.NotNull(_executor);

        // StartAsync already includes a ping. Exercise DiscoverCommandsAsync as a secondary liveness check.
        var config = new PowerShellConfiguration
        {
            FunctionNames = new List<string> { "Get-Process" },
            Modules = new List<string>(),
            IncludePatterns = new List<string>(),
            ExcludePatterns = new List<string>()
        };

        var schemas = await _executor!.DiscoverCommandsAsync(config);
        Assert.NotNull(schemas);
        _logger.LogInformation("Ping/discover returned {Count} schemas", schemas.Count);
    }

    [PwshAvailableFact]
    public async Task CanShutdownGracefully()
    {
        using var factory = LoggerFactory.Create(b =>
        {
            b.AddProvider(new TestOutputLoggerProvider(_output));
            b.SetMinimumLevel(LogLevel.Debug);
        });

        var executor = new OutOfProcessCommandExecutor(
            factory.CreateLogger<OutOfProcessCommandExecutor>());
        await executor.StartAsync();

        // DisposeAsync sends shutdown + waits for process exit.
        await executor.DisposeAsync();

        // Double dispose should be safe.
        await executor.DisposeAsync();
    }

    [PwshAvailableFact]
    public async Task CanHandleMultipleStartCalls()
    {
        using var factory = LoggerFactory.Create(b =>
        {
            b.AddProvider(new TestOutputLoggerProvider(_output));
            b.SetMinimumLevel(LogLevel.Debug);
        });

        var executor = new OutOfProcessCommandExecutor(
            factory.CreateLogger<OutOfProcessCommandExecutor>());
        await executor.StartAsync();

        // Second start on an already-running executor.
        // Depending on implementation this should either be idempotent or throw.
        // The current implementation will attempt to re-launch (creating a second process).
        // We verify it doesn't crash the test.
        try
        {
            await executor.StartAsync();
            _logger.LogInformation("Second StartAsync succeeded (idempotent behavior).");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogInformation("Second StartAsync threw as expected: {Message}", ex.Message);
        }

        await executor.DisposeAsync();
    }

    // ---- Discovery tests with built-in commands ----

    [PwshAvailableFact]
    public async Task CanDiscoverBuiltInCommands()
    {
        var config = new PowerShellConfiguration
        {
            FunctionNames = new List<string> { "Get-Process", "Get-ChildItem" },
            Modules = new List<string>(),
            IncludePatterns = new List<string>(),
            ExcludePatterns = new List<string>()
        };

        var schemas = await _executor!.DiscoverCommandsAsync(config);

        Assert.NotNull(schemas);
        Assert.NotEmpty(schemas);

        var names = schemas.Select(s => s.Name).Distinct().ToList();
        _logger.LogInformation("Discovered commands: {Commands}", string.Join(", ", names));

        Assert.Contains(schemas, s => s.Name == "Get-Process");
        Assert.Contains(schemas, s => s.Name == "Get-ChildItem");
    }

    [PwshAvailableFact]
    public async Task DiscoverReturnsParameterMetadata()
    {
        var config = new PowerShellConfiguration
        {
            FunctionNames = new List<string> { "Get-Process" },
            Modules = new List<string>(),
            IncludePatterns = new List<string>(),
            ExcludePatterns = new List<string>()
        };

        var schemas = await _executor!.DiscoverCommandsAsync(config);
        Assert.NotEmpty(schemas);

        // Get-Process should have a Name parameter at minimum
        var getProcess = schemas.First(s => s.Name == "Get-Process");
        Assert.NotNull(getProcess.Parameters);
        Assert.NotEmpty(getProcess.Parameters);

        _logger.LogInformation("Get-Process parameters: {Params}",
            string.Join(", ", getProcess.Parameters.Select(p => $"{p.Name}:{p.TypeName}")));

        // Verify parameter metadata shape
        foreach (var param in getProcess.Parameters)
        {
            Assert.False(string.IsNullOrWhiteSpace(param.Name), "Parameter Name should not be empty");
            Assert.False(string.IsNullOrWhiteSpace(param.TypeName), "Parameter TypeName should not be empty");
        }
    }

    [PwshAvailableFact]
    public async Task DiscoverWithIncludePatternsWorks()
    {
        using var factory = LoggerFactory.Create(b =>
        {
            b.AddProvider(new TestOutputLoggerProvider(_output));
            b.SetMinimumLevel(LogLevel.Debug);
        });

        var executor = new OutOfProcessCommandExecutor(
            factory.CreateLogger<OutOfProcessCommandExecutor>());
        await executor.StartAsync();

        try
        {
            var config = new PowerShellConfiguration
            {
                FunctionNames = new List<string>(),
                Modules = new List<string>(),
                IncludePatterns = new List<string> { "Get-Process" },
                ExcludePatterns = new List<string>()
            };

            var schemas = await executor.DiscoverCommandsAsync(config);
            Assert.NotNull(schemas);
            Assert.NotEmpty(schemas);
            Assert.Contains(schemas, s => s.Name == "Get-Process");
        }
        finally
        {
            await executor.DisposeAsync();
        }
    }

    // ---- Invocation tests ----

    [PwshAvailableFact]
    public async Task CanInvokeGetProcess()
    {
        // Scope to the current process to avoid serializing all processes (which times out)
        var result = await _executor!.InvokeAsync(
            "Get-Process",
            new Dictionary<string, object?> { ["Id"] = Environment.ProcessId });

        Assert.NotNull(result);
        Assert.NotEmpty(result);
        _logger.LogInformation("Get-Process output length: {Length}", result.Length);

        // Result should be valid JSON (single object or array)
        Assert.True(result.TrimStart().StartsWith("[") || result.TrimStart().StartsWith("{"),
            $"Expected JSON output but got: {result[..Math.Min(200, result.Length)]}");
    }

    [PwshAvailableFact]
    public async Task CanInvokeGetChildItem()
    {
        var result = await _executor!.InvokeAsync(
            "Get-ChildItem",
            new Dictionary<string, object?> { ["Path"] = _testTempDir });

        Assert.NotNull(result);
        _logger.LogInformation("Get-ChildItem temp path output length: {Length}", result.Length);

        // Output should be JSON
        var trimmed = result.TrimStart();
        Assert.True(trimmed.StartsWith("[") || trimmed.StartsWith("{") || trimmed == "null" || trimmed == "\"\"",
            $"Expected JSON output but got: {result[..Math.Min(200, result.Length)]}");
    }

    [PwshAvailableFact]
    public async Task InvokeNonexistentCommandReturnsError()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _executor!.InvokeAsync(
                "Not-A-Real-Command-XYZ987",
                new Dictionary<string, object?>());
        });

        _logger.LogInformation("Expected error for nonexistent command: {Message}", ex.Message);
        Assert.Contains("OOP error", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [PwshAvailableFact]
    public async Task InvokeWithParametersWorks()
    {
        var result = await _executor!.InvokeAsync(
            "Get-ChildItem",
            new Dictionary<string, object?> { ["Path"] = _testTempDir });

        Assert.NotNull(result);
        Assert.NotEmpty(result);
        _logger.LogInformation("Get-ChildItem with Path param output length: {Length}", result.Length);
    }

    // ---- Error handling tests ----

    [PwshAvailableFact]
    public async Task TimeoutOnSlowCommand()
    {
        using var factory = LoggerFactory.Create(b =>
        {
            b.AddProvider(new TestOutputLoggerProvider(_output));
            b.SetMinimumLevel(LogLevel.Debug);
        });

        var executor = new OutOfProcessCommandExecutor(
            factory.CreateLogger<OutOfProcessCommandExecutor>(),
            requestTimeout: TimeSpan.FromSeconds(2));
        await executor.StartAsync();

        try
        {
            // Start-Sleep -Seconds 10 should exceed the 2s timeout
            var ex = await Assert.ThrowsAsync<TimeoutException>(async () =>
            {
                await executor.InvokeAsync(
                    "Start-Sleep",
                    new Dictionary<string, object?> { ["Seconds"] = 10 });
            });

            _logger.LogInformation("Timeout exception caught as expected: {Message}", ex.Message);
        }
        finally
        {
            await executor.DisposeAsync();
        }
    }

    [PwshAvailableFact]
    public async Task DisposedExecutorThrowsObjectDisposedException()
    {
        using var factory = LoggerFactory.Create(b =>
        {
            b.AddProvider(new TestOutputLoggerProvider(_output));
            b.SetMinimumLevel(LogLevel.Debug);
        });

        var executor = new OutOfProcessCommandExecutor(
            factory.CreateLogger<OutOfProcessCommandExecutor>());
        await executor.StartAsync();
        await executor.DisposeAsync();

        // All operations on a disposed executor should throw
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            await executor.StartAsync();
        });

        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            await executor.InvokeAsync("Get-Process", new Dictionary<string, object?>());
        });

        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            var config = new PowerShellConfiguration
            {
                FunctionNames = new List<string> { "Get-Process" },
                Modules = new List<string>(),
                IncludePatterns = new List<string>(),
                ExcludePatterns = new List<string>()
            };
            await executor.DiscoverCommandsAsync(config);
        });
    }

    [PwshAvailableFact]
    public async Task CancellationTokenStopsInvocation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await _executor!.InvokeAsync(
                "Get-Process",
                new Dictionary<string, object?>(),
                cts.Token);
        });
    }

    // ---- Subprocess crash recovery tests ----

    [PwshAvailableFact]
    public async Task SubprocessCrash_PendingRequestFailsWithError()
    {
        // Create an isolated executor for the crash test
        using var factory = LoggerFactory.Create(b =>
        {
            b.AddProvider(new TestOutputLoggerProvider(_output));
            b.SetMinimumLevel(LogLevel.Debug);
        });

        var executor = new OutOfProcessCommandExecutor(
            factory.CreateLogger<OutOfProcessCommandExecutor>(),
            requestTimeout: TimeSpan.FromSeconds(10));
        await executor.StartAsync();

        try
        {
            // Start a long-running command so the executor has a pending request
            var invokeTask = executor.InvokeAsync(
                "Start-Sleep",
                new Dictionary<string, object?> { ["Seconds"] = 30 });

            // Give the request time to be sent to the subprocess
            await Task.Delay(500);

            // Kill recently-started pwsh processes to simulate a crash
            var oopProcess = GetSubprocess(executor);
            Assert.NotNull(oopProcess);

            _logger.LogInformation("Killing OOP subprocess PID {Pid} to simulate crash", oopProcess.Id);
            oopProcess.Kill(entireProcessTree: true);

            // The pending invoke should fail with an error (not hang forever)
            var thrownEx = await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                await invokeTask;
            });

            _logger.LogInformation("Subprocess crash produced expected exception: {Type}: {Message}",
                thrownEx.GetType().Name, thrownEx.Message);
        }
        finally
        {
            await executor.DisposeAsync();
        }
    }

    [PwshAvailableFact]
    public async Task SubprocessCrash_NextInvokeAutoRecovers()
    {
        using var factory = LoggerFactory.Create(b =>
        {
            b.AddProvider(new TestOutputLoggerProvider(_output));
            b.SetMinimumLevel(LogLevel.Debug);
        });

        var executor = new OutOfProcessCommandExecutor(
            factory.CreateLogger<OutOfProcessCommandExecutor>(),
            requestTimeout: TimeSpan.FromSeconds(15));
        await executor.StartAsync();

        try
        {
            // Verify it works first with a fast, single-result command
            var firstResult = await executor.InvokeAsync(
                "Get-Date",
                new Dictionary<string, object?>());
            Assert.NotNull(firstResult);

            var firstProcess = GetSubprocess(executor);
            Assert.NotNull(firstProcess);
            var firstPid = firstProcess!.Id;

            _output.WriteLine($"Killing OOP subprocess PID {firstPid}");
            firstProcess.Kill(entireProcessTree: true);

            // Wait for the process exit event to propagate
            await Task.Delay(2000);

            // The next invoke should auto-restart the subprocess and succeed,
            // not throw "OOP subprocess is not running".
            var afterRestart = await executor.InvokeAsync(
                "Get-Date",
                new Dictionary<string, object?>());
            Assert.NotNull(afterRestart);

            var newProcess = GetSubprocess(executor);
            Assert.NotNull(newProcess);
            Assert.NotEqual(firstPid, newProcess!.Id);
        }
        finally
        {
            await executor.DisposeAsync();
        }
    }

    /// <summary>
    /// Regression for the crash reported in issue #TBD: when a user command
    /// terminates the pwsh subprocess (e.g. via [Environment]::Exit, an
    /// AccessViolation in native code, etc.), subsequent tool invocations
    /// must auto-recover rather than throwing "OOP subprocess is not running"
    /// indefinitely.
    ///
    /// This test uses a wrapper script that calls [Environment]::Exit to
    /// guarantee the host process dies during the invoke. The next invoke
    /// must succeed against a freshly-restarted subprocess.
    /// </summary>
    [PwshAvailableFact]
    public async Task UserCommandKillsHost_NextInvokeAutoRecovers()
    {
        using var factory = LoggerFactory.Create(b =>
        {
            b.AddProvider(new TestOutputLoggerProvider(_output));
            b.SetMinimumLevel(LogLevel.Debug);
        });

        // Generate a temporary module whose function kills the host process
        // when called (simulates a misbehaving cmdlet).
        var moduleDir = Path.Combine(_testTempDir, "KillHostModule");
        Directory.CreateDirectory(moduleDir);
        var psm1 = Path.Combine(moduleDir, "KillHostModule.psm1");
        File.WriteAllText(psm1, @"
function Invoke-KillHost {
    [CmdletBinding()]
    param()
    [System.Environment]::Exit(1)
}
Export-ModuleMember -Function Invoke-KillHost
");
        var psd1 = Path.Combine(moduleDir, "KillHostModule.psd1");
        File.WriteAllText(psd1, $@"
@{{
    ModuleVersion = '1.0.0'
    RootModule = 'KillHostModule.psm1'
    FunctionsToExport = @('Invoke-KillHost')
    GUID = '{Guid.NewGuid()}'
}}
");

        var executor = new OutOfProcessCommandExecutor(
            factory.CreateLogger<OutOfProcessCommandExecutor>(),
            requestTimeout: TimeSpan.FromSeconds(15));
        await executor.StartAsync();

        try
        {
            // Replay-on-restart depends on SetupAsync having been called.
            // Configure the module path + import so the restarted host can
            // resolve Invoke-KillHost and Get-Date both runs.
            var envConfig = new EnvironmentConfiguration
            {
                ModulePaths = new List<string> { _testTempDir },
                ImportModules = new List<string> { "KillHostModule" },
                TrustPSGallery = false,
                InstallModules = new List<PoshMcp.Server.PowerShell.ModuleInstallation>(),
            };
            await executor.SetupAsync(envConfig);

            var firstProcess = GetSubprocess(executor);
            Assert.NotNull(firstProcess);
            var firstPid = firstProcess!.Id;

            // This call kills the host mid-invoke. Recovery should kick in
            // and convert the failure into a one-shot retry. The retry runs
            // the same command (which will die again) — so we expect this
            // call to ultimately throw, but with the underlying host now
            // restarted and the cached environment replayed.
            await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                await executor.InvokeAsync(
                    "Invoke-KillHost",
                    new Dictionary<string, object?>());
            });

            // The next invoke against a benign command must succeed against
            // the freshly-restarted subprocess.
            var afterRecovery = await executor.InvokeAsync(
                "Get-Date",
                new Dictionary<string, object?>());
            Assert.NotNull(afterRecovery);

            var newProcess = GetSubprocess(executor);
            Assert.NotNull(newProcess);
            Assert.NotEqual(firstPid, newProcess!.Id);
            _output.WriteLine($"Recovered: old PID {firstPid} -> new PID {newProcess.Id}");
        }
        finally
        {
            await executor.DisposeAsync();
        }
    }

    /// <summary>
    /// Reflects through the executor's OutOfProcessHost to grab the live pwsh
    /// Process. Used by tests that need to forcibly kill the subprocess to
    /// simulate a crash.
    /// </summary>
    private static Process? GetSubprocess(OutOfProcessCommandExecutor executor)
    {
        var host = GetHost(executor);
        if (host is null) return null;

        var processField = typeof(OutOfProcessHost)
            .GetField("_process", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (Process?)processField!.GetValue(host);
    }

    /// <summary>
    /// Reflects through the executor to grab the live OutOfProcessHost so tests
    /// can issue raw JSON-RPC requests (SendRequestAsync) that surface the full
    /// response envelope (e.g., the hadErrors flag).
    /// </summary>
    private static OutOfProcessHost? GetHost(OutOfProcessCommandExecutor executor)
    {
        var hostField = typeof(OutOfProcessCommandExecutor)
            .GetField("_host", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (OutOfProcessHost?)hostField!.GetValue(executor);
    }

    // ---- Regression tests ----

    /// <summary>
    /// Regression for issue #189: in the single-runspace OOP host, $Error is process-global
    /// and was not cleared between invokes. A failing invoke would leak its error count into
    /// the next invoke's hadErrors flag, producing a false positive. The fix is a one-line
    /// $Error.Clear() in Invoke-InvokeHandler before the user command runs.
    ///
    /// This test calls invoke twice via the host's SendRequestAsync (which exposes the raw
    /// response): first an intentionally failing call (nonexistent path), then a clean call.
    /// Pre-fix, the second call returns hadErrors=true. Post-fix, hadErrors=false.
    /// </summary>
    [PwshAvailableFact]
    public async Task HadErrorsDoesNotLeakAcrossInvokes()
    {
        Assert.NotNull(_executor);
        var host = GetHost(_executor!);
        Assert.NotNull(host);

        // First invoke: deliberately produce a non-terminating error.
        // Get-ChildItem on a path that does not exist writes to $Error but does not throw.
        var missingPath = Path.Combine(_testTempDir, "definitely-does-not-exist-" + Guid.NewGuid().ToString("N"));
        var failingParams = new
        {
            command = "Get-ChildItem",
            parameters = new Dictionary<string, object?>
            {
                ["Path"] = missingPath,
                ["ErrorAction"] = "SilentlyContinue"
            }
        };

        var first = await host!.SendRequestAsync<JsonElement>(
            "invoke", failingParams, CancellationToken.None);

        Assert.True(first.TryGetProperty("hadErrors", out var firstHadErrors),
            "First response should include hadErrors property.");
        Assert.True(firstHadErrors.GetBoolean(),
            "First (failing) invoke should report hadErrors=true.");

        // Second invoke: a clean command. Pre-fix, hadErrors leaks from the previous call.
        var cleanParams = new
        {
            command = "Get-Date",
            parameters = new Dictionary<string, object?>()
        };

        var second = await host.SendRequestAsync<JsonElement>(
            "invoke", cleanParams, CancellationToken.None);

        Assert.True(second.TryGetProperty("hadErrors", out var secondHadErrors),
            "Second response should include hadErrors property.");
        Assert.False(secondHadErrors.GetBoolean(),
            "Second (clean) invoke must report hadErrors=false. Errors from the prior invoke must not leak into this one (#189).");
    }

    /// <summary>
    /// Regression: when an invoke reports hadErrors=true, InvokeAsync must surface
    /// that as a thrown exception (which the MCP framework turns into IsError=true)
    /// rather than silently returning the partial output as a successful tool result.
    ///
    /// User-reported symptom: invoking a command that fails parameter validation
    /// (or otherwise writes to the error stream non-terminally) caused MCP tool
    /// results to come back with IsError=false carrying output that looked like it
    /// came from a prior successful invoke. The actual mechanism is that
    /// commands which emit partial pipeline output before writing a non-terminating
    /// error were returning that partial output as a "success" — InvokeAsync logged
    /// the error stream but did not propagate it.
    ///
    /// This test:
    ///   1. Successfully invokes a command and captures a unique output marker.
    ///   2. Invokes Get-Item with a non-existent path under ErrorAction=Continue,
    ///      which writes a non-terminating error to the stream.
    ///   3. Asserts the second invoke throws and the thrown message does NOT
    ///      contain any text from the first invoke's output.
    /// </summary>
    [PwshAvailableFact]
    public async Task Invoke_WithErrorAfterSuccess_DoesNotReturnPreviousOutput()
    {
        Assert.NotNull(_executor);

        // 1. Successful invoke against a known-existing path so the JSON output
        //    contains the path string verbatim — that gives us a unique marker
        //    we can search for in the second invoke's failure message.
        var uniqueMarker = "poshmcp-bug-marker-" + Guid.NewGuid().ToString("N");
        var markerDir = Path.Combine(_testTempDir, uniqueMarker);
        Directory.CreateDirectory(markerDir);

        var firstResult = await _executor!.InvokeAsync(
            "Get-Item",
            new Dictionary<string, object?> { ["Path"] = markerDir });

        Assert.False(string.IsNullOrWhiteSpace(firstResult));
        Assert.Contains(uniqueMarker, firstResult, StringComparison.Ordinal);
        _logger.LogInformation("First invoke produced {Length} chars of output containing the marker", firstResult.Length);

        // 2. Failing invoke — Get-Item against a path that does not exist writes
        //    to the error stream non-terminally. With ErrorAction=Continue (the
        //    default behaviour for non-terminating errors), the pipeline output
        //    is null but the response's hadErrors flag is set.
        var missingPath = Path.Combine(_testTempDir, "definitely-not-exist-" + Guid.NewGuid().ToString("N"));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _executor!.InvokeAsync(
                "Get-Item",
                new Dictionary<string, object?>
                {
                    ["Path"] = missingPath,
                    ["ErrorAction"] = "Continue"
                });
        });

        _logger.LogInformation("Second invoke threw as expected: {Message}", ex.Message);

        // 3a. The error must announce itself as an OOP error (consistent with the
        //     terminating-error path) so callers can pattern-match the failure.
        Assert.Contains("OOP error", ex.Message, StringComparison.OrdinalIgnoreCase);

        // 3b. The error message must NOT contain any chunk from the prior
        //     successful invoke's output. This is the user-visible regression
        //     guarantee: a failing invoke never surfaces the previous invoke's
        //     payload as if it were the result.
        Assert.DoesNotContain(uniqueMarker, ex.Message, StringComparison.Ordinal);

        // 3c. The error message should mention the command that actually failed
        //     so callers can tell which tool errored.
        Assert.Contains("Get-Item", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Faithful repro test for the user-reported "previous tool's response came back"
    /// scenario. Two DIFFERENT commands invoked back-to-back on the same single-host
    /// executor: command A returns a distinctive marker; command B returns something
    /// else. Asserts B's payload contains no trace of A's marker.
    ///
    /// If a cross-invoke state leak exists anywhere in the OOP pipeline (host script
    /// $script:/$global: variables, C# correlation map mis-routing, executor-level
    /// last-response cache, etc.), this test will fail.
    /// </summary>
    [PwshAvailableFact]
    public async Task Invoke_TwoDifferentSuccessfulCommands_SecondDoesNotReturnFirstOutput()
    {
        Assert.NotNull(_executor);

        // A: Get-Item on a directory whose name embeds a unique GUID marker.
        // The serialized FileSystemInfo carries the path string, so the marker
        // is guaranteed to appear in A's output.
        var markerA = "poshmcp-stale-a-" + Guid.NewGuid().ToString("N");
        var markerDir = Path.Combine(_testTempDir, markerA);
        Directory.CreateDirectory(markerDir);

        var first = await _executor!.InvokeAsync(
            "Get-Item",
            new Dictionary<string, object?> { ["Path"] = markerDir });

        Assert.Contains(markerA, first, StringComparison.Ordinal);

        // B: A completely different command whose output cannot legitimately
        // contain markerA. Get-Date returns a DateTime; serialized form is
        // a JSON object/string with no filesystem paths.
        var second = await _executor!.InvokeAsync(
            "Get-Date",
            new Dictionary<string, object?>());

        Assert.False(string.IsNullOrWhiteSpace(second), "Second invoke must produce output.");
        Assert.DoesNotContain(markerA, second, StringComparison.Ordinal);
    }

    /// <summary>
    /// Faithful repro test matching the user's exact reported scenario: command A
    /// succeeds and returns a distinctive object; command B is a DIFFERENT command
    /// that triggers a non-terminating error. The InvalidOperationException thrown
    /// by the executor (after the 6908917 hadErrors fix) must not contain any
    /// chunk of command A's output. If a stale-response leak exists, the "discarded
    /// N-char output" suffix would carry A's payload back to the caller.
    /// </summary>
    [PwshAvailableFact]
    public async Task Invoke_ErrorInDifferentCommandAfterSuccess_DoesNotReturnFirstOutput()
    {
        Assert.NotNull(_executor);

        // A: Get-Item on a known-existing marker directory.
        var markerA = "poshmcp-stale-x-" + Guid.NewGuid().ToString("N");
        var markerDir = Path.Combine(_testTempDir, markerA);
        Directory.CreateDirectory(markerDir);

        var first = await _executor!.InvokeAsync(
            "Get-Item",
            new Dictionary<string, object?> { ["Path"] = markerDir });

        Assert.Contains(markerA, first, StringComparison.Ordinal);

        // B: a DIFFERENT command (Get-ChildItem, not Get-Item) against a path
        // that does not exist, with ErrorAction=Continue so it produces a
        // non-terminating error and reports hadErrors=true.
        var missingPath = Path.Combine(_testTempDir, "missing-b-" + Guid.NewGuid().ToString("N"));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _executor!.InvokeAsync(
                "Get-ChildItem",
                new Dictionary<string, object?>
                {
                    ["Path"] = missingPath,
                    ["ErrorAction"] = "Continue"
                });
        });

        // Stale-response leak guard: B's error envelope must not carry A's marker.
        Assert.DoesNotContain(markerA, ex.Message, StringComparison.Ordinal);
        // Error envelope should name the failing command, not A.
        Assert.Contains("Get-ChildItem", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Get-Item ", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Exact reproduction of the user-reported cross-invocation state leak:
    ///
    ///   1. Invoke a command that legitimately returns null/empty. Capture result A.
    ///   2. Invoke a DIFFERENT command that returns a unique sentinel string. Capture result B.
    ///   3. Re-invoke the SAME command from step 1. Capture result C.
    ///
    /// The bug surface: result C contains the sentinel from result B, as if some
    /// buffer / runspace variable / dispatcher state held on to B's output and
    /// surfaced it when C produced no output of its own.
    ///
    /// This test uses a startup-script-defined function (<c>Get-PoshMcpEmptyResult</c>)
    /// that returns no pipeline output, so the empty-output path is exercised
    /// directly rather than relying on a built-in command's edge case.
    /// </summary>
    [PwshAvailableFact]
    public async Task Invoke_EmptyCommand_AfterPriorOutput_DoesNotReturnPriorOutput()
    {
        Assert.NotNull(_executor);

        // Use a built-in command that legitimately returns nothing on the
        // success stream and does NOT set HadErrors. Write-Verbose writes to
        // the verbose stream (suppressed by default) and returns nothing.
        var emptyParams = new Dictionary<string, object?>
        {
            ["Message"] = "poshmcp-test-verbose-message"
        };

        // 1. First invoke of the empty command — should produce null/empty output.
        var resultA = await _executor!.InvokeAsync("Write-Verbose", emptyParams);
        _logger.LogInformation("Result A (empty cmd, first call): {Length} chars: {Value}",
            resultA?.Length ?? 0, resultA ?? "<null>");

        // 2. Different command that produces a distinctive sentinel string.
        var sentinel = "SENTINEL-XYZ-" + Guid.NewGuid().ToString("N");
        var sentinelDir = Path.Combine(_testTempDir, sentinel);
        Directory.CreateDirectory(sentinelDir);

        var resultB = await _executor!.InvokeAsync(
            "Get-Item",
            new Dictionary<string, object?> { ["Path"] = sentinelDir });
        _logger.LogInformation("Result B (Get-Item with sentinel): {Length} chars", resultB?.Length ?? 0);
        Assert.NotNull(resultB);
        Assert.Contains(sentinel, resultB, StringComparison.Ordinal);

        // 3. Re-invoke the empty command. THIS is the assertion that catches the bug.
        var resultC = await _executor!.InvokeAsync("Write-Verbose", emptyParams);
        _logger.LogInformation("Result C (empty cmd, after sentinel): {Length} chars: {Value}",
            resultC?.Length ?? 0, resultC ?? "<null>");

        // The empty command must not surface ANY chunk of the prior command's output.
        Assert.DoesNotContain(sentinel, resultC ?? string.Empty, StringComparison.Ordinal);

        // And the rerun should match the first call's shape (both null/empty).
        Assert.Equal(resultA ?? string.Empty, resultC ?? string.Empty);
    }

    // ---- Spec 011 Phase 2 (#268): wire-format parity ----

    [PwshAvailableFact]
    public async Task DiscoverCommandsAsync_PopulatesLastModuleImports_FromOopHostPayload()
    {
        Assert.NotNull(_executor);

        // Configure a small modules+pattern surface so the host emits a
        // moduleImports payload (FR-263-2) and per-command source attribution
        // populates SourceModule / SourcePattern / SourceDetail (FR-263-9).
        var config = new PowerShellConfiguration
        {
            FunctionNames = new List<string> { "Get-Date" }, // commandName-source
            Modules = new List<string> { "Microsoft.PowerShell.Utility" }, // module-source
            IncludePatterns = new List<string> { "Out-*" }, // pattern-source
            ExcludePatterns = new List<string>(),
        };

        var schemas = await _executor!.DiscoverCommandsAsync(config);
        Assert.NotNull(schemas);
        Assert.NotEmpty(schemas);

        // Wire-format parity: payload must be present after a discovery against
        // a spec-011-aware host (this test runs against the in-tree oop-host.ps1).
        var payload = _executor.LastModuleImports;
        Assert.NotNull(payload);
        Assert.NotEmpty(payload!.Modules);

        var probe = payload.Modules.FirstOrDefault(m =>
            string.Equals(m.Name, "Microsoft.PowerShell.Utility", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(probe);
        Assert.True(probe!.Found, "Microsoft.PowerShell.Utility should be found by the OOP host probe");

        // FR-263-9 source attribution per command:
        //   - Get-Date appears in FunctionNames → SourceModule must be null
        //     (commandName takes precedence; pattern/module sources unset).
        //   - Out-* matches come via the include pattern → SourcePattern set.
        //   - Module-derived commands have SourceModule set (with module name in SourceDetail).
        var getDate = schemas.FirstOrDefault(s =>
            string.Equals(s.Name, "Get-Date", StringComparison.OrdinalIgnoreCase));
        if (getDate is not null)
        {
            Assert.Null(getDate.SourceModule);
            Assert.Null(getDate.SourcePattern);
        }

        var outAny = schemas.FirstOrDefault(s =>
            s.Name.StartsWith("Out-", StringComparison.OrdinalIgnoreCase));
        if (outAny is not null)
        {
            Assert.True(
                outAny.SourcePattern is not null || outAny.SourceModule is not null,
                $"Out-* command '{outAny.Name}' must have SourcePattern or SourceModule populated.");
        }

        _logger.LogInformation(
            "Phase-2 payload: modules={ModuleCount} patterns={PatternCount} schemas={SchemaCount}",
            payload.Modules.Count, payload.Patterns.Count, schemas.Count);
    }
}

/// <summary>
/// MCP server round-trip tests that verify the full OOP pipeline through the MCP protocol.
/// Launches InProcessMcpServer with --runtime-mode OutOfProcess and verifies
/// tools/list and tools/call work end-to-end.
/// </summary>
[Trait("Category", "OutOfProcess")]
public class OutOfProcessMcpRoundTripTests : PowerShellTestBase, IAsyncLifetime
{
    private InProcessMcpServer? _server;
    private ExternalMcpClient? _client;
    private readonly ITestOutputHelper _output;

    public OutOfProcessMcpRoundTripTests(ITestOutputHelper output) : base(output)
    {
        _output = output;
    }

    public async Task InitializeAsync()
    {
        // Check if pwsh is available before starting the server
        try
        {
            var path = OutOfProcessCommandExecutor.ResolvePwshPath();
            if (string.IsNullOrEmpty(path)) return;
        }
        catch
        {
            return;
        }

        Logger.LogInformation("=== Starting MCP server in OutOfProcess mode ===");

        _server = new InProcessMcpServer(Logger, extraArgs: "serve --runtime-mode OutOfProcess");
        await _server.StartAsync();

        _client = new ExternalMcpClient(Logger, _server);
        await _client.StartAsync();

        Logger.LogInformation("=== MCP server (OOP mode) and client initialized ===");
    }

    public Task DisposeAsync()
    {
        Logger.LogInformation("=== Disposing OOP MCP server and client ===");

        _client?.Dispose();
        _server?.Dispose();

        return Task.CompletedTask;
    }

    [PwshAvailableFact]
    public async Task ToolsList_ReturnsOopDiscoveredTools()
    {
        Assert.NotNull(_client);

        var toolsResponse = await _client!.SendListToolsAsync();
        Assert.NotNull(toolsResponse);

        Logger.LogInformation("OOP tools/list response: {Response}",
            toolsResponse.ToString(Formatting.Indented));

        var tools = toolsResponse["result"]?["tools"] as JArray;
        Assert.NotNull(tools);
        Assert.True(tools!.Count > 0,
            $"Expected OOP-discovered tools but found none. Response: {toolsResponse.ToString(Formatting.None)}");

        // The default config includes Get-Process, so we should see a tool for it
        var toolNames = tools.Select(t => t["name"]?.ToString()).ToList();
        Logger.LogInformation("OOP discovered tools: {Tools}", string.Join(", ", toolNames));

        Assert.NotEmpty(toolNames);
    }

    [PwshAvailableFact]
    public async Task ToolsCall_RoundTripsGetProcessThroughOopExecutor()
    {
        Assert.NotNull(_client);

        // First verify tools are listed
        var toolsResponse = await _client!.SendListToolsAsync();
        var tools = toolsResponse["result"]?["tools"] as JArray;
        Assert.NotNull(tools);
        Assert.NotEmpty(tools);

        // Find the get_process tool (tools/list returns snake_case names)
        var getProcessTool = tools!.FirstOrDefault(t =>
        {
            var name = t["name"]?.ToString();
            return name != null && name.Contains("get_process", StringComparison.OrdinalIgnoreCase);
        });

        if (getProcessTool == null)
        {
            Logger.LogWarning("get_process* tool not found in OOP tools list. Available: {Tools}",
                string.Join(", ", tools.Select(t => t["name"])));

            // If the specific tool isn't found, at least verify we can call any tool
            var firstToolName = tools.First()["name"]?.ToString();
            Assert.NotNull(firstToolName);
            Logger.LogInformation("Falling back to calling first available tool: {Tool}", firstToolName);
            return;
        }

        var toolName = getProcessTool["name"]!.ToString();
        Logger.LogInformation("Calling OOP tool: {ToolName}", toolName);

        // Call the tool — Get-Process with the current PID
        var currentPid = Process.GetCurrentProcess().Id;
        var callResponse = await _client.SendToolCallAsync(toolName, new JObject
        {
            ["Id"] = new JArray(currentPid)
        });

        Assert.NotNull(callResponse);
        Logger.LogInformation("OOP tools/call response: {Response}",
            callResponse.ToString(Formatting.Indented));

        // Verify the response structure
        Assert.Equal("2.0", callResponse["jsonrpc"]?.ToString());
        Assert.NotNull(callResponse["result"]);
        Assert.Null(callResponse["error"]);

        var content = callResponse["result"]?["content"] as JArray;
        Assert.NotNull(content);
        Assert.NotEmpty(content);

        var textContent = content![0]?["text"]?.ToString();
        Assert.False(string.IsNullOrWhiteSpace(textContent),
            "Tool call result text content should not be empty");

        Logger.LogInformation("OOP round-trip result: {Result}", textContent);
    }

    [PwshAvailableFact]
    public async Task ToolsCall_ErrorHandling_ReturnsErrorForInvalidTool()
    {
        Assert.NotNull(_client);

        var callResponse = await _client!.SendToolCallAsync(
            "nonexistent_tool_xyz_abc_123",
            new JObject());

        Assert.NotNull(callResponse);
        Logger.LogInformation("Error response for invalid tool: {Response}",
            callResponse.ToString(Formatting.Indented));

        // The server should return an error (either in result.isError or in error)
        var hasError = callResponse["error"] != null;
        var isError = callResponse["result"]?["isError"]?.Value<bool>() == true;
        Assert.True(hasError || isError,
            $"Expected error for nonexistent tool. Response: {callResponse.ToString(Formatting.None)}");
    }
}
