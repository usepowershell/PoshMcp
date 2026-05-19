# NounResourceFixture.psm1
#
# Deterministic PowerShell fixture module for Spec 012 (Noun-Derived MCP Resource Mapping)
# integration tests.  Each function targets one test scenario; function bodies are
# irrelevant — only the verb-noun structure and execution outcome matter.

<#
.SYNOPSIS
Returns a fixed sentinel object; exercises FR-NR-05 (resources/list), FR-NR-06
(resources/read executes Get command), FR-NR-08A (Get-* verb augmented), and
FR-NR-14 (static + noun resources coexist).
Noun: NounResourceFixture → resource name: noun_resource_fixture
#>
function Get-NounResourceFixture {
    [CmdletBinding()]
    param()
    [pscustomobject]@{ Name = 'NounResourceFixture'; Value = 42 }
}

<#
.SYNOPSIS
Non-Get command sharing the NounResourceFixture noun; exercises FR-NR-08
(resourceLinkBlock injected on successful non-error result).
#>
function Assert-NounResourceFixture {
    [CmdletBinding()]
    param(
        [Parameter()]
        [string]$InputValue = 'default'
    )
    [pscustomobject]@{ AssertedValue = $InputValue; Status = 'asserted' }
}

<#
.SYNOPSIS
Get command that always terminates with an error; exercises FR-NR-09
(no resourceLinkBlock injected when result IsError = true) and FR-NR-07
(resources/read failure returns McpError).
Noun: NounResourceFixtureError → resource name: noun_resource_fixture_error
#>
function Get-NounResourceFixtureError {
    [CmdletBinding()]
    param()
    throw 'Deliberate integration test error from NounResourceFixture module'
}

<#
.SYNOPSIS
Non-Get command whose noun (NoGetFixture) has no Get-NoGetFixture counterpart
in the configured command set; exercises FR-NR-10 (non-resourceable noun
receives no resourceLinkBlock).
#>
function Assert-NoGetFixture {
    [CmdletBinding()]
    param(
        [Parameter()]
        [string]$InputValue = 'default'
    )
    [pscustomobject]@{ Value = $InputValue; Status = 'asserted' }
}

Export-ModuleMember -Function `
    Get-NounResourceFixture, `
    Assert-NounResourceFixture, `
    Get-NounResourceFixtureError, `
    Assert-NoGetFixture
