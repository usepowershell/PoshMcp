using System;
using System.Diagnostics;
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
