# Program.cs Refactor Plan

**Status:** 49% complete — 4 major PRs merged, additional PRs pending. Target ≤200 lines.
**Completion:** PR 1-4 done, PR 5 (final handlers) in progress.
**Owner:** Farnsworth (Bender executes)

---

## Current Progress (Session: 2025-05-02)

**Baseline:** Program.cs 2,290 lines (starting from 42% completion in prior session)
**Final:** Program.cs 630 lines (73% total reduction — 1,660 lines extracted)

| PR | Extraction | Commit | Status | Lines Moved | New File |
|-------|------------|--------|--------|------------|----------|
| 1 | DoctorService (doctor command logic) | `6a7d9e1` | ✅ Complete | ~500+ | `Diagnostics/DoctorService.cs` |
| 2 | McpToolSetupService (tool discovery, config reload tools) | `f75842d` | ✅ Complete | ~600+ | `Server/McpToolSetupService.cs` |
| 3 | StdioServerHost + HttpServerHost (server startup) | `e4b6309` + `42a55ac` | ✅ Complete | ~694 | `Server/StdioServerHost.cs`, `Server/HttpServerHost.cs` |
| 4 | CliDefinition (CLI tree construction) | `afb76ad` | ✅ Complete | ~250 | `Cli/CliDefinition.cs` |
| 5 | CommandHandlers (all utility command handlers) | `496c2e7` | ✅ Complete | ~536 | `Cli/CommandHandlers.cs` |
| **Cumulative Result** | — | — | — | **2,660 lines** | **5 major classes** |
| **Current Program.cs** | — | — | — | **630 lines** (73% reduction) | — |

**Next Steps (Future):** 
- Extract ConfigurationManager.cs (config file I/O) → potential additional 200-300 line reduction  
- Extract SettingsResolver.cs (settings resolution) → potential additional 150-200 line reduction
- Target ≤200 lines achievable with 1-2 more extractions

---

## Why This Exists

`Program.cs` is ~3,480 lines and owns at least 10 distinct concerns. It is the single largest file in the project by a large margin. Every new feature — auth, logging, docker commands, doctor output, config editing — has been bolted onto it because there was nowhere else obvious to put it. The result is a file where the CLI definition, the server startup, the JSON mutation logic, the doctor diagnostics, and the Docker process-runner all coexist with no separation.

This plan breaks it apart. Every proposed class has one job.

---

## Distinct Concerns Currently Tangled in Program.cs

| # | Concern | Approx. Lines | What it does |
|---|---------|---------------|--------------|
| 1 | CLI tree construction | 65–354 | Option/Command definitions, AddCommand wiring |
| 2 | CLI command handlers | 355–809 | SetHandler lambdas per subcommand |
| 3 | Settings resolution | 811–983 | ParseLogLevel, ResolveArgOrEnv, ResolveCommandSettings, transport/runtime normalization |
| 4 | Config file management | 1825–2291 | Resolve/write/upgrade config files, JSON mutation, interactive prompts |
| 5 | Configuration loading | 2411–2502 | LoadPowerShellConfiguration, LoadMcpResourcesConfig, ValidateResourcesAndPrompts |
| 6 | Doctor/diagnostics | 1200–1760 | RunDoctorAsync, BuildDoctorJson, DiagnoseMissingCommands, CollectPsVersionInfo |
| 7 | MCP tool setup | 2504–2970 | SetupMcpToolsAsync, CreateToolFactory, OOP executor, config/guidance/caching tool factories |
| 8 | Stdio server host | 2559–2577, 2857–3371 | RunMcpServerAsync, ConfigureStdioLogging, ConfigureServerServices, OTel, DI wiring |
| 9 | HTTP server host | 2579–2736, 2738–2818 | RunHttpTransportServerAsync, CORS, health checks, HTTP OTel |
| 10 | Docker commands | 3373–3476 | DetectDockerCommand, CommandExists, ExecuteDockerCommand |
| 11 | Logging helpers | 2379–2384, 2857–2902 | CreateLoggerFactory, MapToSerilogLevel, InferEffectiveLogLevel |
| 12 | Inline types | 1769–1824 | 6 record types + 1 enum that should live with their consumers |

---

## Proposed New Files

All files go under `PoshMcp.Server/` unless a new subdirectory is noted. All types stay in `namespace PoshMcp;` or `PoshMcp.Server` as appropriate — don't change namespaces unless it's a natural fit.

