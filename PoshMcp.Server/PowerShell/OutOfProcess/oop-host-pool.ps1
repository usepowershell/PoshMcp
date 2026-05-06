#!/usr/bin/env pwsh
# oop-host-pool.ps1 — Runspace-pool variant of the PoshMcp OOP host.
# Speaks the same ndjson wire protocol as oop-host.ps1, but executes
# invoke/discover requests concurrently against a pre-warmed runspace pool
# inside this single subprocess.
#
# Wire protocol: ping | setup | discover | invoke | shutdown — request/response
# correlated by 'id'. stdout is ndjson only; diagnostics go to stderr.
#
# Concurrency model:
#   - The PS main thread reads lines from stdin and dispatches.
#   - ping / shutdown handled inline.
#   - setup runs the QUIESCE PROTOCOL: stop accepting invokes, wait for the
#     C# PoolDispatcher to become idle, rebuild InitialSessionState +
#     reopen the pool, then resume.
#   - invoke is enqueued on a C# PoolDispatcher with N worker threads (= pool
#     size). Each worker calls $ps.Invoke() synchronously against a runspace
#     leased from the runspace pool, so true parallelism comes from the pool.
#     All response writes happen in C# under PoolStdout.Lock so frames never
#     interleave on stdout.
#   - discover runs on the pool inline (single-threaded, rare).
#   - Per-pipeline streams (Error/Warning/Verbose/Information/Debug) are read
#     from $ps.Streams.* — never from runspace-wide $Error.
#   - Stream-pollution defense: a custom PSHost + PSHostUserInterface routes
#     all $Host.UI writes to stderr so Write-Host / Write-Warning / progress
#     never reach stdout.
#
# Why not Task.Run([Action]{...})? Because PowerShell ScriptBlock-to-Action
# conversion runs the body on the *default runspace*. With the host's main
# read loop pinning the default runspace, completion callbacks deadlock.
# A pure C# dispatcher sidesteps that entirely.
#
# Metrics: each invoke response carries a 'metrics' field
#   { queueDepthOnArrival, leaseWaitMs, activeOnComplete, poolSize }
# so Option A can be evaluated against the bench (#194).

$ErrorActionPreference = 'Stop'

$env:NO_COLOR = '1'
if ($PSStyle) { $PSStyle.OutputRendering = 'PlainText' }

$script:CommonParameters = @(
    'Verbose', 'Debug', 'ErrorAction', 'WarningAction', 'InformationAction',
    'ErrorVariable', 'WarningVariable', 'InformationVariable',
    'OutVariable', 'OutBuffer', 'PipelineVariable', 'ProgressAction',
    'WhatIf', 'Confirm'
)

# --- Diagnostics ----------------------------------------------------------

function Write-Diag {
    param([string]$Message)
    [Console]::Error.WriteLine("[oop-host-pool] $Message")
}

# --- Custom PSHost / PSHostUserInterface + C# PoolDispatcher -------------
# Compiled once per process. Routes all $Host.UI output to stderr so cmdlets
# like Write-Host / Write-Warning / progress never pollute the ndjson stdout
# channel. [Console]::Out is process-global and cannot be scoped per runspace
# via InitialSessionState — a custom PSHost is the realistic intercept point.

