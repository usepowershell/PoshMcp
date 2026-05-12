# Bender Work History

**Status:** 42.8 KB (checked 2026-05-11: within 90-day retention, no archival required)
**Status:** 37.6 KB (checked 2026-05-03: within 90-day retention, no archival required)

## Learnings

### 2026-05-12 (round 3): OOP cross-invoke — production v0.12.2 evidence; defensive scope landed without local repro

**Requested by:** Steven Murawski (Brady). Brady returned with hard
production evidence: two sequential MCP calls against the deployed
poshmcp-web server returned byte-for-byte identical payloads even though
the tools were unrelated (`get_tenant_context` then
`assert_tenant_role_member`, both responses showing tenant-context
JSON). Brady's directive: re-read the pool host with fresh eyes, run an
aggressive repro that actually mirrors production (runspacePoolSize=10,
DIFFERENT scripts in each step, 50+ iterations), and apply
defense-in-depth even if I still can't reproduce locally.

**Production config gap I missed in round 2:** my round-2 repro ran at
pool=2 / 6 iterations / SAME script with different params. The deployed
server runs at pool=10 / process pool=4 / AdvocacyBami workload. The
"same script with different params" gap is the one that mattered — the
production scenario invokes structurally different tools back-to-back
on the same leased runspace.

**What I did:**

1. **Production-shape repro test.**
   `PoolHost_AlternatingDifferentScripts_LargePool_NoCrossInvokeLeak`
   in `OutOfProcessPoolHostIntegrationTests.cs` runs `Write-Output`
   (returns structured per-iteration sentinel) alternating with
   `Write-Verbose` (returns nothing) for 50 iterations at
   runspacePoolSize=10. After A: response must contain the current
   sentinel and NO prior sentinel. After B: response must equal "null"
   and contain NO prior sentinel.
2. **Result:** test PASSES on current main HEAD without any production
   code change. Cross-invoke `$r`-leak hypothesis is not what the user
   is observing.
3. **Defensive change applied anyway.** Both `oop-host.ps1` and
   `oop-host-pool.ps1` now call `AddScript($userScript, $true)`. With
   `useLocalScope=$true` the script body runs in a child scope of the
   runspace's default scope, so the per-invoke working variable `$r` is
   discarded on return instead of living at runspace scope where the
   next invoke on the same leased runspace could (in some future-edit
   exception path) observe it.

**First defensive attempt I had to back out:** wrapping the call site in
an inner `& { ... }` scriptblock as well broke
`HadErrorsDoesNotLeakAcrossInvokes`. With the inner scriptblock, a
`Get-ChildItem -Path missing -ErrorAction SilentlyContinue` no longer
surfaced `HadErrors=true` on the parent pipeline (`Streams.Error`
populated, boolean flipped). The single-layer `useLocalScope=$true`
change has none of that side effect.

