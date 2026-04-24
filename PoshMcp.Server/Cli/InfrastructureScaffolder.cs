using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace PoshMcp;

internal static class InfrastructureScaffolder
{
    private static readonly (string ResourceSuffix, string RelativePath)[] AzureAssets =
    {
        ("Infrastructure.Azure.deploy.ps1", "infra/azure/deploy.ps1"),
        ("Infrastructure.Azure.validate.ps1", "infra/azure/validate.ps1"),
        ("Infrastructure.Azure.main.bicep", "infra/azure/main.bicep"),
        ("Infrastructure.Azure.resources.bicep", "infra/azure/resources.bicep"),
        ("Infrastructure.Azure.parameters.json", "infra/azure/parameters.json"),
        ("Infrastructure.Azure.deploy.appsettings.json.template", "infra/azure/deploy.appsettings.json.template"),
        ("Infrastructure.Azure.parameters.local.json.template", "infra/azure/parameters.local.json.template")
    };

    internal static async Task<ScaffoldInfraResult> ScaffoldAzureInfrastructureAsync(string targetProjectPath, bool force)
    {
        if (string.IsNullOrWhiteSpace(targetProjectPath))
        {
            throw new ArgumentException("Target project path is required.", nameof(targetProjectPath));
        }

        var absoluteProjectPath = Path.GetFullPath(targetProjectPath);
        Directory.CreateDirectory(absoluteProjectPath);

        var assembly = Assembly.GetExecutingAssembly();
        var resourceNames = assembly.GetManifestResourceNames();

        var filesWritten = 0;
        var filesOverwritten = 0;

        foreach (var asset in AzureAssets)
        {
            var destinationPath = Path.Combine(absoluteProjectPath, asset.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            var destinationDirectory = Path.GetDirectoryName(destinationPath)
                ?? throw new InvalidOperationException($"Unable to determine target directory for '{destinationPath}'.");

            Directory.CreateDirectory(destinationDirectory);

            var destinationExists = File.Exists(destinationPath);
            if (destinationExists && !force)
            {
                throw new IOException($"Target file already exists: {destinationPath}. Use --force to overwrite.");
            }

            var resourceName = resourceNames
                .FirstOrDefault(name => name.EndsWith(asset.ResourceSuffix, StringComparison.OrdinalIgnoreCase));

            if (resourceName is null)
            {
                throw new InvalidOperationException($"Embedded infrastructure resource was not found: {asset.ResourceSuffix}");
            }

            await using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Unable to open embedded resource '{resourceName}'.");
            await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await stream.CopyToAsync(fileStream);

            filesWritten++;
            if (destinationExists)
            {
                filesOverwritten++;
            }
        }

        return new ScaffoldInfraResult(
            absoluteProjectPath,
            "infra/azure",
            filesWritten,
            filesOverwritten,
            force);
    }
}

internal sealed record ScaffoldInfraResult(
    string ProjectPath,
    string RelativeInfraPath,
    int FilesWritten,
    int FilesOverwritten,
    bool Force);