if (-not ('PoshMcp.PoolHost.NdjsonHostUI' -as [type])) {
    # Add-Type's default reference set in pwsh already covers BCL + SMA.
    # Passing -ReferencedAssemblies replaces (not extends) that set and breaks
    # Roslyn's CoreLib resolution under .NET 10, so we omit it here.
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Host;
using System.Security;
using System.Text;
using System.Threading;

namespace PoshMcp.PoolHost
{
    // ---- Custom PSHost ----

    public sealed class NdjsonHostRawUI : PSHostRawUserInterface
    {
        public override ConsoleColor BackgroundColor { get; set; } = ConsoleColor.Black;
        public override ConsoleColor ForegroundColor { get; set; } = ConsoleColor.Gray;
        public override Coordinates CursorPosition { get; set; } = new Coordinates(0, 0);
        public override int CursorSize { get; set; } = 1;
        public override Coordinates WindowPosition { get; set; } = new Coordinates(0, 0);
        public override Size WindowSize { get; set; } = new Size(120, 50);
        public override Size BufferSize { get; set; } = new Size(120, 50);
        public override Size MaxPhysicalWindowSize { get { return new Size(120, 50); } }
        public override Size MaxWindowSize { get { return new Size(120, 50); } }
        public override string WindowTitle { get; set; } = "poshmcp-oop-pool";
        public override bool KeyAvailable { get { return false; } }

        public override void FlushInputBuffer() { }
        public override KeyInfo ReadKey(ReadKeyOptions options) { throw new NotSupportedException(); }
        public override BufferCell[,] GetBufferContents(Rectangle rectangle) { return new BufferCell[0, 0]; }
        public override void SetBufferContents(Coordinates origin, BufferCell[,] contents) { }
        public override void SetBufferContents(Rectangle rectangle, BufferCell fill) { }
        public override void ScrollBufferContents(Rectangle source, Coordinates destination, Rectangle clip, BufferCell fill) { }
    }

    public sealed class NdjsonHostUI : PSHostUserInterface
    {
        private readonly NdjsonHostRawUI _raw = new NdjsonHostRawUI();
        public override PSHostRawUserInterface RawUI { get { return _raw; } }

        private static void Emit(string prefix, string message)
        {
            Console.Error.WriteLine("[oop-host-pool:" + prefix + "] " + (message ?? string.Empty));
        }

        public override void Write(string value) { Emit("write", value); }
        public override void Write(ConsoleColor foregroundColor, ConsoleColor backgroundColor, string value) { Emit("write", value); }
        public override void WriteLine() { Emit("write", string.Empty); }
        public override void WriteLine(string value) { Emit("write", value); }
        public override void WriteLine(ConsoleColor foregroundColor, ConsoleColor backgroundColor, string value) { Emit("write", value); }
        public override void WriteDebugLine(string message) { Emit("debug", message); }
        public override void WriteVerboseLine(string message) { Emit("verbose", message); }
        public override void WriteWarningLine(string message) { Emit("warning", message); }
        public override void WriteErrorLine(string value) { Emit("error", value); }
        public override void WriteProgress(long sourceId, ProgressRecord record) { /* swallow */ }

        public override string ReadLine() { throw new NotSupportedException("Interactive input not supported in OOP pool host."); }
        public override SecureString ReadLineAsSecureString() { throw new NotSupportedException("Interactive input not supported in OOP pool host."); }
        public override Dictionary<string, PSObject> Prompt(string caption, string message, Collection<FieldDescription> descriptions) { throw new NotSupportedException("Interactive prompts not supported in OOP pool host."); }
        public override int PromptForChoice(string caption, string message, Collection<ChoiceDescription> choices, int defaultChoice) { return defaultChoice; }
        public override PSCredential PromptForCredential(string caption, string message, string userName, string targetName) { throw new NotSupportedException("Credential prompts not supported in OOP pool host."); }
        public override PSCredential PromptForCredential(string caption, string message, string userName, string targetName, PSCredentialTypes allowedCredentialTypes, PSCredentialUIOptions options) { throw new NotSupportedException("Credential prompts not supported in OOP pool host."); }
    }

    public sealed class NdjsonHost : PSHost
    {
        private readonly Guid _id = Guid.NewGuid();
        private readonly NdjsonHostUI _ui = new NdjsonHostUI();
        public override CultureInfo CurrentCulture { get { return CultureInfo.CurrentCulture; } }
        public override CultureInfo CurrentUICulture { get { return CultureInfo.CurrentUICulture; } }
        public override Guid InstanceId { get { return _id; } }
        public override string Name { get { return "PoshMcpPoolHost"; } }
        public override PSHostUserInterface UI { get { return _ui; } }
        public override Version Version { get { return new Version(1, 0, 0, 0); } }
        public override void EnterNestedPrompt() { throw new NotSupportedException(); }
        public override void ExitNestedPrompt() { throw new NotSupportedException(); }
        public override void NotifyBeginApplication() { }
        public override void NotifyEndApplication() { }
        public override void SetShouldExit(int exitCode) { }
    }

    // ---- Synchronized stdout writer (shared with PS code) ----

    public static class PoolStdout
    {
        public static readonly object Lock = new object();

        public static void Write(string json)
        {
            lock (Lock)
            {
                Console.Out.WriteLine(json);
                Console.Out.Flush();
            }
        }

        public static string EscapeString(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"':  sb.Append("\\\""); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }

    // ---- Invoke dispatcher ----
    //
    // Owns N worker threads (= runspace pool size). Each worker takes a
    // PoolWorkItem and calls ps.Invoke() synchronously. ps.RunspacePool is
    // pre-set, so .Invoke() leases a runspace from the pool. Worker count
    // matches pool size, so nothing additional gates concurrency.

    public sealed class PoolWorkItem
    {
        public string Id;
        public PowerShell Ps;
        public int QueueDepthOnArrival;
        public Stopwatch QueueSw;
        public string CommandName;
    }

    public sealed class PoolDispatcher : IDisposable
    {
        private readonly int _capacity;
        private readonly BlockingCollection<PoolWorkItem> _queue = new BlockingCollection<PoolWorkItem>();
        private readonly Thread[] _workers;
        private readonly ManualResetEventSlim _idle = new ManualResetEventSlim(true);
        private readonly object _idleLock = new object();
        private int _activeCount;
        private int _pendingCount; // queued + active, used for IsIdle

        public int ActiveCount { get { return Volatile.Read(ref _activeCount); } }
        public int Capacity { get { return _capacity; } }
        public int PendingCount { get { return Volatile.Read(ref _pendingCount); } }
        public bool IsIdle { get { return Volatile.Read(ref _pendingCount) == 0; } }

        public PoolDispatcher(int capacity)
        {
            if (capacity < 1) capacity = 1;
            _capacity = capacity;
            _workers = new Thread[capacity];
            for (int i = 0; i < capacity; i++)
            {
                var t = new Thread(WorkerLoop);
                t.IsBackground = true;
                t.Name = "PoshMcpPool-Worker-" + i;
                t.Start();
                _workers[i] = t;
            }
        }

        public int Submit(string id, PowerShell ps, string commandName)
        {
            // Snapshot depth (queued + active) BEFORE adding ourselves.
            int depth = _queue.Count + Volatile.Read(ref _activeCount);

            var item = new PoolWorkItem
            {
                Id = id,
                Ps = ps,
                QueueDepthOnArrival = depth,
                QueueSw = Stopwatch.StartNew(),
                CommandName = commandName ?? string.Empty
            };

            lock (_idleLock)
            {
                Interlocked.Increment(ref _pendingCount);
                _idle.Reset();
            }

            _queue.Add(item);
            return depth;
        }

        /// <summary>Wait until the dispatcher is idle (queue empty + no active work).</summary>
        public bool WaitIdle(int timeoutMs)
        {
            return _idle.Wait(timeoutMs);
        }

        private void WorkerLoop()
        {
            try
            {
                foreach (var w in _queue.GetConsumingEnumerable())
                {
                    w.QueueSw.Stop();
                    int leaseWaitMs = (int)w.QueueSw.ElapsedMilliseconds;
                    Interlocked.Increment(ref _activeCount);

                    try
                    {
                        ProcessOne(w, leaseWaitMs);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine("[oop-host-pool:dispatcher] worker exception: " + ex);
                        try { WriteError(w.Id, ex.Message); } catch { }
                    }
                    finally
                    {
                        try { w.Ps.Dispose(); } catch { }
                        Interlocked.Decrement(ref _activeCount);
                        lock (_idleLock)
                        {
                            int remaining = Interlocked.Decrement(ref _pendingCount);
                            if (remaining <= 0)
                            {
                                _idle.Set();
                            }
                        }
                    }
                }
            }
            catch (InvalidOperationException)
            {
                // BlockingCollection completed during shutdown.
            }
        }

        private void ProcessOne(PoolWorkItem w, int leaseWaitMs)
        {
            Collection<PSObject> output = null;
            string invokeError = null;

            try
            {
                output = w.Ps.Invoke();
            }
            catch (Exception ex)
            {
                invokeError = ex.Message;
            }

            string[] errs = w.Ps.Streams.Error.Select(e => e.ToString()).ToArray();
            string[] warns = w.Ps.Streams.Warning.Select(x => x.Message).ToArray();
            bool hadErrors = w.Ps.HadErrors || errs.Length > 0;

            // The user script returns a single string from ConvertTo-Json.
            // Embed it as a JSON-string value (escaped).
            string outputJson;
            if (invokeError != null)
            {
                WriteError(w.Id, invokeError);
                return;
            }
            if (output == null || output.Count == 0 || output[0] == null)
            {
                outputJson = "null";
            }
            else
            {
                var first = output[0];
                outputJson = (first.BaseObject as string) ?? first.ToString() ?? "null";
            }

            int activeOnComplete = Volatile.Read(ref _activeCount);

            var sb = new StringBuilder();
            sb.Append("{\"id\":").Append(PoolStdout.EscapeString(w.Id)).Append(",\"result\":{");
            sb.Append("\"output\":").Append(PoolStdout.EscapeString(outputJson)).Append(',');
            sb.Append("\"hadErrors\":").Append(hadErrors ? "true" : "false").Append(',');
            sb.Append("\"errors\":[");
            for (int i = 0; i < errs.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(PoolStdout.EscapeString(errs[i] ?? string.Empty));
            }
            sb.Append("],\"warnings\":[");
            for (int i = 0; i < warns.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(PoolStdout.EscapeString(warns[i] ?? string.Empty));
            }
            sb.Append("],\"metrics\":{");
            sb.Append("\"queueDepthOnArrival\":").Append(w.QueueDepthOnArrival.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"leaseWaitMs\":").Append(leaseWaitMs.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"activeOnComplete\":").Append(activeOnComplete.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"poolSize\":").Append(_capacity.ToString(CultureInfo.InvariantCulture));
            sb.Append("}}}");

            PoolStdout.Write(sb.ToString());
        }

        private static void WriteError(string id, string message)
        {
            var sb = new StringBuilder();
            sb.Append("{\"id\":").Append(PoolStdout.EscapeString(id ?? string.Empty));
            sb.Append(",\"error\":{\"code\":-1,\"message\":")
              .Append(PoolStdout.EscapeString(message ?? string.Empty))
              .Append("}}");
            PoolStdout.Write(sb.ToString());
        }

        public void Dispose()
        {
            try { _queue.CompleteAdding(); } catch { }
        }
    }
}
'@
}

