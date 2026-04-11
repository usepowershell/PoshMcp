using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PoshMcp.Server.PowerShell;

namespace PoshMcp.Server.PowerShell.OutOfProcess;

/// <summary>
/// Abstraction for executing PowerShell commands, either in-process or
/// via the out-of-process subprocess host.
/// </summary>
public interface ICommandExecutor : IAsyncDisposable
{
    /// <summary>
    /// Start the executor (e.g., launch the pwsh subprocess).
    /// Must be called before DiscoverCommandsAsync or InvokeAsync.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Import configured modules in the remote pwsh process and return
    /// schemas describing all discovered commands and their parameters.
    /// </summary>
    Task<IReadOnlyList<RemoteToolSchema>> DiscoverCommandsAsync(
        PowerShellConfiguration config,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Send environment configuration (module installs, imports, startup scripts, etc.)
    /// to the executor. Must be called after StartAsync and before DiscoverCommandsAsync.
    /// </summary>
    Task SetupAsync(
        EnvironmentConfiguration config,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute a PowerShell command by name with the given parameters
    /// in the remote process and return the JSON-serialized result.
    /// </summary>
    Task<string> InvokeAsync(
        string commandName,
        IDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default);
}
