#!/usr/bin/env pwsh
# oop-host.ps1 — Out-of-process PowerShell host for PoshMcp
# Communicates with the .NET MCP server via stdin/stdout ndjson protocol.
#
# IMPORTANT: stdout is ONLY for ndjson responses. All diagnostic output goes to stderr.
#
# Usage: pwsh -NoProfile -NonInteractive -File oop-host.ps1

$ErrorActionPreference = 'Stop'

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

function Write-NdjsonResponse {
    <#
    .SYNOPSIS
        Write a single ndjson response line to stdout.
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
    [Console]::Out.WriteLine($json)
    [Console]::Out.Flush()
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
    exit 0
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
            Import-Module -Name $moduleName -ErrorAction Stop
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
        Execute a PowerShell command and return the result.
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

    # Build parameters hashtable for splatting
    $splatParams = @{}
    if ($null -ne $Params.parameters) {
        # Convert the PSCustomObject from ConvertFrom-Json into a hashtable
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
                    # Remove false switch parameters so they aren't passed
                    $splatParams.Remove($switchName)
                }
            }
        }
    }
    catch {
        Write-Diag "Warning: Could not resolve command info for '$commandName': $_"
        # Proceed anyway — splatting may still work
    }

    Write-Diag "Invoking: $commandName with $($splatParams.Count) parameter(s)"

    try {
        $result = & $commandName @splatParams
        $hadErrors = $false

        # Check if there were non-terminating errors
        if ($Error.Count -gt 0) {
            $hadErrors = $true
        }

        $jsonOutput = $result | ConvertTo-Json -Depth 4 -Compress
        if ($null -eq $jsonOutput) {
            $jsonOutput = 'null'
        }

        Write-NdjsonResponse -Id $Id -Result @{
            output    = $jsonOutput
            hadErrors = $hadErrors
        }
    }
    catch {
        Write-Diag "Error invoking '$commandName': $_"
        Write-NdjsonResponse -Id $Id -ErrorObj @{
            code    = -1
            message = "$_"
        }
    }
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
            'shutdown' {
                Invoke-ShutdownHandler -Id $id
            }
            'discover' {
                Invoke-DiscoverHandler -Id $id -Params $params
            }
            'invoke' {
                Invoke-InvokeHandler -Id $id -Params $params
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