# --- Pool state ----------------------------------------------------------

$script:PoolHost           = [PoshMcp.PoolHost.NdjsonHost]::new()
$script:Pool               = $null
$script:PoolSize           = [Math]::Min([Environment]::ProcessorCount, 8)
if ($script:PoolSize -lt 1) { $script:PoolSize = 1 }
$script:Dispatcher         = $null
$script:DrainEvent         = [System.Threading.ManualResetEventSlim]::new($true) # set => accepting; reset => quiescing
$script:SetupParamsCache   = $null
$script:DiscoveryModuleSet = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)

function Write-NdjsonResponse {
    param(
        [Parameter(Mandatory)][string]$Id,
        [object]$Result,
        [object]$ErrorObj
    )

    $response = [ordered]@{ id = $Id }
    if ($null -ne $ErrorObj) {
        $response['error'] = $ErrorObj
    }
    else {
        $response['result'] = $Result
    }

    $json = $response | ConvertTo-Json -Depth 10 -Compress
    [PoshMcp.PoolHost.PoolStdout]::Write($json)
}

function New-PoolInitialSessionState {
    param([object]$Params)

    $iss = [System.Management.Automation.Runspaces.InitialSessionState]::CreateDefault2()
    $iss.ThreadOptions = [System.Management.Automation.Runspaces.PSThreadOptions]::ReuseThread

    if ($null -ne $Params) {
        $imports = @()
        if ($null -ne $Params.importModules) {
            $imports = @($Params.importModules) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        }
        foreach ($m in $imports) {
            try { $iss.ImportPSModule(@($m)) } catch { Write-Diag "ISS import warning for module '$m': $_" }
        }
    }

    return $iss
}

