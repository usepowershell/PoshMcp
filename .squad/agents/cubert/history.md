# Cubert — History

## Project Context

- **Project:** poshmcp
- **Description:** Model Context Protocol (MCP) server that dynamically transforms PowerShell scripts, cmdlets, and modules into secure, discoverable AI-consumable tools
- **Tech Stack:** .NET 10, C#, PowerShell SDK, OpenTelemetry, ASP.NET Core, xUnit
- **Primary User:** Steven Murawski
- **Joined:** 2026-05-05

## Learnings

### 2026-05-05: Fact-check of squad-story.md and squad-work-log.md
**Requested by:** Steven Murawski

**Method:** Verified each technical claim by reading the actual repo (file_search, grep_search, read_file). Counted `[Fact]/[Theory]` attributes for test counts. Confirmed file paths against current directory layout.

**Key findings:**
- **Systemic future-dating bug.** Both docs (and `.squad/decisions.md`, several agent histories) carry entries dated July 2026 while the current date is 2026-05-05. Filed `cubert-future-dated-entries.md` to the decisions inbox.
- **Story fabricates the `/health` endpoint JSON shape.** The codebase has no `runspacePoolSize`, `activeCommandCount`, or `lastCommandCompletedAt` fields. `PoshMcp.Server/Health/` contains only standard `IHealthCheck` implementations that return Microsoft's `HealthCheckResult`.
- **Wrong file paths in work-log.** `DockerRunner.cs` lives at `PoshMcp.Server/Cli/`, not `Infrastructure/`. `DiagnoseMissingCommands` and `ConfiguredFunctionStatus` live in `PoshMcp.Server/Diagnostics/DoctorService.cs`, not `Program.cs` — work was refactored after the entry was written.
- **Stale counts.** "18 decision entries" is significantly low; actual count is much higher (30+ top-level date headers). "478 integration tests" is implausible for this codebase. "11 DockerRunner tests" vs 16 `[Fact]/[Theory]` attributes — recount before publishing.
- **Story roster missing Cubert.** The article's roster table omits the Fact Checker even though I'm on the active squad in `.squad/team.md`.
- **Unverifiable external claims:** "700+ NuGet downloads" and "CVE-2026-40894" cannot be checked without web access. Both should be sourced or removed.

**Patterns worth remembering:**
- Always grep for symbol locations rather than trust path claims in narrative docs — refactors break path attributions.
- Test counts in prose are a frequent source of staleness; recount via `[Fact]/[Theory]` attribute count or `dotnet test` output before publishing.
- When sample JSON appears in docs, search the source for the field names. Fabricated samples almost always have field names that don't appear anywhere in the codebase.
- Future-dated entries usually indicate either an agent ignoring `CURRENT_DATETIME` or a clock-skew bug — check across multiple files to distinguish a one-off typo from a systemic issue.

**Recommended next agent:** Leela (owns docs).
