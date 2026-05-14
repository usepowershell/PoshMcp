# Spec 009 issue #212 — add Category traits to test classes.
# Inserts [Trait("Category", "<X>")] immediately above each `public class *Tests` line.
# Idempotent: skips classes that already carry a Category trait.
# Adds `using Xunit;` to files that lack it.

param(
    [string]$Root = "$PSScriptRoot\..\PoshMcp.Tests"
)

$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path $Root).Path

# Map of test class FullName/SimpleName -> Category.
# Sourced from honest behavioral audit (see spec 009 FR-401..417).
# Folder location is a hint, NOT authoritative.
$catMap = @{
    # ---------- Unit (no subprocess, no port, no shared temp) ----------
    'AuthenticationConfigurationValidatorTests'        = 'Unit'
    'AuthenticationServiceExtensionsTests'             = 'Unit'
    'AuthorizationHelpersTests'                        = 'Unit'
    'ConfigurationGuidanceToolsTests'                  = 'Unit'
    'ConfigurationHealthCheckTests'                    = 'Unit'
    'ConfigureApplicationInsightsTests'                = 'Unit'
    'DescriptionSanitizerTests'                        = 'Unit'
    'DescriptionSourceMetricsTests'                    = 'Unit'
    'DockerRunnerTests'                                = 'Unit'
    'DoctorReportTests'                                = 'Unit'
    'DoctorTextRendererTests'                          = 'Unit'
    'FunctionOverrideAuthPropertiesTests'              = 'Unit'
    'HealthChecksTests'                                = 'Unit'
    'HttpToolFactoryParityTests'                       = 'Unit'
    'McpToolFactoryV2Tests'                            = 'Unit'
    'MetricsTests'                                     = 'Unit'
    'ModuleDiscoveryStartupOrderingTests'              = 'Unit'
    'OAuthProxyEndpointsTests'                         = 'Unit'
    'OutputTypeTests'                                  = 'Unit'
    'ParameterSetConsistencyTests'                     = 'Unit'
    'ParameterTypeTests'                               = 'Unit'
    'PerformanceConfigurationTests'                    = 'Unit'
    'PowerShellJsonSerializationTests'                 = 'Unit'
    'ProgramCliBuildCommandTests'                      = 'Unit'
    'ProgramCliConfigCommandsTests'                    = 'Unit'
    'ProgramCliScaffoldCommandTests'                   = 'Unit'
    'ProgramConfigurationGuidanceToolExposureTests'    = 'Unit'
    'ProgramDoctorConfigCoverageTests'                 = 'Unit'
    'ProgramDoctorToolExposureTests'                   = 'Unit'
    'ProgramTests'                                     = 'Unit'
    'ProgramTransportSelectionTests'                   = 'Unit'
    'RuntimeCachingStateTests'                         = 'Unit'
    'ServerSessionAwarePowerShellRunspaceTests'        = 'Unit'
    'SimpleAssemblyTests'                              = 'Unit'
    'StdioLoggingConfigurationTests'                   = 'Unit'
    'ToolNameMappingTests'                             = 'Unit'
    'UnserializableTypeTests'                          = 'Unit'
    'WinPsCompatProxyTests'                            = 'Unit'
    'DoctorDescriptionSourceTests'                     = 'Unit'
    'McpPromptConfigurationBindingTests'               = 'Unit'
    'McpPromptsValidatorTests'                         = 'Unit'
    'McpResourceConfigurationBindingTests'             = 'Unit'
    'McpResourcesValidatorTests'                       = 'Unit'
    'LogSanitizerTests'                                = 'Unit'

    # ---------- OutOfProcess (exercises pwsh subprocess via OOP host) ----------
    'OutOfProcessCancellationTests'                    = 'OutOfProcess'
    'OutOfProcessCommandExecutorTests'                 = 'OutOfProcess'
    'OutOfProcessHostConcurrencyTests'                 = 'OutOfProcess'
    'OutOfProcessHostTests'                            = 'OutOfProcess'
    'OutOfProcessSubprocessPoolTests'                  = 'OutOfProcess'
    'OutOfProcessToolAssemblyGeneratorTests'           = 'OutOfProcess'
    'RemoteToolSchemaTests'                            = 'OutOfProcess'
    'RuntimeModeTests'                                 = 'OutOfProcess'
    'OutOfProcessIntegrationTests'                     = 'OutOfProcess'
    'OutOfProcessMcpRoundTripTests'                    = 'OutOfProcess'
    'OutOfProcessModuleTests'                          = 'OutOfProcess'
    'OutOfProcessPoolHostIntegrationTests'             = 'OutOfProcess'
    'OutOfProcessSubprocessPoolIntegrationTests'       = 'OutOfProcess'

    # ---------- Http (binds TCP port / uses HTTP transport) ----------
    'UnifiedHttpTransportIntegrationTests'             = 'Http'
    'ApplicationInsightsIntegrationTests'              = 'Http'

    # ---------- Azure (requires Azure credentials) ----------
    'AzureDeploymentIntegrationTests'                  = 'Azure'

    # ---------- Integration (resource-using, none of the specialised buckets) ----------
    'CommandLineTests'                                 = 'Integration'
    'ConfigurationGuidanceIntegrationTests'            = 'Integration'
    'DeployScriptConfigurationPrecedenceTests'         = 'Integration'
    'IntegrationTests'                                 = 'Integration'
    'McpPromptsIntegrationTests'                       = 'Integration'
    'McpResourcesIntegrationTests'                     = 'Integration'
    'McpServerIntegrationTests'                        = 'Integration'
    'McpServerProcessLifecycleTests'                   = 'Integration'
    'MultiUserIsolationTests'                          = 'Integration'
    'ToolDescriptionParityTests'                       = 'Integration'
    'ToolDescriptionRegressionTests'                   = 'Integration'
    # Functional/StdioLoggingTests uses InProcessMcpServer (subprocess) -> Integration per FR-416.
    'StdioLoggingTests'                                = 'Integration'

    # ---------- Functional (multi-area, in-process PowerShell only) ----------
    'McpPromptsTests'                                  = 'Functional'
    'McpResourcesTests'                                = 'Functional'
    'SwitchParameterMcpRoundTripTests'                 = 'Functional'
    'WinPsCompatProxyMethodGenerationTests'            = 'Functional'
    'ConfigurationReloadTests'                         = 'Functional'
    'DynamicReloadToolsFeatureFlagTests'               = 'Functional'
    'EndToEndDynamicReloadToolsTests'                  = 'Functional'
    'CorrelationIdGenerationTests'                     = 'Functional'
    'CorrelationIdLoggingTests'                        = 'Functional'
    'CorrelationIdMiddlewareTests'                     = 'Functional'
    'CorrelationIdPropagationTests'                    = 'Functional'
    'ShouldFilterCachedResultsTest'                    = 'Functional'
    'ShouldReturnNullWhenInvalidScriptTest'            = 'Functional'
    'ShouldReturnNullWhenNoCacheTest'                  = 'Functional'
    'ShouldCreateParameterSetSpecificMethodsTest'      = 'Functional'
    'ShouldCreateValidAssemblyTest'                    = 'Functional'
    'ShouldIncludeAllUtilityMethodsTest'               = 'Functional'
    'ShouldIncludeFilterUtilityMethodTest'             = 'Functional'
    'ShouldIncludePhase3FrameworkParametersTest'       = 'Functional'
    'ShouldIncludeSortUtilityMethodsTest'              = 'Functional'
    'ShouldReturnMethodsForCommandsTest'               = 'Functional'
    'ShouldReturnValidInstanceTest'                    = 'Functional'
    'ShouldReturnNullTest'                             = 'Functional'  # GetLastCommandOutput + SortLastCommandOutput
    'ShouldFilterCorrectlyWithExcludePatternsTest'     = 'Functional'
    'ShouldHandleNonExistentFunctionGracefullyTest'    = 'Functional'
    'ShouldHaveEmptyDefaultValuesTest'                 = 'Functional'
    'ShouldParseJsonFileCorrectlyTest'                 = 'Functional'
    'ShouldReturnEmptyListWithEmptyConfigurationTest'  = 'Functional'
    'ShouldReturnToolsWithValidConfigurationTest'      = 'Functional'
    'ShouldThrowExceptionWithInvalidJsonTest'          = 'Functional'
    'ShouldThrowExceptionWithMissingFileTest'          = 'Functional'
    'ShouldWorkWithConfigurationToToolsListIntegrationTest' = 'Functional'
    'ShouldWorkWithDefaultParameterlessOverloadTest'   = 'Functional'
    'ShouldBeCallableDirectlyTest'                     = 'Functional'
    'ShouldApplyPhase3PropertyFilteringTest'           = 'Functional'
    'ShouldCacheResultsTest'                           = 'Functional'
    'ShouldHandleGetProcessSerializationTest'          = 'Functional'
    'ShouldHandleInvalidCommandTest'                   = 'Functional'
    'ShouldHandleValidCommandTest'                     = 'Functional'
    'ShouldOverwritePreviousCacheTest'                 = 'Functional'
    'WithParametersTest'                               = 'Functional'
    'ShouldHandleGetChildItemCorrectlyTest'            = 'Functional'
    'ShouldReturnObjectArrayTest'                      = 'Functional'
    'ShouldSortCachedResultsTest'                      = 'Functional'
    # Partial-class containers: tagged on the *Shared.cs file only.
    'SetupTests'                                       = 'Functional'
    'ExecutionTests'                                   = 'Functional'
    'GeneratedInstance'                                = 'Functional'  # tagged in UtilityMethodsShared.cs
    'GeneratedMethod'                                  = 'Functional'  # tagged in TypeTestsShared.cs
    # Partial without Shared.cs — tagged in the alphabetically-first file.
    'FilterCachedResults'                              = 'Functional'  # tagged in ShouldFilterCachedResultsTest.cs
    # Single-file classes whose name does NOT match the file basename.
    'GetLastCommandResult'                             = 'Functional'
    'ShouldApplyPhase3PropertyFiltering'               = 'Functional'
    'CacheResults'                                     = 'Functional'
    'HandleGetProcessSerialization'                    = 'Functional'
    'InvalidCommand'                                   = 'Functional'
    'ValidCommand'                                     = 'Functional'
    'OverwriteCache'                                   = 'Functional'
    'ExecutePowerShellCommandWithParameters'           = 'Functional'
    # SortLastCommandOutput\ShouldSortCachedResultsTest.cs declares `Output_Test` (legacy name).
    'Output_Test'                                      = 'Functional'
}