function Open-Pool {
    param([object]$SetupParams)

    $iss = New-PoolInitialSessionState -Params $SetupParams
    $pool = [runspacefactory]::CreateRunspacePool(1, $script:PoolSize, $iss, $script:PoolHost)
    $pool.ApartmentState = [System.Threading.ApartmentState]::MTA
    $pool.Open()
    return $pool
}

function Close-Pool {
    if ($null -ne $script:Pool) {
        try { $script:Pool.Close() } catch { Write-Diag "Pool Close error: $_" }
        try { $script:Pool.Dispose() } catch { Write-Diag "Pool Dispose error: $_" }
        $script:Pool = $null
    }
}

function Ensure-Pool {
    if ($null -eq $script:Pool) {
        Write-Diag "Opening runspace pool (size=$($script:PoolSize))"
        $script:Pool = Open-Pool -SetupParams $script:SetupParamsCache
    }
    if ($null -eq $script:Dispatcher -or $script:Dispatcher.Capacity -ne $script:PoolSize) {
        if ($null -ne $script:Dispatcher) { try { $script:Dispatcher.Dispose() } catch {} }
        $script:Dispatcher = [PoshMcp.PoolHost.PoolDispatcher]::new($script:PoolSize)
        Write-Diag "Dispatcher started (workers=$($script:PoolSize))"
    }
}

# --- Quiesce protocol ----------------------------------------------------

function Begin-Drain {
    Write-Diag "Quiesce: begin drain (pending=$($script:Dispatcher.PendingCount))"
    $script:DrainEvent.Reset()

    if ($null -eq $script:Dispatcher) { return $true }
    $idle = $script:Dispatcher.WaitIdle(60000)
    if (-not $idle) {
        Write-Diag "Quiesce: drain TIMEOUT (pending=$($script:Dispatcher.PendingCount))"
        return $false
    }
    return $true
}

