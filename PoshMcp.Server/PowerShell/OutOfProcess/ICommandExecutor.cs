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
    /// <param name="discoveryModules">
    /// Top-level Modules from PowerShellConfiguration. These are merged with
    /// config.ImportModules so they are available to the startup script, which runs
    /// before DiscoverCommandsAsync() imports them for command discovery.
    /// </param>
    Task SetupAsync(
        EnvironmentConfiguration config,
        string? configFilePath = null,
        TimeSpan? setupRequestTimeout = null,
        IEnumerable<string>? discoveryModules = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute a PowerShell command by name with the given parameters
    /// in the remote process and return the JSON-serialized result.
    /// </summary>
    Task<string> InvokeAsync(
        string commandName,
        IDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Spec 011 FR-263-2 / FR-263-10: payload returned alongside the
    /// <c>commands</c> array on the most recent <c>discover</c> response.
    /// Surfaces per-module probe data and per-pattern match data so the
    /// .NET consumer can build the doctor <c>moduleImports</c> section
    /// without re-running <c>Get-Module -ListAvailable</c> in an
    /// in-process runspace.
    /// </summary>
    /// <remarks>
    /// <para><c>null</c> in any of the following cases:
    /// <list type="bullet">
    ///   <item>No discovery has been performed yet.</item>
    ///   <item>The OOP host predates spec 011 (older hosts omit the
    ///   <c>moduleImports</c> field from the discover response).</item>
    ///   <item>The configuration uses only <c>CommandNames</c>
    ///   (no modules or patterns; the host omits the payload because
    ///   FR-263-6 requires the section to be empty).</item>
    /// </list>
    /// </para>
    /// <para>Default implementation returns <c>null</c> so consumers that
    /// don't care about this payload (and adapter implementations such as
    /// the in-process path that don't run the OOP host at all) work without
    /// changes.</para>
    /// </remarks>
    RemoteModuleImportsPayload? LastModuleImports => null;
}
