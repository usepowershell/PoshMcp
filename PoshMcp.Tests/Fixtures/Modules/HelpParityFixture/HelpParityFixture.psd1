@{
    RootModule        = 'HelpParityFixture.psm1'
    ModuleVersion     = '1.0.0'
    GUID              = 'a2f4d9e0-3c1b-4a8f-9d27-3b1f5d8c7a01'
    Author            = 'PoshMcp'
    Description       = 'Deterministic fixture module for spec 010 description-source precedence and parity tests.'
    PowerShellVersion = '5.1'
    FunctionsToExport = @(
        'Get-FixtureSynopsisOnly',
        'Get-FixtureFullHelp',
        'Get-FixtureHelpMessageOnly',
        'Get-FixtureValidateSetScalar',
        'Get-FixtureValidateSetArray',
        'Get-FixtureBare'
    )
    CmdletsToExport   = @()
    VariablesToExport = @()
    AliasesToExport   = @()
}