function End-Drain {
    $script:DrainEvent.Set()
    Write-Diag "Quiesce: resume"
}

# --- Handlers: ping / shutdown -------------------------------------------

function Invoke-PingHandler {
    param([string]$Id)
    Write-NdjsonResponse -Id $Id -Result @{ status = 'ok'; mode = 'pool'; poolSize = $script:PoolSize }
}

function Invoke-ShutdownHandler {
    param([string]$Id)
    Write-NdjsonResponse -Id $Id -Result @{ status = 'shutting_down' }
    Write-Diag 'Shutdown requested. Closing pool and exiting.'
    if ($null -ne $script:Dispatcher) { try { $script:Dispatcher.Dispose() } catch {} }
    Close-Pool
    exit 0
}

# --- Setup handler (drains, mutates ISS, reopens pool) -------------------

function Invoke-SetupHandler {
    param(
        [string]$Id,
        [object]$Params
    )

    $errors = [System.Collections.ArrayList]::new()
    $warnings = [System.Collections.ArrayList]::new()
    $installedModules = [System.Collections.ArrayList]::new()
    $importedModules = [System.Collections.ArrayList]::new()
    $configuredPaths = [System.Collections.ArrayList]::new()
    $startupScriptExecuted = $false
    $inlineScriptExecuted = $false

    Write-Diag 'setup: starting (with quiesce)'

    if ($null -ne $Params -and $Params.PSObject.Properties.Match('runspacePoolSize').Count -gt 0) {
        $req = [int]$Params.runspacePoolSize
        if ($req -gt 0) {
            $script:PoolSize = $req
            Write-Diag "setup: runspace pool size set to $($script:PoolSize)"
        }
    }

    # 1. Drain in-flight invokes BEFORE mutating module state.
    $drained = Begin-Drain
    if (-not $drained) {
        $null = $warnings.Add('Drain timed out; proceeding with setup may cancel in-flight work.')
    }

    # 2. Tear down the existing pool so the rebuilt ISS takes effect.
    Close-Pool

    # 3. Apply setup operations to the host process.
    $modulePaths = @()
    if ($null -ne $Params.modulePaths) {
        $modulePaths = @($Params.modulePaths) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    }
    foreach ($p in $modulePaths) {
        $expanded = [System.Environment]::ExpandEnvironmentVariables($p)
        if (Test-Path -Path $expanded -PathType Container) {
            $separator = [System.IO.Path]::PathSeparator
            $env:PSModulePath = "$expanded$separator$($env:PSModulePath)"
            $null = $configuredPaths.Add($expanded)
            Write-Diag "  Added module path: $expanded"
        }
        else {
            $msg = "Module path does not exist: $expanded"
            Write-Diag "  WARNING: $msg"
            $null = $warnings.Add($msg)
        }
    }

    $trustPSGallery = $false
    if ($null -ne $Params.trustPSGallery) { $trustPSGallery = [bool]$Params.trustPSGallery }
    $hasModulesToInstall = $null -ne $Params.installModules -and @($Params.installModules).Count -gt 0
    if ($trustPSGallery -and $hasModulesToInstall) {
        try {
            if (-not (Get-PSRepository -Name PSGallery -ErrorAction SilentlyContinue)) {
                Register-PSRepository -Default -ErrorAction SilentlyContinue
            }
            Set-PSRepository -Name PSGallery -InstallationPolicy Trusted -ErrorAction Stop
        }
        catch {
            $msg = "Failed to trust PSGallery: $_"
            $null = $warnings.Add($msg)
        }
    }

    $installModules = @()
    if ($null -ne $Params.installModules) { $installModules = @($Params.installModules) }
    foreach ($mod in $installModules) {
        $modName = $mod.name
        if ([string]::IsNullOrWhiteSpace($modName)) { continue }
        Write-Diag "Installing module: $modName"
        try {
            $forceInstall = $false
            if ($null -ne $mod.force) { $forceInstall = [bool]$mod.force }
            if (-not $forceInstall) {
                $existing = Get-Module -ListAvailable -Name $modName -ErrorAction SilentlyContinue
                if ($existing) { Write-Diag "  Already installed: $modName"; continue }
            }
            $installParams = @{
                Name        = $modName
                ErrorAction = 'Stop'
                Force       = $true
                Repository  = $(if (-not [string]::IsNullOrWhiteSpace($mod.repository)) { $mod.repository } else { 'PSGallery' })
                Scope       = $(if (-not [string]::IsNullOrWhiteSpace($mod.scope)) { $mod.scope } else { 'CurrentUser' })
            }
            if (-not [string]::IsNullOrWhiteSpace($mod.version))         { $installParams['RequiredVersion'] = $mod.version }
            elseif (-not [string]::IsNullOrWhiteSpace($mod.minimumVersion)) {
                $installParams['MinimumVersion'] = $mod.minimumVersion
                if (-not [string]::IsNullOrWhiteSpace($mod.maximumVersion)) { $installParams['MaximumVersion'] = $mod.maximumVersion }
            }
            $modSkipPublisher = $true
            if ($null -ne $mod.skipPublisherCheck) { $modSkipPublisher = [bool]$mod.skipPublisherCheck }
            if ($modSkipPublisher) { $installParams['SkipPublisherCheck'] = $true }
            if ($null -ne $mod.allowPrerelease -and [bool]$mod.allowPrerelease) { $installParams['AllowPrerelease'] = $true }
            Install-Module @installParams -WarningAction SilentlyContinue
            $null = $installedModules.Add($modName)
        }
        catch {
            $msg = "Error installing module $modName`: $_"
            Write-Diag "  ERROR: $msg"
            $null = $errors.Add($msg)
        }
    }

    $importModulesList = @()
    if ($null -ne $Params.importModules) {
        $importModulesList = @($Params.importModules) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    }
    foreach ($m in $importModulesList) { $null = $importedModules.Add($m) }

    if (-not [string]::IsNullOrWhiteSpace($Params.startupScriptPath)) {
        $scriptPath = [System.Environment]::ExpandEnvironmentVariables($Params.startupScriptPath)
        if (Test-Path -Path $scriptPath -PathType Leaf) {
            try {
                $scriptContent = Get-Content -Path $scriptPath -Raw
                Invoke-Expression $scriptContent
                $startupScriptExecuted = $true
            }
            catch {
                $msg = "Error executing startup script file: $_"
                $null = $errors.Add($msg)
            }
        }
        else {
            $null = $errors.Add("Startup script file not found: $scriptPath")
        }
    }
    if (-not [string]::IsNullOrWhiteSpace($Params.startupScript)) {
        try {
            Invoke-Expression $Params.startupScript
            $inlineScriptExecuted = $true
        }
        catch {
            $msg = "Error executing inline startup script: $_"
            $null = $errors.Add($msg)
        }
    }

    # 4. Cache setup params (drives ISS rebuild for future re-opens) and reopen pool.
    $script:SetupParamsCache = $Params
    Ensure-Pool

    # 5. Resume accepting invokes.
    End-Drain

    $success = $errors.Count -eq 0
    Write-Diag "setup: completed Success=$success Imported=$($importedModules.Count) Errors=$($errors.Count)"

    Write-NdjsonResponse -Id $Id -Result @{
        success                = $success
        installedModules       = @($installedModules)
        importedModules        = @($importedModules)
        configuredModulePaths  = @($configuredPaths)
        startupScriptExecuted  = $startupScriptExecuted
        inlineScriptExecuted   = $inlineScriptExecuted
        errors                 = @($errors)
        warnings               = @($warnings)
        poolSize               = $script:PoolSize
    }
}

