# PowerShell MCP Server Configuration

This document describes the configuration options and validation rules for the PowerShell MCP Server.

## Configuration Schema

### PowerShellConfiguration

The main configuration section for PowerShell command discovery and execution.

```json
{
  "PowerShellConfiguration": {
    "FunctionNames": ["string[]"],
    "Modules": ["string[]"],
    "IncludePatterns": ["string[]"],
    "ExcludePatterns": ["string[]"],
    "EnableDynamicReloadTools": "boolean"
  }
}
```

### Properties

#### FunctionNames (string[])
- **Description**: Specific PowerShell function names to expose as MCP tools
- **Required**: At least one of FunctionNames, Modules, or IncludePatterns must be specified
- **Validation Rules**:
  - Cannot contain null, empty, or whitespace-only values
  - Must follow valid PowerShell command naming conventions (alphanumeric, hyphens, underscores, dots)
  - No duplicate names (case-insensitive)
- **Example**: `["Get-Process", "Get-Service", "Get-ChildItem"]`

#### Modules (string[])
- **Description**: PowerShell module names to import all commands from
- **Required**: At least one of FunctionNames, Modules, or IncludePatterns must be specified
- **Validation Rules**:
  - Cannot contain null, empty, or whitespace-only values
  - Cannot contain path-invalid characters (`/ \ : * ? " < > |`)
  - No duplicate names (case-insensitive)
- **Example**: `["Microsoft.PowerShell.Management", "Microsoft.PowerShell.Utility"]`

#### IncludePatterns (string[])
- **Description**: Wildcard patterns for commands to include
- **Required**: At least one of FunctionNames, Modules, or IncludePatterns must be specified
- **Validation Rules**:
  - Cannot contain null, empty, or whitespace-only values
  - Must be valid wildcard patterns (using * and ? wildcards)
  - No duplicate patterns (case-insensitive)
  - Warns about overly broad dangerous patterns (*, Remove-*, Delete-*, Clear-*, Stop-*)
- **Example**: `["Get-*", "Test-*", "Measure-*"]`

#### ExcludePatterns (string[])
- **Description**: Wildcard patterns for commands to exclude
- **Required**: No
- **Validation Rules**:
  - Cannot contain null, empty, or whitespace-only values
  - Must be valid wildcard patterns (using * and ? wildcards)
  - No duplicate patterns (case-insensitive)
  - Warns if patterns overlap with IncludePatterns
- **Example**: `["*-Dangerous*", "Remove-*"]`

#### EnableDynamicReloadTools (boolean)
- **Description**: Whether to enable configuration reload tools
- **Required**: No
- **Default**: `false`
- **Validation Rules**: Must be a boolean value

## Validation Rules

### Startup Validation

The configuration is validated at application startup with the following checks:

1. **Source Requirement**: At least one source must be configured (FunctionNames, Modules, or IncludePatterns)

2. **Format Validation**: All strings must be non-null, non-empty, and not whitespace-only

3. **Naming Conventions**: 
   - Function names must follow PowerShell naming conventions
   - Module names cannot contain invalid path characters

4. **Pattern Validation**: Wildcard patterns must be valid regex-compatible patterns

5. **Duplicate Detection**: No duplicate entries within each array (case-insensitive)

6. **Logical Consistency**: Warns about potentially problematic configurations:
   - Overlapping include/exclude patterns
   - Overly broad dangerous patterns

### Runtime Validation

Additional validation can be performed at runtime:

- Module availability checking
- Function existence verification
- Permission validation

## Example Configurations

### Basic Configuration
```json
{
  "PowerShellConfiguration": {
    "FunctionNames": [
      "Get-Process",
      "Get-Service",
      "Get-ChildItem"
    ],
    "ExcludePatterns": [],
    "IncludePatterns": [],
    "EnableDynamicReloadTools": false
  }
}
```

### Module-based Configuration
```json
{
  "PowerShellConfiguration": {
    "FunctionNames": [],
    "Modules": [
      "Microsoft.PowerShell.Management"
    ],
    "ExcludePatterns": [
      "Remove-*",
      "Stop-*"
    ],
    "IncludePatterns": [],
    "EnableDynamicReloadTools": true
  }
}
```

### Pattern-based Configuration
```json
{
  "PowerShellConfiguration": {
    "FunctionNames": [],
    "Modules": [],
    "IncludePatterns": [
      "Get-*",
      "Test-*",
      "Measure-*"
    ],
    "ExcludePatterns": [
      "*-Dangerous*"
    ],
    "EnableDynamicReloadTools": false
  }
}
```

## Error Messages

When validation fails, detailed error messages are provided:

- `At least one of FunctionNames, Modules, or IncludePatterns must be specified.`
- `FunctionNames cannot contain null, empty, or whitespace-only values.`
- `Invalid function name 'name'. PowerShell function names should follow Verb-Noun pattern or be valid PowerShell identifiers.`
- `Duplicate function name found: 'name'.`
- `Invalid module name 'name'. Module names cannot contain path-invalid characters.`
- `Invalid wildcard pattern in PropertyName: 'pattern'. Patterns should use * and ? wildcards.`
- `Include pattern 'pattern1' and exclude pattern 'pattern2' may overlap, which could lead to unexpected behavior.`
- `Include pattern 'pattern' is overly broad and may include dangerous commands. Consider more specific patterns.`

## Best Practices

1. **Security**: Use specific patterns instead of broad wildcards like `*`
2. **Performance**: Limit the number of commands exposed to improve startup time
3. **Maintainability**: Use module-based configuration when possible for easier management
4. **Safety**: Always use exclude patterns to filter out dangerous commands
5. **Documentation**: Comment your configuration files to explain choices