#!/usr/bin/env pwsh
# oop-host.ps1 — Out-of-process PowerShell host for PoshMcp
# Communicates with the .NET MCP server via stdin/stdout ndjson protocol.
#
# IMPORTANT: stdout is ONLY for ndjson responses. All diagnostic output goes to stderr.
#
# Usage: pwsh -NoProfile -NonInteractive -File oop-host.ps1

$ErrorActionPreference = 'Stop'

# Suppress ANSI escape codes — stdout is for ndjson only.
$env:NO_COLOR = '1'
if ($PSStyle) { $PSStyle.OutputRendering = 'PlainText' }

# Common parameters that should be excluded from discovery schemas.
$script:CommonParameters = @(
    'Verbose', 'Debug', 'ErrorAction', 'WarningAction', 'InformationAction',
    'ErrorVariable', 'WarningVariable', 'InformationVariable',
    'OutVariable', 'OutBuffer', 'PipelineVariable', 'ProgressAction',
    'WhatIf', 'Confirm'
)

function Write-Diag {
    <#
    .SYNOPSIS
        Write diagnostic output to stderr (never stdout).
    #>
    param([string]$Message)
    [Console]::Error.WriteLine("[oop-host] $Message")
}

# --- C# single-host dispatcher (for cancellation propagation, issue #188) ---
# Compiled once per process. Owns a single worker thread that processes
# invokes serially against a shared runspace; tracks active items by request
# id so a 'cancel' message can call BeginStop() on the live pipeline. All
# stdout writes are serialized through SingleStdout.Lock so the dispatcher
# main loop and the worker thread can never interleave.