# --- Discover handler (single-runspace; runs after drain) ----------------

function Invoke-DiscoverHandler {
    param(
        [string]$Id,
        [object]$Params
    )

    Ensure-Pool

    $modules = @()
    if ($null -ne $Params.modules) { $modules = @($Params.modules) }
    foreach ($m in $modules) {
        if (-not [string]::IsNullOrWhiteSpace($m)) { [void]$script:DiscoveryModuleSet.Add($m) }
    }

    $functionNames = @()
    if ($null -ne $Params.functionNames) {
        $functionNames = @($Params.functionNames) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    }
    $includePatterns = @()
    if ($null -ne $Params.includePatterns) {
        $includePatterns = @($Params.includePatterns) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    }
    $excludePatterns = @()
    if ($null -ne $Params.excludePatterns) { $excludePatterns = @($Params.excludePatterns) }

    $script = {
        param($modules, $functionNames, $includePatterns, $excludePatterns, $commonParameters)

        $commands = [System.Collections.ArrayList]::new()
        foreach ($moduleName in $modules) {
            try { Import-Module -Name $moduleName -ErrorAction Stop -WarningAction SilentlyContinue }
            catch { throw "Failed to import module '$moduleName': $_" }
        }

        foreach ($name in $functionNames) {
            try {
                $cmds = @(Get-Command -Name $name -ErrorAction SilentlyContinue)
                foreach ($cmd in $cmds) { $null = $commands.Add($cmd) }
            } catch { }
        }

        foreach ($moduleName in $modules) {
            foreach ($pattern in $includePatterns) {
                try {
                    $cmds = @(Get-Command -Module $moduleName -Name $pattern -ErrorAction SilentlyContinue)
                    foreach ($cmd in $cmds) {
                        $excluded = $false
                        foreach ($ep in $excludePatterns) { if ($cmd.Name -like $ep) { $excluded = $true; break } }
                        if (-not $excluded) { $null = $commands.Add($cmd) }
                    }
                } catch { }
            }
        }

        if ($modules.Count -eq 0 -and $commands.Count -eq 0 -and $includePatterns.Count -gt 0) {
            foreach ($pattern in $includePatterns) {
                try {
                    $cmds = @(Get-Command -Name $pattern -ErrorAction SilentlyContinue)
                    foreach ($cmd in $cmds) {
                        $excluded = $false
                        foreach ($ep in $excludePatterns) { if ($cmd.Name -like $ep) { $excluded = $true; break } }
                        if (-not $excluded) { $null = $commands.Add($cmd) }
                    }
                } catch { }
            }
        }

        $seen = @{}
        $unique = [System.Collections.ArrayList]::new()
        foreach ($cmd in $commands) {
            if (-not $seen.ContainsKey($cmd.Name)) { $seen[$cmd.Name] = $true; $null = $unique.Add($cmd) }
        }

        $schemas = [System.Collections.ArrayList]::new()
        foreach ($cmd in $unique) {
            $description = ''
            try {
                $helpInfo = Get-Help -Name $cmd.Name -ErrorAction SilentlyContinue
                if ($null -ne $helpInfo -and $null -ne $helpInfo.Synopsis) {
                    $synopsis = "$($helpInfo.Synopsis)".Trim()
                    if ($synopsis -and $synopsis -ne $cmd.Name) { $description = $synopsis }
                }
            } catch { }
            foreach ($paramSet in $cmd.ParameterSets) {
                $parameters = [System.Collections.ArrayList]::new()
                foreach ($param in $paramSet.Parameters) {
                    if ($commonParameters -contains $param.Name) { continue }
                    $null = $parameters.Add([ordered]@{
                        Name        = $param.Name
                        TypeName    = $param.ParameterType.FullName
                        IsMandatory = [bool]$param.IsMandatory
                        Position    = $param.Position
                    })
                }
                $null = $schemas.Add([ordered]@{
                    Name             = $cmd.Name
                    Description      = $description
                    ParameterSetName = $paramSet.Name
                    Parameters       = @($parameters)
                })
            }
        }
        return ,@($schemas)
    }

    $ps = [powershell]::Create()
    $ps.RunspacePool = $script:Pool
    [void]$ps.AddScript($script)
    [void]$ps.AddArgument($modules)
    [void]$ps.AddArgument($functionNames)
    [void]$ps.AddArgument($includePatterns)
    [void]$ps.AddArgument($excludePatterns)
    [void]$ps.AddArgument($script:CommonParameters)

    try {
        $output = $ps.Invoke()
        if ($ps.HadErrors) {
            $msgs = @($ps.Streams.Error | ForEach-Object { $_.ToString() })
            Write-NdjsonResponse -Id $Id -ErrorObj @{ code = -1; message = ($msgs -join '; ') }
            return
        }
        $schemas = @()
        if ($null -ne $output -and $output.Count -gt 0) { $schemas = @($output[0]) }
        Write-NdjsonResponse -Id $Id -Result @{ commands = $schemas }
    }
    catch {
        Write-NdjsonResponse -Id $Id -ErrorObj @{ code = -1; message = "$_" }
    }
    finally {
        $ps.Dispose()
    }
}

