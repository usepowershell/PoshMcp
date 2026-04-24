using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace PoshMcp;

internal static class DockerRunner
{
    internal const string DefaultSourceImage = "ghcr.io/usepowershell/poshmcp/poshmcp";
    internal const string DefaultSourceTag = "latest";


    /// <summary>
    /// Detects whether docker or podman is available in the system PATH.
    /// Returns the command name ("docker" or "podman") or null if neither is available.
    /// </summary>
    internal static string? DetectDockerCommand()
    {
        // Try docker first
        if (CommandExists("docker"))
        {
            return "docker";
        }

        // Fall back to podman
        if (CommandExists("podman"))
        {
            return "podman";
        }

        return null;
    }

    /// <summary>
    /// Checks if a command exists in the system PATH by attempting to execute it with --version.
    /// </summary>
    internal static bool CommandExists(string command)
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var process = Process.Start(processInfo))
            {
                return process?.WaitForExit(5000) == true && process.ExitCode == 0;
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Executes a docker or podman command and returns the exit code.
    /// Optionally runs in interactive mode for terminal interaction.
    /// </summary>
    internal static int ExecuteDockerCommand(string dockerCommand, string arguments, bool interactive = false)
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = dockerCommand,
                Arguments = arguments,
                RedirectStandardOutput = !interactive,
                RedirectStandardError = !interactive,
                UseShellExecute = false,
                CreateNoWindow = !interactive
            };

            using (var process = Process.Start(processInfo))
            {
                if (process == null)
                {
                    return ExitCodes.RuntimeError;
                }

                if (!interactive)
                {
                    var outputTask = Task.Run(() =>
                    {
                        string? line;
                        while ((line = process.StandardOutput.ReadLine()) != null)
                        {
                            Console.WriteLine(line);
                        }
                    });

                    var errorTask = Task.Run(() =>
                    {
                        string? line;
                        while ((line = process.StandardError.ReadLine()) != null)
                        {
                            Console.Error.WriteLine(line);
                        }
                    });

                    process.WaitForExit();
                    Task.WaitAll(outputTask, errorTask);
                }
                else
                {
                    process.WaitForExit();
                }

                return process.ExitCode;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to execute {dockerCommand}: {ex.Message}");
            return ExitCodes.RuntimeError;
        }
    }

    /// <summary>
    /// Builds the argument string for a docker/podman build command.
    /// </summary>
    /// <param name="imageFile">Path to the Dockerfile.</param>
    /// <param name="imageTag">Image tag (e.g., "poshmcp:latest").</param>
    /// <param name="modules">Optional space-separated list of PowerShell modules to pre-install via INSTALL_PS_MODULES build arg.</param>
    /// <param name="sourceImage">Optional source/base image reference for Dockerfiles that support BASE_IMAGE build arg.</param>
    /// <returns>The full argument string to pass to docker/podman.</returns>
    internal static string BuildDockerBuildArgs(string imageFile, string imageTag, string? modules = null, string? sourceImage = null)
    {
        var buildArgs = $"build -f {imageFile} -t {imageTag}";

        if (!string.IsNullOrWhiteSpace(sourceImage))
        {
            buildArgs += $" --build-arg BASE_IMAGE=\"{sourceImage}\"";
        }

        if (!string.IsNullOrWhiteSpace(modules))
        {
            buildArgs += $" --build-arg INSTALL_PS_MODULES=\"{modules}\"";
        }

        return $"{buildArgs} .";
    }

    /// <summary>
    /// Resolves a source image reference from optional image and tag inputs.
    /// If no source image is supplied, defaults to the latest published GHCR image for this project.
    /// </summary>
    internal static string ResolveSourceImageReference(string? sourceImage, string? sourceTag)
    {
        var resolvedImage = string.IsNullOrWhiteSpace(sourceImage)
            ? DefaultSourceImage
            : sourceImage.Trim();

        if (!string.IsNullOrWhiteSpace(sourceTag))
        {
            var normalizedTag = sourceTag.Trim();
            var imageWithoutTagOrDigest = RemoveTagOrDigest(resolvedImage);
            return $"{imageWithoutTagOrDigest}:{normalizedTag}";
        }

        if (HasTagOrDigest(resolvedImage))
        {
            return resolvedImage;
        }

        return $"{resolvedImage}:{DefaultSourceTag}";
    }

    /// <summary>
    /// Reads an embedded Dockerfile by its short name (e.g., "Dockerfile", "Dockerfile.user").
    /// Returns null if no embedded resource with that name is found.
    /// </summary>
    internal static string? ReadEmbeddedDockerfile(string name)
    {
        var resourceName = $"PoshMcp.Dockerfiles.{name}";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            return null;
        }
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Maps a source Dockerfile path to the embedded resource short name.
    /// Returns null if the path does not match a known embedded Dockerfile.
    /// </summary>
    internal static string? GetEmbeddedDockerfileName(string sourceDockerfilePath)
    {
        var normalized = sourceDockerfilePath.Replace('\\', '/').TrimStart('.').TrimStart('/');
        return normalized switch
        {
            "Dockerfile" => "Dockerfile",
            "examples/Dockerfile.user" => "Dockerfile.user",
            "examples/Dockerfile.azure" => "Dockerfile.azure",
            "examples/Dockerfile.custom" => "Dockerfile.custom",
            _ => null
        };
    }

    /// <summary>
    /// Generates a Dockerfile on disk from a source Dockerfile, prepending a comment header.
    /// Reads the source from embedded resources first, falling back to disk.
    /// </summary>
    /// <param name="sourceDockerfilePath">Path to the source Dockerfile to read content from.</param>
    /// <param name="outputPath">Path where the generated Dockerfile will be written.</param>
    /// <param name="imageTag">The image tag that would be used in the equivalent build command.</param>
    /// <param name="modules">Optional INSTALL_PS_MODULES build arg value.</param>
    /// <param name="sourceImage">Optional BASE_IMAGE build arg value.</param>
    /// <param name="appSettingsPath">Optional path to a local appsettings.json to bundle at /app/server/appsettings.json.</param>
    /// <returns>The resolved output path.</returns>
    internal static string GenerateDockerfile(
        string sourceDockerfilePath,
        string outputPath,
        string imageTag,
        string? modules = null,
        string? sourceImage = null,
        string? appSettingsPath = null)
    {
        var embeddedName = GetEmbeddedDockerfileName(sourceDockerfilePath);
        var content = (embeddedName != null ? ReadEmbeddedDockerfile(embeddedName) : null)
            ?? File.ReadAllText(sourceDockerfilePath);

        if (!string.IsNullOrWhiteSpace(appSettingsPath))
        {
            const string placeholder = "COPY appsettings.json /app/server/appsettings.json";
            const string injected = "COPY poshmcp-appsettings.json /app/server/appsettings.json";
            if (content.Contains(placeholder))
            {
                content = content.Replace(placeholder, injected);
            }
            else if (content.Contains("USER appuser"))
            {
                content = content.Replace("USER appuser", $"{injected}\nUSER appuser");
            }
            else
            {
                content += $"\n{injected}\n";
            }
        }

        var equivalentCommand = $"docker build -f {outputPath} -t {imageTag}";
        if (!string.IsNullOrWhiteSpace(sourceImage))
        {
            equivalentCommand += $" --build-arg BASE_IMAGE=\"{sourceImage}\"";
        }
        if (!string.IsNullOrWhiteSpace(modules))
        {
            equivalentCommand += $" --build-arg INSTALL_PS_MODULES=\"{modules}\"";
        }
        equivalentCommand += " .";

        var timestamp = DateTime.UtcNow.ToString("o");
        var header =
            $"# Generated by poshmcp build\n" +
            $"# Equivalent build command: {equivalentCommand}\n" +
            $"# Generated: {timestamp}\n" +
            (!string.IsNullOrWhiteSpace(appSettingsPath) ? $"# appsettings source: {appSettingsPath}\n" : "") +
            "\n";

        var resolvedOutput = Path.GetFullPath(outputPath);
        var outputDir = Path.GetDirectoryName(resolvedOutput);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        File.WriteAllText(resolvedOutput, header + content);
        return resolvedOutput;
    }

    /// <summary>
    /// Copies the appsettings file to a well-known staging path in the build context directory.
    /// </summary>
    /// <param name="appSettingsPath">Path to the source appsettings file, or null to skip.</param>
    /// <param name="buildContextDir">Directory that serves as the Docker build context.</param>
    /// <returns>The full path of the staged file, or null if <paramref name="appSettingsPath"/> was null/empty.</returns>
    internal static string? PrepareAppSettingsForBuild(string? appSettingsPath, string buildContextDir)
    {
        if (string.IsNullOrWhiteSpace(appSettingsPath))
        {
            return null;
        }

        var dest = Path.Combine(buildContextDir, "poshmcp-appsettings.json");
        File.Copy(appSettingsPath, dest, overwrite: true);
        return Path.GetFullPath(dest);
    }

    /// <summary>
    /// Deletes a file if it exists; ignores errors (best-effort cleanup).
    /// </summary>
    internal static void CleanupTempFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // best-effort
        }
    }

    private static bool HasTagOrDigest(string imageReference)
    {
        if (imageReference.Contains('@'))
        {
            return true;
        }

        var slashIndex = imageReference.LastIndexOf('/');
        var colonIndex = imageReference.LastIndexOf(':');
        return colonIndex > slashIndex;
    }

    private static string RemoveTagOrDigest(string imageReference)
    {
        var digestIndex = imageReference.IndexOf('@');
        if (digestIndex >= 0)
        {
            return imageReference[..digestIndex];
        }

        var slashIndex = imageReference.LastIndexOf('/');
        var colonIndex = imageReference.LastIndexOf(':');
        if (colonIndex > slashIndex)
        {
            return imageReference[..colonIndex];
        }

        return imageReference;
    }
}
