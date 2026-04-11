namespace PoshMcp.Server.PowerShell.OutOfProcess;

/// <summary>
/// Controls whether PowerShell commands execute inside the server process
/// or in a separate pwsh subprocess.
/// </summary>
public enum RuntimeMode
{
    /// <summary>
    /// Execute commands using the embedded Microsoft.PowerShell.SDK runtime (default).
    /// </summary>
    InProcess,

    /// <summary>
    /// Execute commands in a persistent external pwsh subprocess.
    /// Required for modules that crash or conflict with the in-process runtime
    /// (e.g., Az.*, Microsoft.Graph.*).
    /// </summary>
    OutOfProcess,

    /// <summary>
    /// The configured runtime mode string was not recognized.
    /// </summary>
    Unsupported
}
