using System;
using System.Diagnostics;

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
                    var output = process.StandardOutput.ReadToEnd();
                    var error = process.StandardError.ReadToEnd();

                    process.WaitForExit();

                    if (process.ExitCode != 0 && !string.IsNullOrEmpty(error))
                    {
                        Console.Error.WriteLine(error);
                    }
                    else if (!string.IsNullOrEmpty(output) && process.ExitCode == 0)
                    {
                        Console.WriteLine(output);
                    }
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
}
