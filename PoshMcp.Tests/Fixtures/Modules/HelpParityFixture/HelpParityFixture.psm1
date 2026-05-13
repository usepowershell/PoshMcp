# HelpParityFixture
#
# Deterministic PowerShell fixture used by spec 010 (Improve MCP Tool
# Self-Documentation) to exercise the FR-500 / FR-510 description-source
# precedence chains and verify path parity (FR-520 / FR-521).
#
# Each exported function targets one rung of the precedence chain. The
# behavior of the function body is irrelevant; only its metadata
# (synopsis, description, parameter help, attributes) matters.

function Get-FixtureSynopsisOnly {
    <#
    .SYNOPSIS
    Returns a fixed sentinel string; exercises FR-500 step 1 (synopsis only, no description, no parameters).
    #>
    [CmdletBinding()]
    param()
    'synopsis-only'
}

function Get-FixtureFullHelp {
    <#
    .SYNOPSIS
    Returns the input echoed back; exercises FR-500 step 2 and FR-510 step 1.

    .DESCRIPTION
    Long-form description for the fixture function. The body intentionally
    spans multiple paragraphs so that the FR-540 sanitization rules
    (paragraph separator preservation, intra-paragraph whitespace collapse)
    can be exercised end-to-end.

    Second paragraph deliberately uses irregular spacing    and  tabs to
    confirm collapse behavior is consistent across in-process and OOP
    runspaces regardless of host buffer width.

    .PARAMETER Message
    The text to echo back to the caller. Used to verify that per-parameter
    `.PARAMETER` help blocks (FR-510 step 1) are surfaced into the MCP
    parameter description.

    .PARAMETER Count
    How many times the message should be repeated. Demonstrates a second
    `.PARAMETER` block on the same command.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message,

        [Parameter()]
        [int]$Count = 1
    )
    1..$Count | ForEach-Object { $Message }
}

function Get-FixtureHelpMessageOnly {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, HelpMessage = 'The user identifier to look up. Sourced from the HelpMessage attribute.')]
        [string]$UserId,

        [Parameter(HelpMessage = 'Optional region hint used to narrow the lookup. Also from HelpMessage.')]
        [string]$Region
    )
    [pscustomobject]@{ UserId = $UserId; Region = $Region }
}

function Get-FixtureValidateSetScalar {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('Red', 'Green', 'Blue')]
        [string]$Color
    )
    $Color
}

function Get-FixtureValidateSetArray {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('North', 'South', 'East', 'West')]
        [string[]]$Directions
    )
    $Directions
}

function Get-FixtureBare {
    [CmdletBinding()]
    param(
        [Parameter()]
        [string]$Anything
    )
    $Anything
}

Export-ModuleMember -Function `
    Get-FixtureSynopsisOnly, `
    Get-FixtureFullHelp, `
    Get-FixtureHelpMessageOnly, `
    Get-FixtureValidateSetScalar, `
    Get-FixtureValidateSetArray, `
    Get-FixtureBare