# --- Invoke handler (concurrent on the runspace pool) --------------------

function Resolve-SwitchParameters {
    param([string]$CommandName, [hashtable]$SplatParams)

    try {
        $cmdInfo = Get-Command -Name $CommandName -ErrorAction Stop
        $switchParams = @()
        foreach ($ps in $cmdInfo.ParameterSets) {
            foreach ($p in $ps.Parameters) {
                if ($p.ParameterType.FullName -eq 'System.Management.Automation.SwitchParameter') {
                    $switchParams += $p.Name
                }
            }
        }
        foreach ($switchName in ($switchParams | Select-Object -Unique)) {
            if ($SplatParams.ContainsKey($switchName)) {
                $val = $SplatParams[$switchName]
                if ($val -eq $true -or $val -eq 'true' -or $val -eq 'True') {
                    $SplatParams[$switchName] = [switch]$true
                }
                else {
                    $SplatParams.Remove($switchName)
                }
            }
        }
    }
    catch {
        Write-Diag "Warning: Could not resolve command info for '$CommandName': $_"
    }
}

function Invoke-InvokeHandler {
    param(
        [string]$Id,
        [object]$Params
    )

    $commandName = $Params.command
    if ([string]::IsNullOrWhiteSpace($commandName)) {
        Write-NdjsonResponse -Id $Id -ErrorObj @{ code = -1; message = 'Missing required parameter: command' }
        return
    }

    Ensure-Pool

    $splatParams = @{}
    if ($null -ne $Params.parameters) {
        $Params.parameters.PSObject.Properties | ForEach-Object { $splatParams[$_.Name] = $_.Value }
    }
    Resolve-SwitchParameters -CommandName $commandName -SplatParams $splatParams

    # Wait for "accepting invokes". A drained-out setup releases this.
    $script:DrainEvent.Wait()

    $ps = [powershell]::Create()
    $ps.RunspacePool = $script:Pool

    # User script executes inside the pool runspace. $Error is per-runspace
    # (cleared so contamination from a prior invoke on the same runspace
    # cannot leak). The pipeline pre-serializes the result to JSON so the
    # C# dispatcher can embed it without a second round-trip into PowerShell.
    # The ConvertTo-Json call is wrapped in try/catch with a Select-Object *
    # fallback to tolerate objects whose CLR type shadows a base-class
    # member of the same name (e.g. BasicHtmlWebResponseObject's 'Content'
    # shadows WebResponseObject.Content). See issue #203.
    $userScript = {
        param($Name, $Splat)
        $Error.Clear()
        $r = & $Name @Splat
        if ($null -eq $r) { return 'null' }
        try {
            return ($r | ConvertTo-Json -Depth 4 -Compress -WarningAction SilentlyContinue)
        }
        catch [System.ArgumentException] {
            try {
                return ($r | Select-Object * | ConvertTo-Json -Depth 4 -Compress -WarningAction SilentlyContinue)
            }
            catch {
                return (($r | Out-String).Trim() | ConvertTo-Json -Compress)
            }
        }
    }
    [void]$ps.AddScript($userScript)
    [void]$ps.AddArgument($commandName)
    [void]$ps.AddArgument($splatParams)

    # Hand off to the C# dispatcher. Worker threads write the response.
    [void]$script:Dispatcher.Submit($Id, $ps, $commandName)
}

