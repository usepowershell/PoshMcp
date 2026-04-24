using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Integration;

public class DeployScriptConfigurationPrecedenceTests
{
    private readonly ITestOutputHelper _output;

    public DeployScriptConfigurationPrecedenceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DeployScript_RegistryNameResolution_ShouldFollowCliThenEnvThenAppsettingsPrecedence()
    {
        var repoRoot = GetRepositoryRoot();
        var deployScriptPath = Path.Combine(repoRoot, "infrastructure", "azure", "deploy.ps1");

        using var tempDirectory = new TemporaryDirectory();
        var appSettingsPath = Path.Combine(tempDirectory.Path, "deploy.appsettings.json");
        await File.WriteAllTextAsync(
            appSettingsPath,
            "{\n  \"AzureDeployment\": {\n    \"RegistryName\": \"appsettings-reg\"\n  }\n}\n");

        var cliCase = await RunDeployWithMocksAsync(
            deployScriptPath,
            new Dictionary<string, string>
            {
                ["AppSettingsFile"] = appSettingsPath,
                ["RegistryName"] = "cli-reg"
            },
            new Dictionary<string, string?>
            {
                ["REGISTRY_NAME"] = "env-reg",
                ["DEPLOY_APPSETTINGS_FILE"] = appSettingsPath
            });

        Assert.Equal(0, cliCase.ExitCode);
        Assert.Contains("RegistryName: cli", cliCase.CombinedOutput, StringComparison.Ordinal);
        Assert.Contains("Checking Azure Container Registry: cli-reg", cliCase.CombinedOutput, StringComparison.Ordinal);

        var envCase = await RunDeployWithMocksAsync(
            deployScriptPath,
            new Dictionary<string, string>
            {
                ["AppSettingsFile"] = appSettingsPath
            },
            new Dictionary<string, string?>
            {
                ["REGISTRY_NAME"] = "env-reg",
                ["DEPLOY_APPSETTINGS_FILE"] = appSettingsPath
            });

        Assert.Equal(0, envCase.ExitCode);
        Assert.Contains("RegistryName: env:REGISTRY_NAME", envCase.CombinedOutput, StringComparison.Ordinal);
        Assert.Contains("Checking Azure Container Registry: env-reg", envCase.CombinedOutput, StringComparison.Ordinal);

        var appSettingsCase = await RunDeployWithMocksAsync(
            deployScriptPath,
            new Dictionary<string, string>
            {
                ["AppSettingsFile"] = appSettingsPath
            },
            new Dictionary<string, string?>
            {
                ["REGISTRY_NAME"] = string.Empty,
                ["DEPLOY_APPSETTINGS_FILE"] = string.Empty
            });

        Assert.Equal(0, appSettingsCase.ExitCode);
        Assert.Contains("RegistryName: appsettings:RegistryName", appSettingsCase.CombinedOutput, StringComparison.Ordinal);
        Assert.Contains("Checking Azure Container Registry: appsettings-reg", appSettingsCase.CombinedOutput, StringComparison.Ordinal);
    }

    private async Task<(int ExitCode, string CombinedOutput)> RunDeployWithMocksAsync(
        string deployScriptPath,
        IReadOnlyDictionary<string, string> cliArguments,
        IReadOnlyDictionary<string, string?> environmentOverrides)
    {
        using var tempDirectory = new TemporaryDirectory();
        var harnessPath = Path.Combine(tempDirectory.Path, "deploy-harness.ps1");

        var cliArgumentBuilder = new StringBuilder();
        cliArgumentBuilder.AppendLine("$deployParams = @{}");
        foreach (var argument in cliArguments)
        {
            cliArgumentBuilder.AppendLine($"$deployParams['{EscapeSingleQuoted(argument.Key)}'] = '{EscapeSingleQuoted(argument.Value)}'");
        }

        var harnessScript = $@"
$ErrorActionPreference = 'Stop'

function az {{
    $text = ($args | ForEach-Object {{ [string]$_ }}) -join ' '
    switch -Regex ($text) {{
        'account show.*tenantId' {{ 'tenant-test'; $global:LASTEXITCODE = 0; return }}
        'account show.*query name' {{ 'sub-test'; $global:LASTEXITCODE = 0; return }}
        'account show.*query id' {{ 'sub-id-test'; $global:LASTEXITCODE = 0; return }}
        'group show' {{ $global:LASTEXITCODE = 1; return }}
        'group create' {{ $global:LASTEXITCODE = 0; return }}
        'acr show' {{ $global:LASTEXITCODE = 1; return }}
        'acr create' {{ $global:LASTEXITCODE = 0; return }}
        'acr login' {{ $global:LASTEXITCODE = 0; return }}
        'deployment sub create' {{ $global:LASTEXITCODE = 0; return }}
        'containerapp show.*ingress\.fqdn' {{ 'mock-app.contoso.test'; $global:LASTEXITCODE = 0; return }}
        default {{ $global:LASTEXITCODE = 0; return }}
    }}
}}

function docker {{
    $global:LASTEXITCODE = 0
}}

function poshmcp {{
    $global:LASTEXITCODE = 0
}}

function Invoke-WebRequest {{
    [pscustomobject]@{{ StatusCode = 200 }}
}}

{cliArgumentBuilder}

& '{EscapeSingleQuoted(deployScriptPath)}' @deployParams
";

        await File.WriteAllTextAsync(harnessPath, harnessScript);

        var startInfo = new ProcessStartInfo
        {
            FileName = "pwsh",
            Arguments = $"-NoProfile -NonInteractive -File \"{harnessPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.Environment["REGISTRY_NAME"] = string.Empty;
        startInfo.Environment["DEPLOY_APPSETTINGS_FILE"] = string.Empty;

        foreach (var pair in environmentOverrides)
        {
            startInfo.Environment[pair.Key] = pair.Value ?? string.Empty;
        }

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                outputBuilder.AppendLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                errorBuilder.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();

        var combined = outputBuilder.ToString() + Environment.NewLine + errorBuilder.ToString();
        _output.WriteLine(combined);

        return (process.ExitCode, combined);
    }

    private static string EscapeSingleQuoted(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }

    private static string GetRepositoryRoot()
    {
        var currentDir = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(currentDir, "PoshMcp.sln")))
        {
            var parent = Directory.GetParent(currentDir);
            if (parent is null)
            {
                throw new InvalidOperationException("Could not find repository root (PoshMcp.sln)");
            }

            currentDir = parent.FullName;
        }

        return currentDir;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; }

        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"poshmcp-deploy-precedence-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}