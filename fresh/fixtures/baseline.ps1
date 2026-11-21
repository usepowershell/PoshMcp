function Invoke-Probe {
    [CmdletBinding()]
    param([string]$Text, [switch]$Flag, [string[]]$Items)
    [Console]::Out.WriteLine("stdout `nUnicode: $([char]0x03bb)")
    [Console]::Error.WriteLine("stderr `nUnicode: $([char]0x03bb)")
    Write-Warning 'fixture warning'
    [pscustomobject]@{ Text = $Text; Flag = [bool]$Flag; Items = $Items; Null = $null }
}

function Invoke-ProbeError {
    [CmdletBinding()]
    param()
    'partial output'
    Write-Error 'fixture nonterminating error'
}

function Invoke-ProbeHang {
    [CmdletBinding()]
    param()
    Start-Sleep -Seconds 120
}

function Invoke-ProbeCrash {
    [CmdletBinding()]
    param()
    [Environment]::Exit(23)
}

function Invoke-ProbeDescendant {
    [CmdletBinding()]
    param()
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = (Get-Process -Id $PID).Path
    $info.Arguments = '-NoLogo -NoProfile -NonInteractive -Command "Start-Sleep -Seconds 120"'
    $info.UseShellExecute = $false
    $child = [Diagnostics.Process]::Start($info)
    $child.Id
    $child.Dispose()
}
