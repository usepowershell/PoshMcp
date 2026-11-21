#requires -Version 5.1
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
$utf8 = New-Object System.Text.UTF8Encoding($false, $true)
$pipe = $null

function Read-Bytes {
    param([int]$Length)
    $bytes = New-Object byte[] $Length
    $offset = 0
    while ($offset -lt $Length) {
        $read = $pipe.Read($bytes, $offset, $Length - $offset)
        if ($read -eq 0) { throw 'Private channel closed.' }
        $offset += $read
    }
    return ,$bytes
}

function Read-Frame {
    param([string]$Kind, [string]$Id)
    $header = Read-Bytes 4
    $length = [BitConverter]::ToInt32($header, 0)
    if ($length -le 0 -or $length -gt 1048576) { throw 'Invalid frame size.' }
    $message = $utf8.GetString((Read-Bytes $length)) | ConvertFrom-Json
    if ($message.version -ne 1 -or $message.kind -ne $Kind -or ($Id -and $message.id -ne $Id)) {
        throw 'Invalid version, lifecycle or correlation.'
    }
    return $message
}

function Write-Frame {
    param([string]$Kind, [string]$Id, $Payload)
    $json = @{ version = 1; kind = $Kind; id = $Id; payload = $Payload } | ConvertTo-Json -Depth 32 -Compress
    $bytes = $utf8.GetBytes($json)
    if ($bytes.Length -gt 1048576) { throw 'Serialization byte budget exceeded.' }
    $header = [BitConverter]::GetBytes([int]$bytes.Length)
    $pipe.Write($header, 0, 4)
    $pipe.Write($bytes, 0, $bytes.Length)
    $pipe.Flush()
}

function ConvertTo-PlainData {
    param($Value, [int]$Depth = 0)
    if ($Depth -gt 16) { throw 'Serialization depth exceeded.' }
    if ($null -eq $Value) { return $null }
    if ($Value -is [string] -or $Value -is [bool] -or $Value -is [int] -or $Value -is [double]) { return $Value }
    if ($Value -is [long] -or $Value -is [decimal]) {
        return @{ kind = $Value.GetType().Name; value = $Value.ToString([Globalization.CultureInfo]::InvariantCulture) }
    }
    if ($Value -is [datetime]) { return @{ kind = 'DateTime'; value = $Value.ToUniversalTime().ToString('o') } }
    if ($Value -is [Collections.IDictionary]) {
        $result = @{}
        foreach ($key in $Value.Keys) { $result[[string]$key] = ConvertTo-PlainData $Value[$key] ($Depth + 1) }
        return $result
    }
    if ($Value -is [Collections.IEnumerable]) {
        $items = @()
        foreach ($item in $Value) { $items += ,(ConvertTo-PlainData $item ($Depth + 1)) }
        return ,$items
    }
    if ($Value -is [pscustomobject]) {
        $result = @{}
        foreach ($property in $Value.PSObject.Properties) {
            $result[$property.Name] = ConvertTo-PlainData $property.Value ($Depth + 1)
        }
        return $result
    }
    throw 'Unsupported output type; no silent ToString fallback.'
}

try {
    $bootstrap = [Console]::In.ReadLine() | ConvertFrom-Json
    $pipe = New-Object IO.Pipes.NamedPipeClientStream('.', $bootstrap.pipe, [IO.Pipes.PipeDirection]::InOut)
    $pipe.Connect(10000)
    Write-Frame 'hello' $bootstrap.nonce @{ nonce = $bootstrap.nonce }
    $initialize = Read-Frame 'initialize' ''
    $profile = $initialize.payload.profile
    $architecture = if ([IntPtr]::Size -eq 4) { 'x86' } else { 'x64' }
    if ('System.Runtime.InteropServices.RuntimeInformation' -as [type]) {
        $architecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant()
    } elseif ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { $architecture = 'arm64' }
    $edition = $PSVersionTable.PSEdition
    $version = $PSVersionTable.PSVersion.ToString()
    if ($architecture -ne $profile.architecture -or
        ($profile.kind -eq 'PowerShell7' -and ($edition -ne 'Core' -or $version -ne $profile.version)) -or
        ($profile.kind -eq 'WindowsPowerShell51' -and ($edition -ne 'Desktop' -or -not $version.StartsWith('5.1.')))) {
        throw 'Runtime identity mismatch.'
    }
    Set-Location -LiteralPath $profile.workingDirectory
    foreach ($module in $profile.modules) {
        Import-Module -Name $module.name -RequiredVersion $module.version -ErrorAction Stop
    }
    foreach ($startup in $profile.startupFiles) {
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $hash = [BitConverter]::ToString($sha.ComputeHash([IO.File]::ReadAllBytes($startup.path))).Replace('-', '').ToLowerInvariant() }
        finally { $sha.Dispose() }
        if ($hash -ne $startup.sha256) { throw 'Startup generation mismatch.' }
        . $startup.path
    }
    foreach ($command in $profile.commands) { $null = Get-Command -Name $command -ErrorAction Stop }
    Write-Frame 'ready' $initialize.id @{
        pid = $PID; edition = $edition; version = $version; architecture = $architecture
        generation = $initialize.payload.generation
    }
    $invoke = Read-Frame 'invoke' ''
    if ($profile.commands -notcontains $invoke.payload.command) { throw 'Command is not allowed.' }
    $metadata = Get-Command -Name $invoke.payload.command -ErrorAction Stop
    $parameters = @{}
    foreach ($property in $invoke.payload.parameters.PSObject.Properties) {
        if (-not $metadata.Parameters.ContainsKey($property.Name)) { throw 'Unknown parameter.' }
        $parameters[$property.Name] = $property.Value
    }
    Write-Frame 'executing' $invoke.id @{}
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $invocationErrors = @()
    $invocationWarnings = @()
    $output = @()
    $success = $true
    try {
        $ErrorActionPreference = 'Continue'
        $output = @(& $invoke.payload.command @parameters -ErrorVariable invocationErrors -WarningVariable invocationWarnings)
        $ErrorActionPreference = 'Stop'
        $output = ConvertTo-PlainData $output
    } catch {
        $success = $false
        $invocationErrors += $_
        $output = @()
    }
    $nativeCode = $null
    $exitVariable = Get-Variable -Name LASTEXITCODE -ErrorAction SilentlyContinue
    if ($null -ne $exitVariable) { $nativeCode = $exitVariable.Value }
    if ($invocationErrors.Count -gt 0 -or ($null -ne $nativeCode -and $nativeCode -ne 0)) { $success = $false }
    $result = @{
        success = $success; output = @($output); errors = @($invocationErrors | ForEach-Object { $_.ToString() })
        warnings = @($invocationWarnings | ForEach-Object { $_.ToString() })
        nativeExitCode = $nativeCode; executionMs = $timer.Elapsed.TotalMilliseconds
    }
    Write-Frame 'result' $invoke.id $result
    $null = Read-Frame 'retire' $invoke.id
} catch {
    # Never log invocation data or recover a result from console output.
    [Console]::Error.WriteLine('P0 worker failed initialization, invocation or private IPC.')
    exit 1
} finally {
    if ($null -ne $pipe) { $pipe.Dispose() }
}