# Partial classes without a *Shared.cs — designate the canonical owner file.
$partialOwner = @{
    'FilterCachedResults' = 'ShouldFilterCachedResultsTest.cs'
}

$counts = @{}
$tagged = @()
$skipped = @()
$unknown = @()

$files = Get-ChildItem -Path $Root -Recurse -Filter '*.cs' |
    Where-Object {
        $_.FullName -notmatch '\\(bin|obj|TestResults)\\' -and
        $_.Name -ne 'AssemblyInfo.cs'
    }

foreach ($file in $files) {
    $rel = $file.FullName.Substring($Root.Length + 1)
    $text = Get-Content -Raw -LiteralPath $file.FullName
    $orig = $text

    # Find all `public ... class FooTests` declarations (handles `sealed`, generics).
    $pattern = '(?m)^(?<indent>[ \t]*)public\s+(?<modifiers>(?:sealed\s+|static\s+|abstract\s+|partial\s+)*)class\s+(?<name>\w+)(?<rest>[\s\S]*?\{)'
    $classMatches = [regex]::Matches($text, $pattern)

    if ($classMatches.Count -eq 0) { continue }

    # Process matches in reverse so earlier offsets stay valid.
    $newText = $text
    for ($i = $classMatches.Count - 1; $i -ge 0; $i--) {
        $m = $classMatches[$i]
        $name = $m.Groups['name'].Value
        $isPartial = $m.Groups['modifiers'].Value -match '\bpartial\b'
        # Partial classes accept [Trait] on only ONE declaration.
        # Default policy: tag the *Shared.cs canonical partial.
        # Exception: if $partialOwner names a specific file, only tag in that file.
        if ($isPartial) {
            if ($partialOwner.ContainsKey($name)) {
                if ($file.Name -ne $partialOwner[$name]) { continue }
            } elseif (-not $file.Name.EndsWith('Shared.cs')) {
                continue
            }
        }
        # Anything in catMap is fair game; class names that don't end in `Test`/`Tests`
        # (e.g. `CacheResults`, `Output_Test`) are still test classes by other criteria.
        if (-not $catMap.ContainsKey($name)) {
            if ($name -notmatch 'Shared$') {
                $unknown += "$rel : class $name"
            }
            continue
        }

        $category = $catMap[$name]
        $indent = $m.Groups['indent'].Value
        $startLine = $m.Index

        # Walk backwards to find the start of the contiguous attribute/comment/doc block
        # immediately preceding the class declaration.
        $blockStart = $startLine
        $lines = $newText.Substring(0, $startLine).Split("`n")
        $idx = $lines.Length - 1
        # Track lines that immediately precede the class decl and are part of its attribute/doc cluster.
        for ($j = $idx; $j -ge 0; $j--) {
            $line = $lines[$j].TrimStart()
            if ($line.StartsWith('[') -or $line.StartsWith('///') -or $line.StartsWith('//')) {
                continue
            }
            if ($line -eq '') { continue }  # allow blank lines inside the cluster (rare)
            $idx = $j + 1
            break
        }
        if ($j -lt 0) { $idx = 0 }

        # Build offset of the attribute-cluster start.
        $clusterStartOffset = 0
        for ($k = 0; $k -lt $idx; $k++) {
            $clusterStartOffset += $lines[$k].Length + 1  # +1 for the \n we split on
        }

        $cluster = $newText.Substring($clusterStartOffset, $startLine - $clusterStartOffset)
        # Strip /// doc-comment lines and // line comments so a `[Trait("Category", ...)]`
        # mentioned inside a docstring doesn't trip the "already tagged" guard.
        $clusterCode = ($cluster -split "`n" |
            Where-Object { $_.TrimStart() -notmatch '^(///|//)' }) -join "`n"
        if ($clusterCode -match '\[Trait\s*\(\s*"Category"') {
            $skipped += "$rel : $name (already tagged)"
            continue
        }

        $traitLine = "$indent[Trait(`"Category`", `"$category`")]`r`n"
        $newText = $newText.Substring(0, $startLine) + $traitLine + $newText.Substring($startLine)
        $tagged += "$rel : $name -> $category"
        if (-not $counts.ContainsKey($category)) { $counts[$category] = 0 }
        $counts[$category]++
    }

    if ($newText -ne $orig) {
        # Ensure `using Xunit;` is present (Trait lives in Xunit namespace).
        if ($newText -notmatch '(?m)^using\s+Xunit\s*;') {
            # Insert after the last `using` line, or at the top.
            $usingMatches = [regex]::Matches($newText, '(?m)^using\s[^\r\n]+;\r?\n')
            if ($usingMatches.Count -gt 0) {
                $last = $usingMatches[$usingMatches.Count - 1]
                $insertAt = $last.Index + $last.Length
                $newText = $newText.Substring(0, $insertAt) + "using Xunit;`r`n" + $newText.Substring($insertAt)
            } else {
                $newText = "using Xunit;`r`n" + $newText
            }
        }

        Set-Content -LiteralPath $file.FullName -Value $newText -NoNewline
    }
}

Write-Host ""
Write-Host "=== TAGGED ===" -ForegroundColor Green
$tagged | ForEach-Object { Write-Host "  $_" }
Write-Host ""
Write-Host "=== SKIPPED (already tagged) ===" -ForegroundColor Yellow
$skipped | ForEach-Object { Write-Host "  $_" }
Write-Host ""
Write-Host "=== UNKNOWN (no entry in catMap) ===" -ForegroundColor Red
$unknown | ForEach-Object { Write-Host "  $_" }
Write-Host ""
Write-Host "=== COUNTS ===" -ForegroundColor Cyan
$counts.GetEnumerator() | Sort-Object Name | ForEach-Object {
    Write-Host ("  {0,-15} {1}" -f $_.Key, $_.Value)
}
Write-Host ("  {0,-15} {1}" -f 'TOTAL', ($counts.Values | Measure-Object -Sum).Sum)