if (-not ('PoshMcp.SingleHost.SingleDispatcher' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Management.Automation;
using System.Text;
using System.Threading;

namespace PoshMcp.SingleHost
{
    public static class SingleStdout
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

    public sealed class SingleWorkItem
    {
        public string Id;
        public PowerShell Ps;
        public bool Cancelled;
    }

    public sealed class SingleDispatcher : IDisposable
    {
        private readonly BlockingCollection<SingleWorkItem> _queue = new BlockingCollection<SingleWorkItem>();
        private readonly ConcurrentDictionary<string, SingleWorkItem> _active = new ConcurrentDictionary<string, SingleWorkItem>();
        private readonly Thread _worker;

        public SingleDispatcher()
        {
            _worker = new Thread(WorkerLoop);
            _worker.IsBackground = true;
            _worker.Name = "PoshMcpSingle-Worker";
            _worker.Start();
        }

        public void Submit(string id, PowerShell ps)
        {
            var item = new SingleWorkItem { Id = id, Ps = ps };
            _active[id] = item;
            _queue.Add(item);
        }

        public bool Cancel(string requestId)
        {
            if (string.IsNullOrEmpty(requestId)) return false;
            SingleWorkItem item;
            if (!_active.TryGetValue(requestId, out item)) return false;
            try
            {
                item.Cancelled = true;
                item.Ps.BeginStop(null, null);
                return true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[oop-host:dispatcher] BeginStop failed for " + requestId + ": " + ex.Message);
                return false;
            }
        }

        private void WorkerLoop()
        {
            try
            {
                foreach (var w in _queue.GetConsumingEnumerable())
                {
                    try
                    {
                        ProcessOne(w);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine("[oop-host:dispatcher] worker exception: " + ex);
                        try { WriteError(w.Id, ex.Message); } catch { }
                    }
                    finally
                    {
                        try { w.Ps.Dispose(); } catch { }
                        SingleWorkItem _removed;
                        _active.TryRemove(w.Id, out _removed);
                    }
                }
            }
            catch (InvalidOperationException)
            {
                // BlockingCollection completed during shutdown.
            }
        }

        private void ProcessOne(SingleWorkItem w)
        {
            Collection<PSObject> output = null;
            string invokeError = null;
            bool wasStopped = false;

            try
            {
                output = w.Ps.Invoke();
            }
            catch (System.Management.Automation.PipelineStoppedException)
            {
                wasStopped = true;
            }
            catch (Exception ex)
            {
                invokeError = ex.Message;
            }

            if (!wasStopped)
            {
                try
                {
                    var state = w.Ps.InvocationStateInfo;
                    if (state != null && state.State == PSInvocationState.Stopped)
                    {
                        wasStopped = true;
                    }
                }
                catch { /* best-effort */ }
            }

            bool cancelled = wasStopped || w.Cancelled;

            string[] errs = w.Ps.Streams.Error.Select(e => e.ToString()).ToArray();
            string[] warns = w.Ps.Streams.Warning.Select(x => x.Message).ToArray();
            bool hadErrors = w.Ps.HadErrors || errs.Length > 0 || cancelled;

            string outputJson;
            if (invokeError != null && !cancelled)
            {
                WriteError(w.Id, invokeError);
                return;
            }
            if (cancelled || output == null || output.Count == 0 || output[0] == null)
            {
                outputJson = "null";
            }
            else
            {
                var first = output[0];
                outputJson = (first.BaseObject as string) ?? first.ToString() ?? "null";
            }

            var sb = new StringBuilder();
            sb.Append("{\"id\":").Append(SingleStdout.EscapeString(w.Id)).Append(",\"result\":{");
            sb.Append("\"output\":").Append(SingleStdout.EscapeString(outputJson)).Append(',');
            sb.Append("\"hadErrors\":").Append(hadErrors ? "true" : "false").Append(',');
            sb.Append("\"cancelled\":").Append(cancelled ? "true" : "false").Append(',');
            sb.Append("\"errors\":[");
            for (int i = 0; i < errs.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(SingleStdout.EscapeString(errs[i] ?? string.Empty));
            }
            sb.Append("],\"warnings\":[");
            for (int i = 0; i < warns.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(SingleStdout.EscapeString(warns[i] ?? string.Empty));
            }
            sb.Append("]}}");

            SingleStdout.Write(sb.ToString());
        }

        private static void WriteError(string id, string message)
        {
            var sb = new StringBuilder();
            sb.Append("{\"id\":").Append(SingleStdout.EscapeString(id ?? string.Empty));
            sb.Append(",\"error\":{\"code\":-1,\"message\":")
              .Append(SingleStdout.EscapeString(message ?? string.Empty))
              .Append("}}");
            SingleStdout.Write(sb.ToString());
        }

        public void Dispose()
        {
            try { _queue.CompleteAdding(); } catch { }
        }
    }
}
'@
}

$script:Dispatcher = $null
$script:SharedRunspace = $null

function Ensure-SharedRunspace {
    if ($null -eq $script:SharedRunspace) {
        $iss = [System.Management.Automation.Runspaces.InitialSessionState]::CreateDefault2()
        $script:SharedRunspace = [runspacefactory]::CreateRunspace($iss)
        $script:SharedRunspace.Open()
        Write-Diag 'Shared runspace opened.'
    }
}

function Ensure-Dispatcher {
    if ($null -eq $script:Dispatcher) {
        Ensure-SharedRunspace
        $script:Dispatcher = [PoshMcp.SingleHost.SingleDispatcher]::new()
        Write-Diag 'Single-mode dispatcher started.'
    }
}

function Write-NdjsonResponse {
    <#
    .SYNOPSIS
        Write a single ndjson response line to stdout.
    .NOTES
        Uses [PoshMcp.SingleHost.SingleStdout]::Write so the dispatcher main
        loop and the worker thread (which writes invoke responses directly
        from C#) cannot interleave on stdout.
    #>
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
    if (('PoshMcp.SingleHost.SingleStdout' -as [type])) {
        [PoshMcp.SingleHost.SingleStdout]::Write($json)
    }
    else {
        [Console]::Out.WriteLine($json)
        [Console]::Out.Flush()
    }
}

function ConvertTo-SafeJson {
    <#
    .SYNOPSIS
        Serialize an object to compact JSON, tolerating duplicate-property
        errors thrown by ConvertTo-Json on objects whose CLR type shadows a
        base-class member of the same name (e.g. BasicHtmlWebResponseObject's
        'Content' shadows WebResponseObject.Content). Falls back to a
        Select-Object * projection that materializes a flat PSObject (which
        de-duplicates shadowed members), then to a string representation.
    .NOTES
        See issue #203. Without this wrapper, `Invoke-WebRequest |
        ConvertTo-Json -Depth 4` throws
        `An item with the same key has already been added. Key: Content`,
        which surfaces to the C# client as an OOP error.
    #>
    param(
        [Parameter(Mandatory)][AllowNull()]$InputObject,
        [int]$Depth = 4
    )

    if ($null -eq $InputObject) { return 'null' }

    try {
        return $InputObject | ConvertTo-Json -Depth $Depth -Compress -WarningAction SilentlyContinue 3>$null
    }
    catch [System.ArgumentException] {
        # Duplicate property name (shadowed member). Project to a flat
        # PSObject so PowerShell's member resolver collapses duplicates.
        try {
            $projected = $InputObject | Select-Object *
            return $projected | ConvertTo-Json -Depth $Depth -Compress -WarningAction SilentlyContinue 3>$null
        }
        catch {
            # Last resort: stringify and JSON-encode the string.
            return (($InputObject | Out-String).Trim() | ConvertTo-Json -Compress)
        }
    }
}

function Invoke-PingHandler {
    <#
    .SYNOPSIS
        Respond to a health-check ping.
    #>
    param([string]$Id)
    Write-NdjsonResponse -Id $Id -Result @{ status = 'ok' }
}

function Invoke-ShutdownHandler {
    <#
    .SYNOPSIS
        Acknowledge shutdown request and exit.
    #>
    param([string]$Id)
    Write-NdjsonResponse -Id $Id -Result @{ status = 'shutting_down' }
    Write-Diag 'Shutdown requested. Exiting.'
    if ($null -ne $script:Dispatcher) { try { $script:Dispatcher.Dispose() } catch {} }
    if ($null -ne $script:SharedRunspace) {
        try { $script:SharedRunspace.Close() } catch {}
        try { $script:SharedRunspace.Dispose() } catch {}
    }
    exit 0
}

function Invoke-SetupHandler {
    <#
    .SYNOPSIS
        Apply environment customization: module paths, PSGallery trust,
        module installation, module import, and startup scripts.
        Mirrors the ordering from PowerShellEnvironmentSetup.ApplyEnvironmentConfiguration().
    #>
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

    Write-Diag 'Starting environment setup'

    # Step 1: Configure PSModulePath with additional paths
    $modulePaths = @()
    if ($null -ne $Params.modulePaths) {
        $modulePaths = @($Params.modulePaths) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    }
    if ($modulePaths.Count -gt 0) {
        Write-Diag "Configuring PSModulePath with $($modulePaths.Count) additional path(s)"
        $validPaths = [System.Collections.ArrayList]::new()
        foreach ($p in $modulePaths) {
            $expanded = [System.Environment]::ExpandEnvironmentVariables($p)
            if (Test-Path -Path $expanded -PathType Container) {
                $null = $validPaths.Add($expanded)
                Write-Diag "  Added module path: $expanded"
            }
            else {
                $msg = "Module path does not exist: $expanded"
                Write-Diag "  WARNING: $msg"
                $null = $warnings.Add($msg)
            }
        }
        if ($validPaths.Count -gt 0) {
            $separator = [System.IO.Path]::PathSeparator
            $env:PSModulePath = ($validPaths -join $separator) + $separator + $env:PSModulePath
            $null = $configuredPaths.AddRange($validPaths)
        }
    }

    # Step 2: Trust PSGallery if configured — only needed when modules will be installed
    $trustPSGallery = $false
    if ($null -ne $Params.trustPSGallery) {
        $trustPSGallery = [bool]$Params.trustPSGallery
    }
    $hasModulesToInstall = $null -ne $Params.installModules -and @($Params.installModules).Count -gt 0
    if ($trustPSGallery -and $hasModulesToInstall) {
        Write-Diag 'Configuring PSGallery as trusted repository'
        try {
            if (-not (Get-PSRepository -Name PSGallery -ErrorAction SilentlyContinue)) {
                Register-PSRepository -Default -ErrorAction SilentlyContinue
            }
            Set-PSRepository -Name PSGallery -InstallationPolicy Trusted -ErrorAction Stop
            Write-Diag '  PSGallery configured as trusted'
        }
        catch {
            $msg = "Failed to trust PSGallery: $_"
            Write-Diag "  WARNING: $msg"
            $null = $warnings.Add($msg)
        }
    }

    # Step 3: Install modules from PSGallery or other repositories
    $installModules = @()
    if ($null -ne $Params.installModules) {
        $installModules = @($Params.installModules)
    }
    $skipPublisherCheck = $true
    if ($null -ne $Params.skipPublisherCheck) {
        $skipPublisherCheck = [bool]$Params.skipPublisherCheck
    }
    $installTimeoutSeconds = 300
    if ($null -ne $Params.installTimeoutSeconds) {
        $installTimeoutSeconds = [int]$Params.installTimeoutSeconds
    }
    foreach ($mod in $installModules) {
        $modName = $mod.name
        if ([string]::IsNullOrWhiteSpace($modName)) { continue }

        Write-Diag "Installing module: $modName"
        try {
            # Check if already installed (skip unless force)
            $forceInstall = $false
            if ($null -ne $mod.force) {
                $forceInstall = [bool]$mod.force
            }
            if (-not $forceInstall) {
                $existing = Get-Module -ListAvailable -Name $modName -ErrorAction SilentlyContinue
                if ($existing) {
                    Write-Diag "  Module $modName already installed. Skipping."
                    continue
                }
            }

            $installParams = @{
                Name        = $modName
                ErrorAction = 'Stop'
                Force       = $true
            }

            # Repository
            if (-not [string]::IsNullOrWhiteSpace($mod.repository)) {
                $installParams['Repository'] = $mod.repository
            }
            else {
                $installParams['Repository'] = 'PSGallery'
            }

            # Scope
            if (-not [string]::IsNullOrWhiteSpace($mod.scope)) {
                $installParams['Scope'] = $mod.scope
            }
            else {
                $installParams['Scope'] = 'CurrentUser'
            }

            # Version constraints
            if (-not [string]::IsNullOrWhiteSpace($mod.version)) {
                $installParams['RequiredVersion'] = $mod.version
            }
            elseif (-not [string]::IsNullOrWhiteSpace($mod.minimumVersion)) {
                $installParams['MinimumVersion'] = $mod.minimumVersion
                if (-not [string]::IsNullOrWhiteSpace($mod.maximumVersion)) {
                    $installParams['MaximumVersion'] = $mod.maximumVersion
                }
            }

            # SkipPublisherCheck — per-module setting overrides global
            $modSkipPublisher = $skipPublisherCheck
            if ($null -ne $mod.skipPublisherCheck) {
                $modSkipPublisher = [bool]$mod.skipPublisherCheck
            }
            if ($modSkipPublisher) {
                $installParams['SkipPublisherCheck'] = $true
            }

            # AllowPrerelease
            if ($null -ne $mod.allowPrerelease -and [bool]$mod.allowPrerelease) {
                $installParams['AllowPrerelease'] = $true
            }

            Install-Module @installParams -WarningAction SilentlyContinue -WarningVariable installWarnings
            foreach ($w in $installWarnings) { Write-Diag "  Module install warning: $w" }
            $null = $installedModules.Add($modName)
            Write-Diag "  Successfully installed module: $modName"
        }
        catch {
            $msg = "Error installing module $modName`: $_"
            Write-Diag "  ERROR: $msg"
            $null = $errors.Add($msg)
        }
    }

    # Step 4: Import pre-installed modules
    $importModulesList = @()
    if ($null -ne $Params.importModules) {
        $importModulesList = @($Params.importModules) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    }
    $allowClobber = $false
    if ($null -ne $Params.allowClobber) {
        $allowClobber = [bool]$Params.allowClobber
    }
    foreach ($modName in $importModulesList) {
        Write-Diag "Importing module: $modName"
        try {
            $importParams = @{
                Name            = $modName
                ErrorAction     = 'Stop'
                PassThru        = $true
                WarningAction   = 'SilentlyContinue'
                WarningVariable = 'importWarnings'
            }
            if ($allowClobber) {
                $importParams['Force'] = $true
            }
            Import-Module @importParams
            foreach ($w in $importWarnings) { Write-Diag "  Module warning: $w" }
            $null = $importedModules.Add($modName)
            Write-Diag "  Successfully imported module: $modName"
        }
        catch {
            $msg = "Error importing module $modName`: $_"
            Write-Diag "  ERROR: $msg"
            $null = $errors.Add($msg)
        }
    }

    # Step 5: Execute startup script from file
    if (-not [string]::IsNullOrWhiteSpace($Params.startupScriptPath)) {
        $scriptPath = [System.Environment]::ExpandEnvironmentVariables($Params.startupScriptPath)
        Write-Diag "Executing startup script file: $scriptPath"
        if (Test-Path -Path $scriptPath -PathType Leaf) {
            try {
                $scriptContent = Get-Content -Path $scriptPath -Raw
                Invoke-Expression $scriptContent
                $startupScriptExecuted = $true
                Write-Diag '  Successfully executed startup script file'
            }
            catch {
                $msg = "Error executing startup script file: $_"
                Write-Diag "  ERROR: $msg"
                $null = $errors.Add($msg)
            }
        }
        else {
            $msg = "Startup script file not found: $scriptPath"
            Write-Diag "  ERROR: $msg"
            $null = $errors.Add($msg)
        }
    }

    # Step 6: Execute inline startup script
    if (-not [string]::IsNullOrWhiteSpace($Params.startupScript)) {
        Write-Diag "Executing inline startup script ($($Params.startupScript.Length) characters)"
        try {
            Invoke-Expression $Params.startupScript
            $inlineScriptExecuted = $true
            Write-Diag '  Successfully executed inline startup script'
        }
        catch {
            $msg = "Error executing inline startup script: $_"
            Write-Diag "  ERROR: $msg"
            $null = $errors.Add($msg)
        }
    }

    $success = $errors.Count -eq 0
    Write-Diag "Environment setup completed. Success=$success, Installed=$($installedModules.Count), Imported=$($importedModules.Count), Errors=$($errors.Count)"

    Write-NdjsonResponse -Id $Id -Result @{
        success                = $success
        installedModules       = @($installedModules)
        importedModules        = @($importedModules)
        configuredModulePaths  = @($configuredPaths)
        startupScriptExecuted  = $startupScriptExecuted
        inlineScriptExecuted   = $inlineScriptExecuted
        errors                 = @($errors)
        warnings               = @($warnings)
    }
}