### 1. `Cli/CliDefinition.cs` — CLI tree (safe)

**Responsibility:** Build and return the configured `RootCommand`. No handlers. Purely declarative CLI shape.

**Move here:**
- All `new Option<>` declarations (evaluateToolsOption through runInteractiveOption)
- All `new Command(...)` declarations (serveCommand through runCommand)
- All `command.AddOption()` and `rootCommand.AddCommand()` calls
- `RootCommand` construction

**Expose:** `internal static RootCommand Build()` returning a fully-wired CLI tree. Handlers are **not** registered here — just the shape.

**Benefit:** Eliminates ~290 lines from Main. Adding a new command requires touching only this class.

---

### 2. `Cli/SettingsResolver.cs` — Settings resolution (safe)

**Responsibility:** All the "resolve a setting from CLI > env > config > default" plumbing.

**Move here:**
- `ParseLogLevel`
- `ResolveArgumentOrEnvironment` (both overloads)
- `ResolveLogFilePath`
- `ResolveEffectiveLogLevel`
- `ResolveCommandSettingsAsync`
- `ResolveEffectiveRuntimeMode` (both overloads)
- `ResolveConfigurationPathWithSourceAsync`
- `GetUserConfigPath`
- `InstallEmbeddedDefaultConfigToUserLocationAsync` (config path resolution side-effect)
- `NormalizeTransportValue` / `ResolveTransportMode`
- `NormalizeRuntimeModeValue` / `ResolveRuntimeMode`
- `NormalizeMcpPath`
- `ShouldPrintResolvedSettings`
- `PrintResolvedSettings`
- `HasOption`
- `UpgradeConfigWithMissingDefaultsAsync` (called during path resolution)
- `MergeMissingProperties`
- `LoadEmbeddedDefaultConfig`

**Move types here:**
- `ResolvedSetting`
- `ResolvedCommandSettings`
- `TransportMode` enum

**Note:** `ResolveConfigurationPathWithSourceAsync` currently calls `UpgradeConfigWithMissingDefaultsAsync` — keep that coupling intact; both belong in this class.

---

### 3. `Cli/ConfigurationFileManager.cs` — Config file write/mutate (safe)

**Responsibility:** Everything that mutates an `appsettings.json` on disk.

**Move here:**
- `CreateDefaultConfigInCurrentDirectoryAsync`
- `UpdateConfigurationFileAsync`
- `PromptForAdvancedFunctionConfiguration`
- `IsYesAnswer`
- `PromptForNullableBoolean`
- `GetOrCreateObject`
- `GetOrCreateArray`
- `AddUniqueValues`
- `RemoveValues`
- `NormalizeRuntimeMode` (the one that parses CLI string → "InProcess"/"OutOfProcess")
- `TryParseRequiredBoolean`
- `NormalizeFormat`

**Move types here:**
- `CreateDefaultConfigResult`
- `ConfigUpdateRequest`
- `ConfigUpdateResult`

---

### 4. `Configuration/ConfigurationLoader.cs` — Config loading (safe)

**Responsibility:** Read config objects from file. Pure I/O + binding, no mutation.

**Move here:**
- `LoadPowerShellConfiguration` (both overloads)
- `LoadResourcesAndPromptsConfiguration`
- `LoadMcpResourcesConfiguration`
- `LoadPromptsConfiguration`
- `ValidateResourcesAndPrompts`
- `TryValidateResourcesAndPrompts`
- `LogConfigurationDetails`
- `ResolveExplicitOrDefaultConfigPath`

**Note:** There's a `ResolveExplicitOrDefaultConfigPath` that delegates to `SettingsResolver`. That delegation is fine — keep it, don't inline.

---

### 5. `Doctor/DoctorService.cs` — All doctor/diagnostics logic (safe)

**Responsibility:** The `doctor` command output, both text and JSON.

**Move here:**
- `RunDoctorAsync`
- `BuildDoctorJson`
- `BuildConfiguredFunctionStatus`
- `DiagnoseMissingCommands` (with inner `DiagnoseOneCommand`)
- `CollectPowerShellDiagnostics`
- `ResolveConfiguredModulePathsForOop`
- `BuildConfigurationWarnings`
- `SerializeEffectivePowerShellConfiguration`
- `TryValidateResourcesAndPrompts` (or delegate to `ConfigurationLoader`)
- `ToToolName`
- `GetDiscoveredToolNames`
- `TryGetNameFromObject`
- `GetExpectedToolNames`
- `EscapeForPowerShell`

