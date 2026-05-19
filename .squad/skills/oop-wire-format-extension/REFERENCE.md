# OOP Wire-Format Extension — Code Reference

## C# Field Addition (RemoteToolSchema)

```csharp
public class RemoteToolSchema
{
    // existing fields...

    [JsonProperty("sourceModule", NullValueHandling = NullValueHandling.Ignore)]
    public string? SourceModule { get; set; }
}
```

## Parallel Top-Level Payload (JSON shape)

```jsonc
{
    "id": "...",
    "result": {
        "tools": [ /* RemoteToolSchema[] */ ],
        "moduleImports": { /* RemoteModuleImportsPayload */ }   // optional
    }
}
```

## OOP Host Pool Defensive Unwrap (PowerShell)

```powershell
$result = & $scriptBlock @args
if ($result -is [pscustomobject] -and $result.PSObject.Properties.Match('Schemas').Count -gt 0) {
    $schemas = $result.Schemas
    $moduleImports = $result.ModuleImports
} else {
    # legacy bare-array shape
    $schemas = $result
    $moduleImports = $null
}
```

## Source-Attribution Priority (PowerShell)

```powershell
$sourceMap = @{}
foreach ($cmdName in $config.CommandNames) {
    if (-not $sourceMap.ContainsKey($cmdName)) {
        $sourceMap[$cmdName] = @{ Source = 'commandName'; Detail = $cmdName }
    }
}
foreach ($mod in $config.Modules) {
    foreach ($cmdName in (Get-Command -Module $mod).Name) {
        if (-not $sourceMap.ContainsKey($cmdName)) {
            $sourceMap[$cmdName] = @{ Source = 'module'; Detail = $mod }
        }
    }
}
foreach ($pattern in $config.IncludePatterns) {
    foreach ($cmdName in (Get-Command -Name $pattern).Name) {
        if (-not $sourceMap.ContainsKey($cmdName)) {
            $sourceMap[$cmdName] = @{ Source = 'pattern'; Detail = $pattern }
        }
    }
}
```
