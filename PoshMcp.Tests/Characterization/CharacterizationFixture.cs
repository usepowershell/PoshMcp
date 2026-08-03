using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace PoshMcp.Tests.Characterization;

/// <summary>
/// xUnit class fixture shared across all <see cref="V1BaselineCharacterizationTests"/>.
/// Starts one shared warm HTTP server before the first test and disposes it after the
/// last, then writes the collected scenario results to the v1-baseline JSON artifact.
/// </summary>
public sealed class CharacterizationFixture : IAsyncLifetime
{
    private CharacterizationHttpServer? _warmServer;
    private readonly ConcurrentBag<CharacterizationScenario> _scenarios = new();

    internal CharacterizationHttpServer WarmServer =>
        _warmServer ?? throw new InvalidOperationException("CharacterizationFixture not initialized.");

    internal void RecordScenario(CharacterizationScenario scenario) => _scenarios.Add(scenario);

    /// <summary>
    /// Resolves the path to a characterization config asset in the test output directory.
    /// </summary>
    internal static string ResolveAssetPath(string filename) =>
        Path.Combine(AppContext.BaseDirectory, "Characterization", "Assets", filename);

    public async Task InitializeAsync()
    {
        _warmServer = new CharacterizationHttpServer();
        await _warmServer.StartAsync(ResolveAssetPath("with-startup-script.appsettings.json"));
    }

    public async Task DisposeAsync()
    {
        if (_warmServer is not null)
            await _warmServer.DisposeAsync();

        await WriteArtifactAsync();
    }

    private Task WriteArtifactAsync()
    {
        var path = Environment.GetEnvironmentVariable("CHARACTERIZATION_ARTIFACT_PATH")
            ?? Path.Combine("TestResults", "v1-baseline-characterization.json");

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var orderedScenarios = new List<CharacterizationScenario>(_scenarios);
        orderedScenarios.Sort((a, b) => string.Compare(a.Scenario, b.Scenario, StringComparison.Ordinal));

        var artifact = new CharacterizationArtifact
        {
            CapturedAt = DateTime.UtcNow.ToString("O"),
            SdkPackageVersion = "ModelContextProtocol 1.4.1",
            RuntimeInfo = new CharacterizationRuntimeInfo
            {
                DotNetVersion = Environment.Version.ToString(),
                Os = Environment.OSVersion.ToString(),
                LogicalProcessors = Environment.ProcessorCount,
                MachineName = Environment.MachineName,
            },
            Scenarios = orderedScenarios,
        };

        var json = JsonSerializer.Serialize(artifact, new JsonSerializerOptions { WriteIndented = true });
        return File.WriteAllTextAsync(path, json);
    }
}