**Move type here:**
- `ConfiguredFunctionStatus` (already `internal sealed record` — fine to stay `internal`)

---

### 6. `Server/McpToolSetupService.cs` — Tool wiring (safe, mostly)

**Responsibility:** Discover tools, create the tool list, wire reload/caching/guidance/troubleshooting tools.

**Move here:**
- `SetupMcpToolsAsync`
- `SetupHttpMcpToolsAsync`
- `DiscoverToolsAsync`
- `CreateToolFactory`
- `StartOutOfProcessExecutorIfNeededAsync`
- `CreateConfigurationReloadTools`
- `AddConfigurationReloadToolsToList`
- `CreateReloadFromFileToolInstance`
- `CreateUpdateConfigurationToolInstance`
- `CreateGetConfigurationStatusToolInstance`
- `CreateSetResultCachingToolInstance`
- `AddConfigurationGuidanceToolToList`
- `AddConfigurationTroubleshootingToolToList`
- `CreateConfigurationTroubleshootingToolInstance`
- `CreateConfigurationGuidanceToolInstance`
- `ParseEnabledParameter`
- `InferEffectiveLogLevel`
- `OutOfProcessExecutorLease` (nested class — move here as a peer class or keep nested, either is fine)

**Care needed:** `CreateConfigurationTroubleshootingToolInstance` closes over `configurationPath`, `effectiveTransport`, etc. and delegates to `BuildDoctorJson`. Make sure `DoctorService` is accessible (`internal`). The lambda captures `registeredToolsProvider` — that's fine, just make sure the call site still provides a `Func<List<McpServerTool>>`.

---

### 7. `Server/StdioServerHost.cs` — Stdio server startup (safe)

**Responsibility:** Everything needed to stand up the stdio MCP server.

**Move here:**
- `RunMcpServerAsync`
- `ConfigureStdioLogging`
- `ConfigureServerConfiguration`
- `ConfigureServerServices`
- `ConfigureOpenTelemetry` (the `HostApplicationBuilder` version with `isStdioMode` flag)
- `ConfigureJsonSerializerOptions` (the `HostApplicationBuilder` overload)
- `RegisterMcpServerServices`
- `RegisterCleanupServices` (the `HostApplicationBuilder` overload)
- `MapToSerilogLevel`

---

### 8. `Server/HttpServerHost.cs` — HTTP server startup (safe)

**Responsibility:** Everything needed to stand up the HTTP MCP server.

**Move here:**
- `RunHttpTransportServerAsync`
- `ConfigureCorsForMcp`
- `RegisterHealthChecks`
- `ConfigureOpenTelemetryForHttp`
- `ConfigureJsonSerializerOptions` (the `WebApplicationBuilder` overload)
- `RegisterCleanupServices` (the `WebApplicationBuilder` overload)
- `WriteHealthCheckResponseAsync`

---

### 9. `Cli/DockerRunner.cs` — Docker CLI operations (safe)

**Responsibility:** Process-level Docker/Podman command execution.

**Move here:**
- `DetectDockerCommand`
- `CommandExists`
- `ExecuteDockerCommand`

The build and run SetHandler lambdas in Main can call `DockerRunner.*` directly.

---

### 10. `Logging/LoggingHelpers.cs` — Logging utilities (safe)

**Responsibility:** Bootstrap logger factory and level mapping utilities.

**Move here:**
- `CreateLoggerFactory`
- `MapToSerilogLevel` (if not in `StdioServerHost`)
- `InferEffectiveLogLevel` (if not in `McpToolSetupService`)

**Note:** `CreateLoggerFactory` is `internal` and used in tests — keep it `internal static`. Don't change its visibility.

---

## What Stays in Program.cs

After the above extractions, `Program.cs` should contain only:

```
public class Program
{
    // Exit code constants
    // Env var name constants

    public static async Task<int> Main(string[] args)
    {
        var rootCommand = CliDefinition.Build(...);
        // Register handlers — each handler delegates immediately to one of the classes above
        // rootCommand.InvokeAsync(args)
    }
}
```

