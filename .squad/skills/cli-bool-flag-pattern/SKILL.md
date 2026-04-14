---
name: "cli-bool-flag-pattern"
description: "Pattern for adding boolean configuration flags to the update-config CLI command in Program.cs using helper methods and JSON mutation."
domain: "cli-design"
confidence: "medium"
source: "observed"
---

## Context
The `update-config` CLI command in `Program.cs` allows users to update configuration settings via command-line flags. Boolean flags follow a consistent pattern using two helpers: `TryParseRequiredBoolean()` for argument parsing and `GetOrCreateObject()` for JSON section navigation. This skill codifies the repeated pattern across PRs (#85, #86, #92, and others).

## Patterns
- **Parse helper:** `TryParseRequiredBoolean(args, "--flag-name", ref settingsChanged, out bool? value)` extracts and validates a flag from CLI arguments. Increments `settingsChanged` counter on success.
- **JSON nesting helper:** `GetOrCreateObject(jObject, "SectionName")` navigates to or creates a nested JSON object for grouping related settings.
- **Three-step flow per flag:**
  1. Parse the flag argument: `TryParseRequiredBoolean(args, "--my-flag", ref settingsChanged, out var myFlag);`
  2. Get or create the JSON section: `var section = GetOrCreateObject(config, "MySection");`
  3. Set the value and increment counter: `section["myFlag"] = myFlag; settingsChanged++;` (counter already incremented by helper, so increment again only if extra logic applies; check helper behavior)
- **Counter semantics:** `settingsChanged` is an `int` counter (not a bool). Each successfully applied flag increments it. The final count appears in JSON output under `SettingsChanged`.
- **JSON section nesting conventions:**
  - Performance-related flags nest under `powerShellConfiguration.Performance` (e.g., `EnableCaching`)
  - Authentication-related flags nest at root level under `Authentication` (e.g., `RequireAuth`)
  - Function-related flags nest under `Function` section
- **Testing pattern:** All new bool flags require corresponding unit tests in `ProgramCliConfigCommandsTests.cs`. Follow the existing test structure: parse args, call config command, assert JSON output structure and value.
- **Related skills:** `cli-config-resolution` covers path resolution logic; this skill covers the flag-parsing and JSON mutation pattern specifically.

## Examples
**Adding a new Performance boolean flag:**
```csharp
// In Program.cs update-config handler
var config = JObject.Parse(File.ReadAllText(configPath));

var settingsChanged = 0;

TryParseRequiredBoolean(args, "--enable-caching", ref settingsChanged, out var enableCaching);
if (enableCaching.HasValue)
{
    var perfSection = GetOrCreateObject(config, "powerShellConfiguration", "Performance");
    perfSection["enableCaching"] = enableCaching.Value;
}

// Output
var output = new { SettingsChanged = settingsChanged, Config = config };
Console.WriteLine(JsonConvert.SerializeObject(output, Formatting.Indented));
```

**Unit test pattern:**
```csharp
[Fact]
public void UpdateConfig_WithEnableCachingFlag_SetsPerfSectionValue()
{
    var args = new[] { "update-config", "--config-path", "config.json", "--enable-caching", "true" };
    var config = new { powerShellConfiguration = new { Performance = new { } } };
    File.WriteAllText("config.json", JsonConvert.SerializeObject(config));

    // Act
    Program.Main(args);

    // Assert
    var result = JObject.Parse(File.ReadAllText("config.json"));
    Assert.True((bool)result["powerShellConfiguration"]["Performance"]["enableCaching"]);
}
```

**JSON nesting with GetOrCreateObject:**
```csharp
var authSection = GetOrCreateObject(config, "Authentication");
authSection["requireAuth"] = true;  // Nested at root: config.Authentication.requireAuth

var perfSection = GetOrCreateObject(config, "powerShellConfiguration", "Performance");
perfSection["enableCaching"] = true;  // Nested: config.powerShellConfiguration.Performance.enableCaching
```

## Anti-Patterns
- ❌ Mixing boolean and non-boolean flag parsing without consistent helpers
- ❌ Directly manipulating the JSON object without `GetOrCreateObject` (creates brittle code if structure changes)
- ❌ Incrementing `settingsChanged` manually instead of deferring to the helper (double-counting or missed increments)
- ❌ Nesting unrelated sections (mixing Performance flags at root level or Auth flags under Function section)
- ❌ Adding new boolean flags without corresponding unit tests
- ❌ Hardcoding section names in flag handlers instead of defining constants
