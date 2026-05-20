@{
    RootModule        = 'NounResourceFixture.psm1'
    ModuleVersion     = '1.0.0'
    GUID              = 'b3e5f1a0-4d2c-4b9e-8e38-4c2a6e9d8b02'
    Author            = 'PoshMcp'
    Description       = 'Deterministic fixture module for Spec 012 noun-derived MCP resource mapping integration tests.'
    PowerShellVersion = '5.1'
    FunctionsToExport = @(
        'Get-NounResourceFixture',
        'Assert-NounResourceFixture',
        'Get-NounResourceFixtureError',
        'Get-RequiredFixture',
        'Assert-RequiredFixture',
        'Assert-NoGetFixture'
    )
    CmdletsToExport   = @()
    VariablesToExport = @()
    AliasesToExport   = @()
}