**Honest disposition for the production symptom:** v0.12.2 lacks commit
6908917 ("fix(oop): clear per-invoke state so errors don't return prior
output"). That fix converts `hadErrors=true` into a thrown
`InvalidOperationException` that MCP marks `IsError=true` instead of
returning the partial pipeline output as a deceptive success. The
mechanism in production for the user-visible "same tenant payload from
a role-member tool" is the same partial-pipeline-output-before-error
pattern documented in round 1: `Assert-BamiTenantRoleMember` emits
tenant-shaped output internally (via its embedded `Assert-BamiTenantUser`
/ `Get-BamiTenantContext` call chain) before writing the role
non-terminating error; v0.12.2 returns that pipeline output as success.
**The user-facing fix is the existing 6908917 commit; deploying a
0.12.3 (or later) build that includes it is what resolves the report.**
The defensive scope change landed in this round is the structural
belt-and-suspenders against the adjacent `$r`-leak class.

**What this change does NOT fix:**

- User-authored modules setting their own cross-invoke state via
  `$global:` or `$script:` in their own module scope. The OOP framework
  cannot contain that from outside.
- The deployed v0.12.2 server's behavior. That requires a deployment of
  current main.

**Files changed (this round):**

- `PoshMcp.Server/PowerShell/OutOfProcess/oop-host.ps1` —
  `AddScript($userScript, $true)`; comment block.
- `PoshMcp.Server/PowerShell/OutOfProcess/oop-host-pool.ps1` — same.
- `PoshMcp.Tests/Integration/OutOfProcessPoolHostIntegrationTests.cs` —
  new test `PoolHost_AlternatingDifferentScripts_LargePool_NoCrossInvokeLeak`.

**Test status:** Category=OutOfProcess: 47 passed, 0 failed, 0 skipped.

**Commit:** `e1c923e fix(oop): defensive per-invoke scope for user
script`. Pushed to main.

**Don't regress:**

- Do NOT also wrap the user script body in an inner `& { ... }`
  scriptblock. That breaks non-terminating-error propagation to the
  parent pipeline's `HadErrors` flag. The single
  `useLocalScope=$true` is the right shape.
- Do NOT use `Get-Variable -Name r -ErrorAction Ignore` as a peek
  primitive in tests. `Get-Variable` on a missing name still flips
  `HadErrors=true` and (post-6908917) the C# layer surfaces that as an
  exception. The 2026-05-12 round-2 history already captured this trap;
  I hit it again in round 3 while writing a variable-peek test and
  removed that test in favor of the alternating-scripts production-
  shape repro.

---

### 2026-05-12 (revisit): OOP cross-invoke leak — could NOT reproduce; prior diagnosis acknowledged as incomplete

**Requested by:** Steven Murawski. User explicitly rejected the prior diagnosis below
("YOU GOT THE PRIOR DIAGNOSIS WRONG... The actual observed behavior is: 'When the command
was run the first time, it returned null. After other commands were run, it started
returning their output when being rerun.' That is definitively cross-invocation state.")
and asked me to find and fix the real leak — reproduce FIRST, no speculative fix.

**Acknowledgment of prior misdiagnosis:** The 2026-05-12 entry below correctly identified
*one* real bug (`hadErrors=true` was being logged but the partial output returned anyway),
but it conflated that with the user's observation. The user's report describes a *time-
separated* state leak: first call returned null, *then after other calls* the same command
started returning their output. That pattern is not explained by "current invoke's
pre-error pipeline output" — it requires actual state surviving between separate invokes.

**Reproduction attempts (all PASS on current main HEAD 273bc3b — no leak observed):**
1. `Invoke_TwoDifferentSuccessfulCommands_SecondDoesNotReturnFirstOutput` (existed) — Single host
2. `Invoke_ErrorInDifferentCommandAfterSuccess_DoesNotReturnFirstOutput` (existed) — Single host
3. `PoolHost_TwoDifferentSuccessfulCommands_SecondDoesNotReturnFirstOutput` (existed) — Pool host
4. `PoolHost_ErrorInDifferentCommandAfterSuccess_DoesNotReturnFirstOutput` (existed) — Pool host
5. **NEW** `Invoke_EmptyCommand_AfterPriorOutput_DoesNotReturnPriorOutput` — exactly matches
   user's sequence: empty-returning cmd (Write-Verbose) → producing cmd (Get-Item) → rerun
   empty cmd, assert "null". Single host. PASS.
6. **NEW** `PoolHost_EmptyCommand_AfterPriorOutput_DoesNotReturnPriorOutput` — same pattern
   on Pool host (pool size 2, 6 iterations to exercise every runspace). PASS.

False starts during repro design (lessons):
- Custom function via startup script does NOT land in `$script:SharedRunspace` (startup
  scripts execute in the OOP host's own runspace) — separate gap worth filing as its own
  issue, not the leak being investigated.
- `Get-Variable -ValueOnly -ErrorAction Ignore` on a nonexistent name STILL sets
  `HadErrors=true`, which (correctly) trips the 2026-05-12 fix and throws "OOP error:
  ... (discarded 4-char output)". The 4 chars are the literal string `"null"` returned
  by the user script. The hadErrors→throw path is working as designed.
- Settled on `Write-Verbose -Message 'x'` as the clean empty-returning vehicle: built-in,
  writes to verbose stream (suppressed), returns nothing, does NOT set HadErrors.

**Architectural review (no leak surface found):**
- `oop-host.ps1` `$script:` vars: only `Dispatcher`, `SharedRunspace`, `CommonParameters`,
  `Cancellations`. No output accumulator. User script uses a *local* `$r` overwritten
  every invoke; `$Error.Clear()` runs at the top of every invoke.
- `oop-host-pool.ps1` mirrors the pattern. `$script:Pool`, `$script:Dispatcher`, host UI
  routes to stderr. Same per-invoke fresh `[powershell]` with `RunspacePool`.
- `OutOfProcessHost.cs`: `_pending` is `ConcurrentDictionary<string, TCS>` keyed on
  `Guid.NewGuid()` per request, removed on completion. No keying on command name.
- `OutOfProcessCommandExecutor.cs`: `_cachedSchemas` is for `discover` output only.
  `_lastSetupConfig` is for restart replay, never returned to callers.
- `OutOfProcessSubprocessPool.cs`: same per-request-ID pattern.
- `OutOfProcessToolAssemblyGenerator.cs`: no per-tool result cache.
- No mutable static fields in the OOP module (grep verified).

**Disposition:** I cannot reproduce a framework-level cross-invocation output leak on
current main. All 46 `Category=OutOfProcess` tests pass, including the 2 new regression
guards. Per Steven's directive ("do NOT push a speculative fix"), I am NOT modifying
production code. The 2 new tests are committed as permanent regression guards — they will
fail loudly if a real cross-invoke leak is ever introduced.

**Remaining hypotheses I did NOT chase** (any of these could explain Steven's observation
without a framework bug; would require Steven's exact command list to verify):
- The reported sequence used an AdvocacyBami module command whose own internal state
  (module-scoped `$script:` vars in user-authored modules) leaked across calls. The OOP
  framework cannot detect or prevent that.
- The reported sequence involved restart/reconnect of the OOP subprocess in between
  calls, where `_lastSetupConfig` replay or a stale pending response could matter. I
  did not exercise the subprocess-death+restart path with overlapping calls.
- A bug in a specific tool-generation code path for a specific parameter shape (e.g.,
  PSCredential, complex pipeline-bound parameters) that I didn't exercise.

**Files changed (this session):**
- `PoshMcp.Tests/Integration/OutOfProcessIntegrationTests.cs` — added regression test
- `PoshMcp.Tests/Integration/OutOfProcessPoolHostIntegrationTests.cs` — added regression test
- NO production code modified.

---

### 2026-05-12: OOP invoke — hadErrors was logged but not propagated to MCP

**Bug**: A user invoked `assert_tenant_role_member` with a bad role and got back what looked
like the *prior* `assert_tenant_user` payload, with MCP `IsError=false`. Server log showed
`warn: ... reported errors. Output: {prior-looking JSON}` and `IsError = False`.

**Root cause** (NOT actual cross-invoke leak):
- Each invoke uses a fresh `[powershell]` instance, so streams are not shared across invokes
  and `$Error.Clear()` already runs at the top of the user script (#189 was a prior fix).
- The real bug was in `OutOfProcessCommandExecutor.InvokeAsync` (and the pool mirror in
  `OutOfProcessSubprocessPool.InvokeAsync`): when the response carried `hadErrors=true`,
  the executor **logged a warning and returned the partial output anyway**. The MCP
  framework can only mark a tool call `IsError=true` if the generated method throws —
  returning a normal string is always treated as success.
- The "prior payload" the user saw was actually the *current* command's partial pipeline
  output before its own non-terminating error. AdvocacyBami's `Assert-BamiTenantRoleMember`
  internally calls `Assert-BamiTenantUser` (which emits the user object) and then writes
  a non-terminating error for the bad role. With `$r = & $Name @Splat`, `$r` ends up
  holding the user assertion object, which then got JSON-serialized as "success".

**Fix location**:
- `PoshMcp.Server/PowerShell/OutOfProcess/OutOfProcessCommandExecutor.cs` — `InvokeAsync`
  now throws `InvalidOperationException` with message `OOP error: command '{X}' reported
  {N} error(s): {joined errors}` whenever `hadErrors=true && cancelled=false`. Added
  private helper `ExtractErrorMessage`.
- `PoshMcp.Server/PowerShell/OutOfProcess/OutOfProcessSubprocessPool.cs` — same change
  in the pool's `InvokeAsync`, with helper `ExtractInvokeErrorMessage`. Pool mode and
  single mode now behave identically on hadErrors.
- `cancelled=true` is intentionally excluded so cooperative cancellation does not get
  reclassified as a tool failure.

**Message format preserves the existing "OOP error:" prefix** used by `OutOfProcessHost`
for terminating errors. That means existing test catches like
`ex.Message.Contains("OOP error")` (e.g. the `Get-AzContext` path in
`OutOfProcessModuleTests`) keep working without modification.

**Regression test**:
`PoshMcp.Tests/Integration/OutOfProcessIntegrationTests.cs::Invoke_WithErrorAfterSuccess_DoesNotReturnPreviousOutput`
runs a successful `Get-Item` against a marker directory, then a failing `Get-Item`
against a non-existent path with `ErrorAction=Continue`, and asserts the second invoke:
(1) throws `InvalidOperationException`, (2) message contains `"OOP error"` and the
failing command name, (3) message does NOT contain the unique marker token from the
prior successful output.

**Test status**: 18/18 in `OutOfProcessIntegrationTests`, 40/40 with `Category=OutOfProcess`.

**Don't regress**:
- A command can legitimately write to `$Error` non-terminally and still produce output
  the user might care about. Post-fix, that case becomes `IsError=true`. This is the
  intended contract: MCP clients must see error state instead of silently-success
  output. If a future caller wants a tolerant variant, add a separate API rather than
  weakening this gate.
- Do NOT throw when `cancelled=true`. Cancellation already has its own surface and
  reclassifying it as an error would break the cancel-in-flight path.

## Recent Work (2026-05-01 — CURRENT SESSION)

### Diagnosis: AggregateError — Failed to Fetch Authorization Server Metadata
**Date:** 2026-05-01  
**Status:** Diagnosis complete — fix NOT yet applied  
**Report:** `.squad/decisions/inbox/bender-authserver-metadata-diagnosis.md`

- **Task**: Diagnose why VS Code reports `AggregateError: Failed to fetch authorization server metadata from all attempted URLs` after the v0.9.4 fix for `WWW-Authenticate`
- **Findings**:
  - Root cause: `authorization_servers` in the PRM contains `https://login.microsoftonline.com/{tenant}` (missing `/v2.0`). VS Code fetches the discovery doc at `{url}/.well-known/openid-configuration`, gets back a v1.0 document with `issuer: https://sts.windows.net/{tenant}/`. Per RFC 8414 §3, issuer MUST match the URL used to discover it — `sts.windows.net` ≠ `login.microsoftonline.com` → VS Code rejects the document → AggregateError.
  - Secondary: PRM response contains duplicated entries (2× `authorization_servers`, 2× `scopes_supported`, 3× `bearer_methods_supported`). Consistent with config being applied twice; the count of 3 for BearerMethods (which has a model-level default of `["header"]`) vs 2 for others (empty default) confirms this.
  - The config file (`appsettings.json`) already has the correct `/v2.0` form in `ValidIssuers` — just missing it in `AuthorizationServers`.
- **Fix required**:
  1. Change `AuthorizationServers` in the AdvocacyBami `appsettings.json` from `login.microsoftonline.com/{tenant}` to `login.microsoftonline.com/{tenant}/v2.0`
  2. Investigate why configuration arrays are being accumulated (duplicated) — likely double-registration of the config JSON file in the provider pipeline

---

### Diagnosis: VS Code Auth Redirect to PoshMcp `/authorize`
**Date:** 2026-05-01  
**Status:** Diagnosis complete — awaiting fix approval  
**Report:** `.squad/decisions/inbox/bender-vscode-auth-redirect-diagnosis.md`

- **Task**: Diagnose why VS Code redirects authentication to PoshMcp's own `/authorize` endpoint instead of Entra ID
- **Findings**:
  - Root cause: `AuthenticationServiceExtensions.cs` does not configure `JwtBearerEvents.OnChallenge`, so JwtBearer 401 responses emit `WWW-Authenticate: Bearer` without the RFC 9728 `resource_metadata` parameter. VS Code can't discover the PRM and falls back to treating PoshMcp as the auth server → constructs `{serverBaseUrl}/authorize`.
  - Secondary bug: `ApiKeyAuthenticationHandler.HandleChallengeAsync` constructs the `resource_metadata` URL from `ProtectedResource.Resource` (an `api://` URI) instead of the server's actual HTTP base URL. Produces an invalid non-HTTP URL.
  - The `client_id=80939099-d811-4488-8333-83eb0409ed53` in the redirect is the PoshMcp App Registration's Application ID — confirms VS Code is in fallback mode (extracted GUID from PRM's `resource` field).
  - The PRM content and `authorization_servers` configuration are correct; only the 401 challenge header is missing.
- **Fix required**:
  1. Add `JwtBearerEvents.OnChallenge` in `AuthenticationServiceExtensions.cs` to emit `WWW-Authenticate: Bearer resource_metadata="{request.Scheme}://{request.Host}/.well-known/oauth-protected-resource"`
  2. Fix `ApiKeyAuthenticationHandler.HandleChallengeAsync` to use `Request.Scheme + Request.Host` for the metadata URL

## Learnings

- **RFC 9728 `resource_metadata` is required in `WWW-Authenticate`** — Without `resource_metadata="{url}"` in the 401 `WWW-Authenticate` header, VS Code's MCP OAuth client cannot discover the PRM. It falls back to treating the resource server as the authorization server and appends `/authorize` to the base URL.
- **`ProtectedResource.Resource` is an `api://` URI, not an HTTP URL** — Never use it to construct HTTP endpoint URLs (like the PRM metadata URL). Always derive the server base URL from `HttpContext.Request.Scheme + Request.Host`.
- **VS Code fallback `client_id` behavior** — When VS Code can't resolve the real auth server, it extracts the GUID from the PRM's `resource` field (e.g., `api://80939099-...`) and uses it as the OAuth `client_id` in the fallback authorization request. This GUID is the App Registration's Application ID, NOT VS Code's own client_id (`aebc6443-996d-45c2-90f0-388ff96faa56`).
- **ApiKey scheme ≠ JwtBearer scheme for challenge handling** — Adding `WWW-Authenticate` logic to `ApiKeyAuthenticationHandler` does NOT cover the JwtBearer scheme. Each scheme must independently configure its challenge response.
- **`context.HandleResponse()` is required when overriding JwtBearer challenge** — Calling `context.HandleResponse()` in `OnChallenge` suppresses the default JwtBearer challenge pipeline so you can set your own `StatusCode` and `WWW-Authenticate` header. Without it, ASP.NET Core writes a second `WWW-Authenticate: Bearer` header after your custom one, producing a malformed multi-value header.
- **Entra ID `authorization_servers` must include `/v2.0`** — The PRM `authorization_servers` value must be `https://login.microsoftonline.com/{tenant}/v2.0`, NOT the bare tenant URL. Without `/v2.0`, VS Code discovers the v1.0 OIDC endpoint, which returns `issuer: https://sts.windows.net/{tenant}/`. That issuer does not match the authorization_server URL, so VS Code rejects the discovery document per RFC 8414 §3 → AggregateError. With `/v2.0`, the issuer is `https://login.microsoftonline.com/{tenant}/v2.0` which matches exactly.
- **ASP.NET Core config array duplication** — If the same JSON config file is registered as a configuration provider more than once (e.g., once by the default `WebApplication.CreateBuilder()` pipeline and again by a custom config loader), array values are accumulated (not replaced). Properties with C# model-level defaults (e.g., `new() { "header" }`) accumulate one extra copy. Check for double `AddJsonFile(path)` calls in the configuration pipeline when PRM arrays contain unexpected duplicates.

---

## Recent Work (2026-04-20)

## Recent Work (2026-05-11 — CURRENT SESSION)

### PR #211: Test Fixtures for Proxy & High-Parameter Method Schema Validation
**Date:** 2026-05-11
**Status:** Complete (committed, awaiting Fry for integration test implementation)
**Branch:** `fix/winpscompat-proxy-parameters`

- Created new `PoshMcp.Tests/Fixtures/` folder with three files:
  - `ProxyTestFixtures.cs` — Static factory methods for synthetic commands:
    - `CreateProxyStyledCommand()` → CommandInfo with ImplicitRemoting marker, object params
    - `CreateHighParameterCommand()` → CommandInfo with 17 params (triggers cached delegate path in McpToolFactoryV2)
    - `CreateObjectParameterCommand()` → CommandInfo with [object] params on proxy module
  - `Pr211IntegrationFixtureSetup.cs` — Test infrastructure class:
    - `GetFixtureCommands()` → Creates and caches all three fixture commands
    - `ValidateFixtureSchemas()` → Helper to validate generated MCP tool schemas
    - Collection fixture definition for Xunit shared setup
  - `README.md` — Documentation of fixture usage for Fry (test specialist)

- Fixtures address Farnsworth's finding: unit tests validated helper behavior, but NOT end-to-end schema generation.
  - Fixtures are ready to pass directly to McpToolFactoryV2 for schema generation
  - No mocking/stubbing — real CommandInfo objects created via PowerShell
  - Designed for integration test to validate schema parameter types are correct (object→string for proxies, etc.)

- Build validation:
  - Fixtures compile clean (0 errors, 0 warnings in fixture code)
  - Committed: `test(#211): Add fixtures for proxy & >16-param method-generation tests`

**Files added:**
- `PoshMcp.Tests/Fixtures/ProxyTestFixtures.cs` (289 lines)
- `PoshMcp.Tests/Fixtures/Pr211IntegrationFixtureSetup.cs` (167 lines)
- `PoshMcp.Tests/Fixtures/README.md` (125 lines)

## Learnings (2026-05-11)

- **PowerShell fixture creation pattern**: Use `New-Module -ScriptBlock { ... } | Export-ModuleMember` to create synthetic PSModuleInfo objects. The `Invoke()` result wraps output in PSObjects — use `.BaseObject` to unwrap and `OfType<T>()` to filter by actual type (not PSObject).
- **Proxy module structure**: Export-PSSession creates modules with:
  - `PrivateData["ImplicitRemoting"] = true` (primary signal)
  - `Description` starting with "Implicit remoting for ..."
  - `RootModule` matching pattern `remoteIpMoProxy_*_*.psm1`
  - All parameters typed as `[object]` with no Mandatory flag
- **Read-only PSModuleInfo properties**: Properties like `RootModule` and `ModuleType` are not publicly settable. Access backing fields via reflection (`_propertyName` or `_lowercaseFirstLetter` pattern) to mutate in test fixtures.
- **Test infrastructure layering**: Separate static factories (`ProxyTestFixtures`) from test runner infrastructure (`Pr211IntegrationFixtureSetup`). Factories create objects, runner handles caching, validation, and Xunit collection fixture protocol.
- **End-to-end validation path**: Unit tests verify individual helper behavior; integration tests validate that helpers compose correctly through the full MCP tool schema generation pipeline. Fixtures bridge the gap by providing realistic CommandInfo inputs that exercise both code paths (proxy detection + high-parameter delegate emit).
- **File structure**: New test fixtures go in `PoshMcp.Tests/Fixtures/` (parallel to `Unit/`, `Integration/`, `Functional/`). Include README documenting usage for teammates who will consume the fixtures.
- **Xunit analyzer**: Prefer `Assert.Contains(item, collection)` or `Assert.NotEmpty(collection)` with LINQ filters rather than `Assert.True(collection.Any(...))` — the analyzer catches verbose assertion patterns and suggests idiomatic xUnit.

---

### Fix: CWE-117 log forging — `LogSanitizer` + call-site scrubbing
**Date:** 2026-05-06
**Status:** Complete (committed, not pushed — coordinator orchestrates push)
**Branch:** `squad/security-codeql-cleanup`

- Added `PoshMcp.Server/Observability/LogSanitizer.cs` — `Scrub(string?)` static helper.
  - Replaces CR/LF with visible escape sequences (`\\r`, `\\n`); other ASCII C0 controls and DEL → `\\xNN`; TAB → `\\t`.
  - Truncates at 2048 chars with `…(truncated)` suffix.
  - Null → `"<null>"`.
  - Allocation-conscious: returns input unchanged when no escapes needed and within length.
- Applied at call sites only (Farnsworth's call: CodeQL `cs/log-forging` is call-site sink-tracked, so a Serilog enricher would not close the alerts).
  - `LoggerExtensions.BeginCorrelationScope` — scrub `OperationName` before it enters scope.
  - `AuthenticationServiceExtensions` `OnMessageReceived` — scrub `context.Request.Path` (attacker-controlled).
  - `PowerShellAssemblyGenerator`:
    - Introduced `safeCommandName` local once at top of `ExecutePowerShellCommandTyped`; replaced all log/metric sink uses of `commandName` (≈25 sites) with the sanitized form. Raw `commandName` still used at `ps.AddCommand(...)`/`OperationContext.BeginOperation(...)` and in the JSON error responses returned to MCP callers.
    - Scrubbed `paramInfo.Name`, `paramValue`, `convertedValue`, PowerShell error stream messages, exception messages.
    - Generation-time logs (`GenerateAssembly`, `GenerateMethodForCommand`) — scrubbed `command.Name`, `commandInfo.Name`, `parameterSet.Name`, `ex.Message`. Also converted several `$"..."` interpolated log calls to structured templates while wrapping tainted args.
    - `HandlePowerShellErrors`, `InvokePowerShellSafe`, `InvokePowerShellSafeAsync`, `ConvertToJson` — scrubbed `operationName` (interpolates `commandName` at call sites) and PS error messages at every log sink.
- Added 9 focused tests at `PoshMcp.Tests/Unit/Observability/LogSanitizerTests.cs`. All pass.
- Build clean (0 errors; 19 pre-existing warnings — no new warnings introduced).
- Full Unit test slice: 452 passed, 0 failed.

**Files modified:**
- `PoshMcp.Server/Observability/LogSanitizer.cs` (new)
- `PoshMcp.Server/Observability/LoggerExtensions.cs`
- `PoshMcp.Server/Authentication/AuthenticationServiceExtensions.cs`
- `PoshMcp.Server/PowerShell/PowerShellAssemblyGenerator.cs`
- `PoshMcp.Tests/Unit/Observability/LogSanitizerTests.cs` (new)

## Learnings

- **CWE-117 / `cs/log-forging`** — CodeQL's taint analysis tracks **call-site sinks**, not whether a Serilog enricher exists in the pipeline. Centralized enrichers do not close the alerts; call-site `Scrub()` does. (Farnsworth call.)
- When a log statement uses interpolated `$"..."` strings, prefer converting to structured templates (`"... {Foo} ..."` + arg) when adding scrub wrappers — it's both safer and gives observability platforms structured fields. Keep the change minimal: don't restructure messages that don't need scrubbing.
- For methods with many log calls referencing the same tainted value (here: `commandName` ≈25× in `ExecutePowerShellCommandTyped`), introduce one `safeFoo` local at the method top rather than wrapping `Scrub(...)` 25 times. Easier to read, single allocation, and you can document the sanitization rationale once.
- Distinguish carefully between log sinks and operational uses of the same value. `ps.AddCommand(commandName)` MUST stay raw — escaping the cmdlet name would break invocation. Only sanitize where the value flows into a logger/metric tag/scope.

---

## Recent Work (2026-05-03 — PRIOR SESSION)

### Fix: RequiredRoles OR Semantics
**Date:** 2026-05-03
**Status:** Complete
### Auth enforcement bypass despite Enabled: true (2026-05-01)

- **Root cause:** `WebApplicationBuilder`'s `ConfigurationManager` starts with the container's baked-in `appsettings.json` (`Authentication.Enabled: false`). Even though the user's custom `PoshMcp/appsettings.json` (with `Enabled: true`) is added later via `builder.Configuration.AddJsonFile(...)`, the default `appsettings.json` was winning over the custom file, causing `authConfigValue.Enabled = false` at line 1800 and `IOptions<AuthenticationConfiguration>.Value.Enabled = false` at middleware setup time.
- **Evidence of the bug:** `/.well-known/oauth-protected-resource` returned 404 (endpoint not mapped because `config.Enabled = false`). No `WWW-Authenticate` header on unauthenticated requests. `ToolListAuthorizationFilter` returned ALL tools to unauthenticated user (filter short-circuits when `authConfig.Enabled = false`).
- **Misleading diagnostic:** The `get-configuration-troubleshooting` and `get-configuration-guidance` tools showed `enabled: true` — but they read from the config FILE directly via `ConfigurationLoader.BuildRootConfiguration(configurationPath)`, not from `IOptions`. The DI runtime had `Enabled: false` while the diagnostic tools showed `true`.
- **Why the v0.9.2 IOptions fix didn't fix this:** That fix addressed a different case: `Enabled: false` → IOptions always showed the default `false` even when no guard was hit. The *current* bug is: even when `Enabled: true` in the custom file, `builder.Configuration` returns `false` due to the baked-in base `appsettings.json` winning the precedence battle.
- **Fix (this session):** Changed `RunHttpTransportServerAsync` to build a dedicated `authRootConfig` via `ConfigurationLoader.BuildRootConfiguration(finalConfigPath, reloadOnChange: false)` — reading ONLY from the custom file + env vars, same as diagnostic tools. Three call sites updated:
  - `authConfigValue` (line ~1806): now reads from `authRootConfig` instead of `builder.Configuration`
  - `AddOptions<T>().Configure(opts => authRootConfig...)`: binds IOptions directly from `authRootConfig`
  - `AddPoshMcpAuthentication(authRootConfig)`: JWT Bearer and McpAccess policy now configured from correct source
- **Key rule:** Never use `WebApplicationBuilder.Configuration` as the source for security-gate decisions when a custom config file is involved. The `WebApplicationBuilder` default config chain always includes the baked-in `appsettings.json` which may have different (and unsafe) defaults. Use `ConfigurationLoader.BuildRootConfiguration(configPath)` for auth configuration — it reads only what the user explicitly configured.
- **Files modified:** `PoshMcp.Server/Program.cs`

### ConfigureCorsForMcp also used builder.Configuration (2026-05-01)

- **Discovery:** After applying the main auth fix (authRootConfig for IOptions/AddPoshMcpAuthentication/authConfigValue), `ConfigureCorsForMcp` still read from `builder.Configuration`. This would cause CORS to silently open up (`AllowAnyOrigin`) even when auth is enabled, because `authConfig.Enabled` resolved to `false` from the baked-in base appsettings.
- **Fix:** Changed method signature from `ConfigureCorsForMcp(WebApplicationBuilder builder)` to `ConfigureCorsForMcp(WebApplicationBuilder builder, IConfigurationRoot authRootConfig)`, replacing `builder.Configuration.GetSection("Authentication")` with `authRootConfig.GetSection("Authentication")`. Updated the call site at line ~1781 to pass `authRootConfig`.
- **Pattern:** After applying an auth config source fix, grep ALL call sites for `builder.Configuration.GetSection("Authentication")` — any remaining uses are potential auth bypasses. The `authRootConfig` should be the single source of truth for all auth-gated decisions in the server setup method.
- **Commit:** 351c42c
- **Files modified:** `PoshMcp.Server/Program.cs`


- Changed `HasRequiredRoles` in `AuthorizationHelpers.cs` from `.All()` to `.Any()`
- Fixes AND/OR mismatch: users need any one role, not every role
- Both `ToolAuthorizationFilter` and `ToolListAuthorizationFilter` inherit the fix automatically
- Build verified clean; committed as `fix(auth): use OR semantics for RequiredRoles checks`

**Files modified:**
- `PoshMcp.Server/Authentication/AuthorizationHelpers.cs`

## Learnings

- Entra app roles are granted one-at-a-time; AND semantics on role lists are unreachable in practice
- ASP.NET Core's `policy.RequireRole(string[])` uses OR — always match that behavior in custom helpers
- Small one-liner fixes can have wide blast radius; always check every caller before changing LINQ predicates

---

### Feature: Claims Mapping Fix + Token Proxy Logging
**Date:** 2026-05-03
**Status:** Complete

- Fixed MapInboundClaims pipeline to correctly transform inbound OAuth claims
- Ensured scope fields properly populated from claim paths
- Fixed RequiredScopes validation for authority/issuer handling
- Updated DoctorReport diagnostic output to reflect fixes
- Enhanced token proxy logging for OAuth flow traceability
- All integration tests passing

**Files modified:**
- OAuth proxy claim transformation logic
- RequiredScopes validation code
- DoctorReport diagnostic output
- Token proxy logging configuration

## Recent Work (2026-05-02 — PRIOR SESSION)

### Feature: Token diagnostics + configurable IdleTimeout (v0.9.12 prep)
**Date:** 2026-05-02
**Status:** Complete

#### 1. Token Diagnostics in `/token` proxy
- Upgraded `OAuthProxyEndpoints.cs` `/token` handler with diagnostic logging
- `LogInformation` on 2xx: logs status code and Content-Type (no token body)
- `LogWarning` on non-2xx: logs status code, Content-Type, and full response body (error JSON)
- `LogDebug` for request field names only (excludes `resource`; field names only, no values)
- Removed old single-line Debug log; replaced with structured conditional logging

#### 2. Configurable `IdleSessionTimeoutSeconds`
- Created `PoshMcp.Server/McpServerConfiguration.cs` with `McpServerConfiguration` class (namespace `PoshMcp`)
- Added `"McpServer": { "IdleSessionTimeoutSeconds": 60 }` to `appsettings.json`
- Updated `HttpServerHost.cs`: reads `McpServer` section via `authRootConfig`, passes `IdleTimeout` via `WithHttpTransport(opts => ...)` delegate overload
- Added `using ModelContextProtocol.AspNetCore;` to `HttpServerHost.cs`

**Key findings:**
- `WithHttpTransport` in `ModelContextProtocol.AspNetCore` 1.2.0 DOES have an overload accepting `Action<HttpServerTransportOptions>` — confirmed via package XML docs
- `HttpServerTransportOptions.IdleTimeout` is a `TimeSpan` property
- Build succeeded: 0 errors, 19 pre-existing warnings (no new warnings introduced)

**Files modified:**
- `PoshMcp.Server/Authentication/OAuthProxyEndpoints.cs` — enhanced /token logging
- `PoshMcp.Server/Server/HttpServerHost.cs` — IdleTimeout wiring + using
- `PoshMcp.Server/appsettings.json` — added McpServer section
- `PoshMcp.Server/McpServerConfiguration.cs` — new file (created)

### Diagnostic: Auth challenge/redirect on no-token MCP connect
**Date:** 2026-05-02
**Status:** In Progress (spawned 15:36:07)
**Focus:** Investigating why unauthenticated MCP clients not receiving auth challenge or redirect
**Session log:** `.squad/log/2026-05-02T15-36-07-auth-challenge-debug.md`

### Bug Fix: Entra v1.0 Authority causing JWT signature validation failure
**Date:** 2026-05-02
**Status:** Complete
**Commits:**
- `fix: use Entra v2.0 authority for JWT Bearer` (AdvocacyBami repo)
- `fix: warn when Entra Authority is v1.0 but ValidIssuers specifies v2.0` (poshmcp repo)

- **Root cause**: `Authority` in AdvocacyBami `appsettings.json` was `https://login.microsoftonline.com/{tenant}` (v1.0). This caused JWT Bearer middleware to fetch the v1.0 OIDC discovery doc and v1.0 JWKS. VS Code obtained tokens via the v2.0 endpoint, which are signed with v2.0 JWKS keys — keys absent from the v1.0 JWKS. Result: `SecurityTokenSignatureKeyNotFoundException`, 401, `DenyAnonymousAuthorizationRequirement` error.
- **Fix 1 (AdvocacyBami)**: Changed `Authority` to `https://login.microsoftonline.com/{tenant}/v2.0` so the v2.0 OIDC discovery doc (and v2.0 JWKS) are fetched.
- **Fix 2 (PoshMcp)**: Added a startup `Console.Error.WriteLine` warning in `AuthenticationServiceExtensions.cs` that fires when Authority is Entra v1.0 but `ValidIssuers` contains a v2.0 issuer — helps operators catch this misconfiguration early.
- **Build note**: `dotnet build --no-incremental` required due to pre-existing MSBuild "Question build" cache issue; build succeeded with 0 CS errors.

**Files modified:**
- `C:\Users\stmuraws\source\emu\gim-home\AdvocacyBami\appsettings.json` — Authority += `/v2.0`
- `PoshMcp.Server/Authentication/AuthenticationServiceExtensions.cs` — added `using System;` + startup warning block

### Feature: /authorize proxy redirect endpoint (v0.9.11)
**Date:** 2026-05-02
**Status:** Complete
**Commits:** `feat(auth): add /authorize proxy redirect endpoint for VS Code OAuth`


---
*Further trimmed to 100 lines on 2026-05-05 by Scribe (15KB gate). Full record in `history-archive.md`.*

## 2026-05-06: New milestone-tagged issues assigned

Milestone #5 (Spec 004 - Out-of-Process PowerShell Execution) was created. You have issues assigned via squad:* labels:
- Bender: #190 (extract OutOfProcessHost), #192 (Option B - process pool prototype, blocked by #190)
- Fry: #193 (benchmark harness infra), #194 (wire harness to executors, blocked by #191/#192/#193)
- Farnsworth: #196 (adopt the winner, blocked by #195)

Check the issue body for plan reference and dependency chain before starting.

### 2026-05-07: v0.11.0 release shipped (cross-agent note from Scribe)
Your work landed in v0.11.0 (csproj 0.10.0 → 0.11.0, CHANGELOG entry, release notes at docs/release-notes/0.11.0.md). The release narrative credits the OOP maturity wave: Pool default flip (#196/#208), cancellation propagation across all modes (#207), benchmarks harness + findings (#193/#194/#195/#205), OOP host extraction (#190/#198), bug fixes (#203/#189), CWE-117 log-injection hardening, minimum workflow permissions, and SECURITY.md. Tag/push deferred to Steven.
1. Add `<EmbeddedResource>` entries in `.csproj` with `Link` paths using backslash separators to control the manifest resource name:
   ```xml
   <EmbeddedResource Include="..\Dockerfile" Link="Dockerfiles\Dockerfile" />
   ```

2. The manifest name is: `{AssemblyName}.{Link path with backslashes replaced by dots}`.  
   **Important:** The prefix is the *assembly name* (`<AssemblyName>` or project name), not the namespace. For this project, the assembly is `PoshMcp`, so the resource is `PoshMcp.Dockerfiles.Dockerfile` — NOT `PoshMcp.Server.Dockerfiles.Dockerfile`.

3. Read via `Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)`.

4. When the resource isn't found (e.g., file wasn't embedded, or path was custom), fall back to `File.ReadAllText()` so local dev still works.

5. Skip disk-existence checks (`File.Exists`) for paths that are satisfied by embedded resources — in this case the `--generate-dockerfile` flow.

### `--generate-dockerfile` default corrected to "custom" (fixed current session)

**What was wrong:** The `build` command handler had:

```csharp
var buildType = string.IsNullOrWhiteSpace(type)
    ? (generateDockerfile ? "base" : "custom")
    : type.ToLowerInvariant();
```

This meant `poshmcp build --generate-dockerfile` defaulted to `buildType = "base"`, which maps
to the repo root `Dockerfile` — the file for building PoshMcp from source. That is the wrong
template for users; they want `examples/Dockerfile.user`, which extends the published base image.

**How it was fixed:** Both paths (with and without `--generate-dockerfile`) now default to `"custom"`:

```csharp
var buildType = string.IsNullOrWhiteSpace(type)
    ? "custom"
    : type.ToLowerInvariant();
```

Users who explicitly want the source-build Dockerfile can still pass `--type base`.

**Also updated:** `examples/Dockerfile.user` — clarified that `install-modules.ps1` must be
downloaded from the repo, and that the `COPY appsettings.json` line is a placeholder the user
should update to their own path (removed the repo-internal `examples/appsettings.basic.json` path).

- Added --appsettings to poshmcp build: injects COPY line into generated Dockerfile; for build mode stages file to CWD as poshmcp-appsettings.json, uses temp Dockerfile (.poshmcp-build.dockerfile), cleans up both temp files after build
- Fixed poshmcp build 'Dockerfile not found' — embedded resources bypass the disk check; always generate temp dockerfile from embedded resource so build works outside the poshmcp repo

### 2026-05-01T16:16:11Z - VS Code OAuth Redirect Fix - Release v0.9.4 (Bender contribution)

- Diagnosed VS Code OAuth redirect root cause: missing resource_metadata in WWW-Authenticate header
- Implemented Fix 1: JwtBearerEvents.OnChallenge in AuthenticationServiceExtensions.cs
- Implemented Fix 2: ApiKeyAuthenticationHandler metadata URL configuration
- All 574 tests passing (green build)
- Coordination: Worked with Amy (release engineering), Leela (docs), Fry (regression tests)