# --- Main loop -----------------------------------------------------------

Write-Diag "oop-host-pool.ps1 started. PoolSize=$($script:PoolSize). Waiting for requests on stdin."

while ($true) {
    $line = [Console]::ReadLine()
    if ($null -eq $line) {
        Write-Diag 'stdin closed (EOF). Exiting.'
        break
    }
    if ([string]::IsNullOrWhiteSpace($line)) { continue }

    try { $request = $line | ConvertFrom-Json }
    catch { Write-Diag "Malformed JSON received, skipping: $line"; continue }

    $id = $request.id
    $method = $request.method
    $params = $request.params

    if ([string]::IsNullOrWhiteSpace($id)) {
        Write-Diag "Request missing 'id' field, skipping: $line"
        continue
    }
    if ([string]::IsNullOrWhiteSpace($method)) {
        Write-NdjsonResponse -Id $id -ErrorObj @{ code = -1; message = "Missing required field: method" }
        continue
    }

    try {
        switch ($method) {
            'ping'     { Invoke-PingHandler     -Id $id }
            'setup'    { Invoke-SetupHandler    -Id $id -Params $params }
            'shutdown' { Invoke-ShutdownHandler -Id $id }
            'discover' { Invoke-DiscoverHandler -Id $id -Params $params }
            'invoke'   { Invoke-InvokeHandler   -Id $id -Params $params }
            default {
                Write-NdjsonResponse -Id $id -ErrorObj @{ code = -1; message = "Unknown method: $method" }
            }
        }
    }
    catch {
        Write-Diag "Unhandled error processing method '$method': $_"
        Write-NdjsonResponse -Id $id -ErrorObj @{ code = -1; message = "Internal error: $_" }
    }
}

if ($null -ne $script:Dispatcher) { try { $script:Dispatcher.Dispose() } catch {} }
Close-Pool
