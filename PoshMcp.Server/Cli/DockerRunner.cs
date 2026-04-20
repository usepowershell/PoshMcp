using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace PoshMcp;

internal static class DockerRunner
{


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
    /// <returns>The full argument string to pass to docker/podman.</returns>
    internal static string BuildDockerBuildArgs(string imageFile, string imageTag, string? modules = null)
    {
        var buildArgs = $"build -f {imageFile} -t {imageTag} .";

        if (!string.IsNullOrWhiteSpace(modules))
        {
            buildArgs += $" --build-arg INSTALL_PS_MODULES=\"{modules}\"";
        }

        return buildArgs;
    }
}