function Invoke-DiscoverHandler {
    <#
    .SYNOPSIS
        Import modules, discover commands, and return RemoteToolSchema objects.
    #>
    param(
        [string]$Id,
        [object]$Params
    )

    $commands = [System.Collections.ArrayList]::new()

    # Import requested modules
    $modules = @()
    if ($null -ne $Params.modules) {
        $modules = @($Params.modules)
    }
    foreach ($moduleName in $modules) {
        try {
            Write-Diag "Importing module: $moduleName"
            Import-Module -Name $moduleName -ErrorAction Stop -WarningAction SilentlyContinue -WarningVariable discoverImportWarnings
            foreach ($w in $discoverImportWarnings) { Write-Diag "  Module warning: $w" }
            Write-Diag "Imported module: $moduleName"
        }
        catch {
            Write-Diag "Failed to import module '$moduleName': $_"
            Write-NdjsonResponse -Id $Id -ErrorObj @{
                code    = -1
                message = "Failed to import module '$moduleName': $_"
            }
            return
        }
    }

    # Build Get-Command parameters
    $getCommandParams = @{}

    # Explicit function names
    $functionNames = @()
    if ($null -ne $Params.functionNames) {
        $functionNames = @($Params.functionNames) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    }

    # Include patterns
    $includePatterns = @()
    if ($null -ne $Params.includePatterns) {
        $includePatterns = @($Params.includePatterns) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    }

    # Exclude patterns
    $excludePatterns = @()
    if ($null -ne $Params.excludePatterns) {
        $excludePatterns = @($Params.excludePatterns)
    }

    # Discover commands from explicit names
    foreach ($name in $functionNames) {
        try {
            $cmds = @(Get-Command -Name $name -ErrorAction SilentlyContinue)
            foreach ($cmd in $cmds) {
                $null = $commands.Add($cmd)
            }
        }
        catch {
            Write-Diag "Warning: Could not resolve command '$name': $_"
        }
    }

    # Discover commands from modules using include patterns
    foreach ($moduleName in $modules) {
        foreach ($pattern in $includePatterns) {
            try {
                $cmds = @(Get-Command -Module $moduleName -Name $pattern -ErrorAction SilentlyContinue)
                foreach ($cmd in $cmds) {
                    # Apply exclude patterns
                    $excluded = $false
                    foreach ($ep in $excludePatterns) {
                        if ($cmd.Name -like $ep) {
                            $excluded = $true
                            break
                        }
                    }
                    if (-not $excluded) {
                        $null = $commands.Add($cmd)
                    }
                }
            }
            catch {
                Write-Diag "Warning: Get-Command failed for module '$moduleName' pattern '$pattern': $_"
            }
        }
    }

    # When no modules are specified, discover include patterns globally (same behaviour as in-process)
    if ($modules.Count -eq 0 -and $commands.Count -eq 0 -and $includePatterns.Count -gt 0) {
        foreach ($pattern in $includePatterns) {
            try {
                $cmds = @(Get-Command -Name $pattern -ErrorAction SilentlyContinue)
                foreach ($cmd in $cmds) {
                    $excluded = $false
                    foreach ($ep in $excludePatterns) {
                        if ($cmd.Name -like $ep) { $excluded = $true; break }
                    }
                    if (-not $excluded) { $null = $commands.Add($cmd) }
                }
            }
            catch {
                Write-Diag "Warning: Get-Command failed for global pattern '$pattern': $_"
            }
        }
    }

    # Deduplicate by name (same command may appear from explicit + pattern)
    $seen = @{}
    $uniqueCommands = [System.Collections.ArrayList]::new()
    foreach ($cmd in $commands) {
        if (-not $seen.ContainsKey($cmd.Name)) {
            $seen[$cmd.Name] = $true
            $null = $uniqueCommands.Add($cmd)
        }
    }

    Write-Diag "Discovered $($uniqueCommands.Count) unique command(s)"

    # Build RemoteToolSchema for each command and parameter set
    $schemas = [System.Collections.ArrayList]::new()

    foreach ($cmd in $uniqueCommands) {
        $description = ''
        try {
            $helpInfo = Get-Help -Name $cmd.Name -ErrorAction SilentlyContinue
            if ($null -ne $helpInfo -and $null -ne $helpInfo.Synopsis) {
                $synopsis = "$($helpInfo.Synopsis)".Trim()
                if ($synopsis -and $synopsis -ne $cmd.Name) {
                    $description = $synopsis
                }
            }
        }
        catch {
            # Best effort — description stays empty
        }

        foreach ($paramSet in $cmd.ParameterSets) {
            $parameters = [System.Collections.ArrayList]::new()

            foreach ($param in $paramSet.Parameters) {
                # Skip common parameters
                if ($script:CommonParameters -contains $param.Name) {
                    continue
                }

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

    Write-NdjsonResponse -Id $Id -Result @{ commands = @($schemas) }
}

function Invoke-InvokeHandler {
    <#
    .SYNOPSIS
        Execute a PowerShell command asynchronously via the SingleDispatcher
        worker thread. The dispatcher main loop returns to read stdin
        immediately so a follow-up 'cancel' message can be processed while
        the user pipeline is in flight.
    #>
    param(
        [string]$Id,
        [object]$Params
    )

    $commandName = $Params.command
    if ([string]::IsNullOrWhiteSpace($commandName)) {
        Write-NdjsonResponse -Id $Id -ErrorObj @{
            code    = -1
            message = 'Missing required parameter: command'
        }
        return
    }

    Ensure-Dispatcher

    # Build parameters hashtable for splatting
    $splatParams = @{}
    if ($null -ne $Params.parameters) {
        $Params.parameters.PSObject.Properties | ForEach-Object {
            $splatParams[$_.Name] = $_.Value
        }
    }

    # Handle SwitchParameter: if value is boolean true, include as [switch];
    # if false, omit from splatting entirely.
    try {
        $cmdInfo = Get-Command -Name $commandName -ErrorAction Stop
        $switchParams = @()
        foreach ($ps in $cmdInfo.ParameterSets) {
            foreach ($p in $ps.Parameters) {
                if ($p.ParameterType.FullName -eq 'System.Management.Automation.SwitchParameter') {
                    $switchParams += $p.Name
                }
            }
        }

        foreach ($switchName in ($switchParams | Select-Object -Unique)) {
            if ($splatParams.ContainsKey($switchName)) {
                $val = $splatParams[$switchName]
                if ($val -eq $true -or $val -eq 'true' -or $val -eq 'True') {
                    $splatParams[$switchName] = [switch]$true
                }
                else {
                    $splatParams.Remove($switchName)
                }
            }
        }
    }
    catch {
        Write-Diag "Warning: Could not resolve command info for '$commandName': $_"
    }

    Write-Diag "Invoking: $commandName with $($splatParams.Count) parameter(s)"

    # User script executes inside the shared runspace. $Error is per-runspace
    # (cleared so contamination from a prior invoke cannot leak; see #189).
    # The pipeline pre-serializes the result to JSON so the worker thread can
    # embed it without a second round-trip into PowerShell. ConvertTo-Json is
    # wrapped to tolerate shadowed-property objects (see #203).
    $userScript = {
        param($Name, $Splat)
        $Error.Clear()
        # Wrap the call so terminating errors (e.g. CommandNotFoundException
        # from the call operator) propagate out of [powershell]::Invoke() as
        # exceptions rather than being collected into Streams.Error. The
        # SingleDispatcher uses that exception to emit an error frame, which
        # the .NET client surfaces as InvalidOperationException.
        try { $r = & $Name @Splat } catch { throw }
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

    $ps = [powershell]::Create()
    $ps.Runspace = $script:SharedRunspace
    [void]$ps.AddScript($userScript)
    [void]$ps.AddArgument($commandName)
    [void]$ps.AddArgument($splatParams)

    # Hand off to the worker thread; it writes the response when done.
    $script:Dispatcher.Submit($Id, $ps)
}

function Invoke-CancelHandler {
    <#
    .SYNOPSIS
        Cancel an in-flight invoke. Looks up the request id in the dispatcher's
        active registry and calls BeginStop() on the live pipeline. Always
        responds promptly with a small ack frame regardless of outcome.
    #>
    param(
        [string]$Id,
        [object]$Params
    )

    $requestId = ''
    if ($null -ne $Params -and $null -ne $Params.requestId) {
        $requestId = "$($Params.requestId)"
    }

    $found = $false
    if (-not [string]::IsNullOrEmpty($requestId) -and $null -ne $script:Dispatcher) {
        $found = $script:Dispatcher.Cancel($requestId)
    }
    Write-Diag "cancel: requestId=$requestId found=$found"
    Write-NdjsonResponse -Id $Id -Result @{ cancelled = $found; requestId = $requestId }
}

# --- Main loop ---
# Read ndjson from stdin, dispatch to the appropriate handler.

Write-Diag 'oop-host.ps1 started. Waiting for requests on stdin.'

while ($true) {
    $line = [Console]::ReadLine()

    # stdin closed (EOF) — exit cleanly
    if ($null -eq $line) {
        Write-Diag 'stdin closed (EOF). Exiting.'
        break
    }

    # Skip blank lines
    if ([string]::IsNullOrWhiteSpace($line)) {
        continue
    }

    try {
        $request = $line | ConvertFrom-Json
    }
    catch {
        Write-Diag "Malformed JSON received, skipping: $line"
        continue
    }

    $id = $request.id
    $method = $request.method
    $params = $request.params

    if ([string]::IsNullOrWhiteSpace($id)) {
        Write-Diag "Request missing 'id' field, skipping: $line"
        continue
    }

    if ([string]::IsNullOrWhiteSpace($method)) {
        Write-NdjsonResponse -Id $id -ErrorObj @{
            code    = -1
            message = "Missing required field: method"
        }
        continue
    }

    try {
        switch ($method) {
            'ping' {
                Invoke-PingHandler -Id $id
            }
            'setup' {
                Invoke-SetupHandler -Id $id -Params $params
            }
            'shutdown' {
                Invoke-ShutdownHandler -Id $id
            }
            'discover' {
                Invoke-DiscoverHandler -Id $id -Params $params
            }
            'invoke' {
                Invoke-InvokeHandler -Id $id -Params $params
            }
            'cancel' {
                Invoke-CancelHandler -Id $id -Params $params
            }
            default {
                Write-NdjsonResponse -Id $id -ErrorObj @{
                    code    = -1
                    message = "Unknown method: $method"
                }
            }
        }
    }
    catch {
        Write-Diag "Unhandled error processing method '$method': $_"
        Write-NdjsonResponse -Id $id -ErrorObj @{
            code    = -1
            message = "Internal error: $_"
        }
    }
}