Target size: **~150 lines**. If it's longer than 200, something didn't get extracted.

---

## Safe vs. Needs Care

### Safe to extract without behavioral risk
Everything listed above is `private static` or `internal static` with no instance state. These are pure function extractions — move the method body, update call sites, done.

- All of `SettingsResolver` — pure functions
- All of `ConfigurationFileManager` — pure JSON mutation, no external state
- All of `ConfigurationLoader` — pure read + bind
- All of `DoctorService` — pure diagnostic assembly
- `DockerRunner` — pure process invocation
- `LoggingHelpers.CreateLoggerFactory` — pure factory

### Needs care (but still safe with attention)

**The `args` capture in SetHandler lambdas:** `serveCommand.SetHandler` and `rootCommand.SetHandler` both close over the `args` parameter from `Main`. When extracting handler logic to dedicated classes, thread `args` through explicitly rather than closing over it. Check each handler.

**`ConfigureJsonSerializerOptions` duplication:** Two identical methods exist — one for `HostApplicationBuilder`, one for `WebApplicationBuilder`. The bodies are identical but the parameter types differ. Options: (a) keep both overloads, (b) extract to a `static Action<JsonSerializerOptions> ConfigureJsonOptions` and call it in both. Option (b) is cleaner. Low risk either way.

**`RegisterCleanupServices` duplication:** Same pattern — two identical bodies, two builder types. Same fix as above.

**`OutOfProcessExecutorLease` nested class:** Currently private nested inside `Program`. When moving to `McpToolSetupService`, make it `internal sealed` or keep it private to that class. Don't accidentally make it public.

**Static mutable state on `McpToolFactoryV2` and `PowerShellAssemblyGenerator`:** `SetMetrics`, `SetRuntimeCachingState`, `SetConfiguration` are static mutation calls. This is a pre-existing anti-pattern — **do not fix it in this refactor**. Just make sure the call sites move with `McpToolSetupService` / `StdioServerHost` / `HttpServerHost` and the ordering (set before use) is preserved.

**`UpgradeConfigWithMissingDefaultsAsync` side effects:** This is called inside `ResolveConfigurationPathWithSourceAsync` (config path resolution). It mutates the config file as a side effect of reading it. The coupling is intentional but opaque. Don't decouple it in this refactor — just move both methods together to `SettingsResolver`.

---

## Execution Order (Actual vs. Planned)

**Actual (Session 2025-05-02):** Prioritized high-value extractions first to maximize reduction quickly.

1. ✅ **PR 1:** `DoctorService.cs` — doctor diagnostics (commit `6a7d9e1`)
2. ✅ **PR 2:** `McpToolSetupService.cs` — tool wiring & setup (commit `f75842d`)
3. ✅ **PR 3:** `StdioServerHost.cs` + `HttpServerHost.cs` — server startup (commit `e4b6309`, fixed `42a55ac`)
4. ✅ **PR 4:** `CliDefinition.cs` — CLI tree extraction (commit `afb76ad`)
5. 🔄 **PR 5 (Next):** Extract remaining utility command handlers to `CommandHandlers.cs` or split into specialized classes
   - Move: RunToolEvaluationAsync, RunListToolsAsync, RunValidateConfigAsync, RunPSModulePathCommand + helpers
   - Target: Reduce Program.cs to ≤200 lines
6. ⏭️ **PR 6 (Future):** Extract config file management (ConfigurationFileManager.cs) if needed
7. ⏭️ **PR 7 (Future):** Extract settings resolution (SettingsResolver.cs) if needed

**Original planned order (reference):**
- A: LoggingHelpers + DockerRunner (deferred — lower priority)
- B: SettingsResolver (deferred — can be standalone)
- C: ConfigurationFileManager (deferred — can be standalone)
- D-I: (reordered based on actual dependencies and reduction impact)

Each completed PR leaves the build green and tests passing. All new files in `namespace PoshMcp;` for consistency with extracted utilities.

---

## Definition of Done

- `Program.cs` is ≤200 lines
- No method in the project is >100 lines (soft target — flag anything over 80)
- All existing tests pass unchanged
- No new public API surface introduced (these are all internal concerns)
- Static mutable state on `McpToolFactoryV2` and `PowerShellAssemblyGenerator` is **not** changed (separate refactor if ever)
