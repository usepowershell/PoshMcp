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

function Invoke-ProbeNative {
    [CmdletBinding()]
    param()
    # Avoid native argument quote stripping in Windows PowerShell 5.1.
    $encodedCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes(
        '[Console]::Out.WriteLine("native stdout"); [Console]::Error.WriteLine("native stderr"); exit 17'))
    & (Get-Process -Id $PID).Path -NoLogo -NoProfile -NonInteractive -EncodedCommand $encodedCommand
}

function Invoke-ProbeSerialization {
    [CmdletBinding()]
    param([ValidateSet('tagged', 'unsupported', 'depth')][string]$Case)
    switch ($Case) {
        'tagged' {
            [pscustomobject]@{
                Integer = [long]::MaxValue
                Decimal = [decimal]::Parse('1234567890.123456789', [Globalization.CultureInfo]::InvariantCulture)
                Timestamp = [datetime]::Parse('2026-01-02T03:04:05Z', [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AdjustToUniversal)
            }
        }
        'unsupported' { [version]'1.2.3' }
        'depth' {
            $value = @{ leaf = 'value' }
            for ($index = 0; $index -lt 20; $index++) { $value = @{ nested = $value } }
            $value
        }
    }
}
