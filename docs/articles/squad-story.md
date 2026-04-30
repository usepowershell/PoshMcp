# The Squad Story: Building PoshMcp

## Prologue: The Mission

In March 2026, we set out to build something that PowerShell communities had been asking for: a practical way to expose PowerShell scripts, cmdlets, and modules as discoverable AI-consumable tools through the Model Context Protocol (MCP). Not as a theoretical exercise, but as a working, operational system that could run in production with confidence.

The challenge was substantial. MCP is a protocol designed for AI integration. PowerShell is a dynamic, stateful scripting language with complex execution semantics. Bridging them required solving problems across architecture, runtime behavior, testing, deployment, observability, and developer education—simultaneously.

We built a squad.

---

## Part I: The Team Takes Shape

### The Roster

Our squad formed around a single project goal: **deliver a practical PowerShell-first MCP server with secure transports, evolving auth, and production documentation.**

| Role | Name | Mission |
|------|------|---------|
| **Lead Architect** | Farnsworth | Design the dynamic PowerShell-to-MCP tooling model that exposes scripts and cmdlets as discoverable tools |
| **Backend Developer** | Bender | Drive a unified `poshmcp` entry point and transport flow so operators can run HTTP or stdio modes consistently |
| **PowerShell Expert** | Hermes | Provide deep PowerShell and runspace execution guidance that keeps tool execution reliable and maintainable |
| **DevOps & Cloud** | Amy | Advance observability and cloud operations practices, including OpenTelemetry-aware deployment and runtime diagnostics |
| **Test Engineer** | Fry | Raise testing quality with broader unit, functional, and integration coverage around MCP behavior and regressions |
| **Developer Advocate** | Leela | Improve docs and developer education so teams can onboard quickly and use configuration and security features correctly |
| **Session Logger** | Scribe | Strengthen decisions logging and project traceability so architectural and process changes stay clear over time |
| **Work Monitor** | Ralph | Improve work queue monitoring and issue tracking flow to keep execution visible and priorities actionable |

### What Each Team Member Brought

**Farnsworth**, our lead architect, understood that the core innovation had to be in the architecture itself. He designed a dynamic PowerShell-to-MCP tooling model—a system where PowerShell functions could be introspected at runtime and automatically transformed into clean MCP tool schemas. This wasn't just about wrapping commands; it was about creating a system where the tooling layer and the execution layer spoke the same language: structured, discoverable, JSON-based tool definitions. His architectural decisions became the foundation every other team member built upon.

**Bender** focused on the unglamorous but critical work of runtime implementation. He understood that architects design beautiful systems, but engineers have to make them actually run. He drove the unification of the `poshmcp` entry point—the canonical command-line interface that meant deployment operators and local developers experienced the same tool, the same interface, whether running HTTP or stdio modes. He extracted Docker build logic, refined JSON serialization patterns, and ensured that the system felt cohesive from the operator's perspective.

**Hermes** was our PowerShell specialist. He understood that PowerShell is stateful, that runspaces have lifecycle constraints, that the dynamic nature of the language made serialization harder and that some types simply cannot be represented in JSON. His work didn't always produce shiny new features. Instead, he diagnosed why `Get-Process` would hang when called via MCP (answer: certain properties on the Process object make Win32 API calls that can block indefinitely), he designed property-set discovery to let us surface only the properties that matter to AI clients, and he identified which parameter types we had to filter out from the schema because they can't be serialized. His contributions kept us from shipping something that worked 95% of the time and broke catastrophically on edge cases.

**Amy** owned operational reliability. While the rest of the team was building the server, Amy was thinking about how this thing would run in production. She designed the Azure infrastructure scaffolding, integrated OpenTelemetry observability, defined how environment variables would map to configuration, and ensured that cloud deployment teams had clear paths to success. She also enforced a critical principle: that infrastructure code and deployment scripts are not decoration—they are the implementation. Her work with Bicep and PowerShell deployment scripts became canonical; when other scripts diverged from her defaults, she corrected them. The rule: infrastructure code is source of truth; scripts are wrappers.

**Fry** represented quality and confidence. As our test engineer, he didn't just write tests—he asked the question every engineer dreads: "What happens when this assumption breaks?" He built comprehensive unit tests for Docker build arguments. He created integration tests for Application Insights graceful degradation. He rewrote 12 failing doctor output tests to match the new spec structure, not to make them pass, but to lock them in place so the next person would know exactly what the system was supposed to do. His test suite became a specification in executable form.

**Leela** made the work accessible. She understood that no matter how good the architecture was, if developers and operators couldn't understand how to use it, the project failed. She audited documentation for gaps. She created conference-ready team introductions for the PowerShell Summit. She wrote release notes that operators could actually understand. She documented new features—Resources and Prompts, server configuration for Azure deployment, Docker build semantics—in ways that connected implementation to real-world value. She turned engineering output into learning.

**Scribe** kept us honest. Every decision the team made got logged. Every iteration, every mistake, every pattern discovered went into the record. That record became the institutional memory that ensured new contributors could understand context quickly and avoid repeating past debates. Scribe's work was invisible in production, but it was essential for the team's ability to move fast and stay aligned.

**Ralph** kept us moving. With a backlog of features, fixes, and edge cases to handle, Ralph's job was to make sure the team never sat idle. He monitored the work queue, triaged issues, and maintained visibility into what was coming next. He was the keeper of predictability.

---

## Part II: The Technical Journey

### Foundation: The MCP Architecture

In March 2026, our team started with the core question: how do we expose PowerShell in MCP?

Farnsworth's answer: **dynamic schema generation**. Instead of requiring developers to manually define MCP tools, our server would inspect PowerShell functions at runtime, extract their signatures, build JSON schemas automatically, and expose them as MCP tools. This meant:

- PowerShell functions are the source of truth.
- The MCP tool definitions are derived, not handwritten.
- Changes to function signatures automatically propagate to the tool schema.

It sounds simple until you realize the implication: we were building a compiler that translates PowerShell signatures (with all their dynamic quirks) into strict JSON Schema definitions (which allow no ambiguity).

Hermes immediately saw the problems this created:

1. **Parameter types that can't serialize:** PowerShell allows parameters typed as `ScriptBlock`, `PSObject`, `IntPtr`, `Delegate`, or dozens of other types that have no JSON representation. Hermes designed a filtering system: if a parameter is unserializable, drop it silently (if it's optional) or drop the entire parameter set (if it's mandatory). This kept the tool schema clean without lying to the MCP client.

2. **Large result sets that hang:** When a user called `Get-Process` via MCP, the system would serialize ~200 objects, each with ~50 properties. Some of those properties (like `Process.Modules`) are expensive to enumerate. Hermes diagnosed the problem: we were walking the entire object graph, triggering OS calls that could block indefinitely. His solution: property-set discovery. Before exposing a command, discover the `DefaultDisplayPropertySet` for each output type and surface only those properties. Reduce the serialization payload from 10,000+ property accesses to 50.

3. **Runspace lifecycle management:** PowerShell runspaces are not thread-safe and are expensive to create. Hermes designed the singleton runspace pattern: create one runspace at server startup, protect it with a semaphore to ensure only one thread uses it at a time, and clean it up at shutdown. This worked beautifully—until out-of-process execution came into play.

### Transport: HTTP and Stdio

Bender tackled the transport layer. The MCP spec supports both HTTP and stdio. Most MCP servers pick one. Bender realized that for operators, this was a critical decision point: deploying to Azure Container Apps? HTTP. Running locally for development? Stdio. Why force a choice?

His answer: **a unified `poshmcp` CLI that handles both modes.**

```powershell
poshmcp serve --transport http   # HTTP mode (default)
poshmcp serve --transport stdio  # Stdio mode
poshmcp build                    # Build the container image
poshmcp scaffold                 # Scaffold infrastructure
```

This meant the same `poshmcp` binary worked in every deployment scenario. Operators didn't have to learn multiple tools. Developers didn't have to maintain two separate entry points.

But unification created new problems. HTTP mode runs an ASP.NET Core server. Stdio mode runs the MCP protocol over stdin/stdout. How do you log in stdio mode without breaking the protocol? Write a log message to stdout and you've corrupted the MCP stream.

Amy and Fry tackled this together: **stdio logging suppression**. In stdio mode, the server silently suppresses console logging and optionally routes logs to a file via Serilog. In HTTP mode, nothing changes. The unified entry point hides this complexity from the operator.

### Observability: Making Production Visible

Amy drove a complete observability architecture using OpenTelemetry. Instead of proprietary logging, we built on the open standard: OpenTelemetry for metrics and structured logs, Application Insights for cloud-side collection, and health endpoints for operational checks.

The health endpoint was critical. When a server is struggling—too many open runspaces, hung commands, memory exhaustion—an operator needs to know **before** it fails completely. Amy designed a `/health` endpoint that returns structured health status:

```json
{
  "status": "healthy",
  "runspacePoolSize": 1,
  "activeCommandCount": 0,
  "lastCommandCompletedAt": "2026-07-30T12:34:56Z"
}
```

Amy also integrated Application Insights **gracefully**. The requirement: if Application Insights is enabled but misconfigured (no connection string), the server must still start and function. It just logs a warning. This meant operators couldn't accidentally break production by forgetting a secret.

### Configuration: A Single Source of Truth

Configuration in PoshMcp flows through layers: infrastructure defaults (Bicep/parameters.json) → deployment scripts → environment variables → appsettings.json → runtime defaults.

Amy made a critical decision: **Bicep and parameters.json are the source of truth. Deploy scripts are wrappers.**

This meant that when parameters changed (default resource group name, region, sku), the scripts would follow, not fight. She also built a configuration translation layer: the deploy script could read a PowerShell appsettings.json, translate its keys to environment variables (e.g., `PowerShellConfiguration.CommandNames` → `POSHMCP_COMMAND_NAMES`), and pass them to the container.

### The Doctor: Operational Diagnostics

One of the most powerful tools Farnsworth designed was the `poshmcp doctor` command. When a server starts up, something might go wrong:

- A configured PowerShell module isn't installed.
- A command isn't being discovered.
- The transport is misconfigured.
- Application Insights is enabled but has no connection string.

Farnsworth's design: **a structured diagnostic output that tells operators exactly what's wrong and why.**

```text
╔════════════════════════════════════════════╗
║ PoshMcp Server Configuration Diagnostics  ║
╚════════════════════════════════════════════╝

── Runtime Settings ──
  Transport:      http
  Log Level:      Information
  Session Mode:   Stateless
  
── Configured Commands ──
  [✓] Get-Process
  [✓] Get-Service
  [⚠] Get-ExternalData (warning: all parameter sets skipped due to unserializable types)
  [✗] Invoke-Missing (missing: command not found in PS session)
    reason: command not found in PS session
```

Hermes built the diagnostic engine. When a command is marked `[✗] MISSING`, the system doesn't just say "missing." It runs PowerShell introspection to determine exactly why:

- Is the module installed?
- Is the module in `PSModulePath`?
- Does the module export the command?
- Are all parameter sets filtered out by the unserializable type filter?

Each of these generates a different, actionable reason. An operator reads the output and knows exactly what to fix.

Fry tested the entire diagnostic output, creating 28 test cases across three phases:

1. **DoctorReport structure** — the underlying data model
2. **DoctorTextRenderer** — how it gets formatted for human reading
3. **MCP tool definition** — how it gets exposed to AI clients

His tests locked in the exact output format. The next person to modify doctor output would see immediately what was supposed to happen.

### Resources and Prompts: Extending MCP

Farnsworth pushed the MCP spec further. MCP supports Resources (files/content the LLM needs) and Prompts (reusable interaction templates). We built support for both.

A Resource might be a README file that an AI needs to understand the project. A Prompt might be "analyze this code for security issues" with pre-filled context. Both can be sourced from files or generated by PowerShell commands.

The spec required careful design:

- How do you reference a resource? URI scheme: `poshmcp://resources/{slug}`
- How are resource files resolved? Relative to `appsettings.json`.
- How do you substitute arguments in a prompt? Pre-assign them in the PowerShell environment before the command runs.
- What about result caching? Not in the server; operators can implement it in PowerShell if needed.

Fry wrote 16 integration tests validating resources and prompts end-to-end. Every code path got exercised: file sources, command sources, error handling, missing resources, validation failures.

### Deployment: From Laptop to Azure

Amy built a complete Azure deployment infrastructure. The flow:

1. **Scaffold** — use `poshmcp scaffold` to generate a Bicep template and parameter file.
2. **Customize** — edit parameters for your environment (region, sku, authentication).
3. **Deploy** — run `deploy.ps1` to provision resources and push the container image.

But Amy went further. She designed spec 007: **support for deploying pre-built container images**.

Instead of always building locally, operators could:

1. **Mode A (local pull):** Pull an image from Docker Hub, re-tag it for ACR, push it.
2. **Mode B (ACR import):** Use ACR's pull-through cache to import directly.
3. **Mode C (traditional):** Build from Dockerfile locally (the original behavior).

This enabled artifact promotion workflows: build once in CI, test in staging, promote to production without rebuilding.

---

## Part III: Quality and Confidence

### Testing: Locking in Behavior

Fry didn't just test features. He tested assumptions. When Bender refactored Docker build logic into a `DockerRunner` utility class, Fry created 11 unit tests covering:

- Minimal configuration (just a Dockerfile)
- BuildKit mode
- Multiple registries
- Multi-architecture builds
- Custom output paths
- Error conditions

These weren't nice-to-have tests. They were specifications in code. The next time someone modified Docker build arguments, Fry's tests would immediately show whether the behavior changed.

The same discipline applied to Application Insights integration. Amy added support for telemetry export. Fry wrote tests for:

- Server startup with Application Insights enabled (should succeed)
- Server startup with Application Insights enabled but no connection string (should warn, not crash)
- All MCP operations working normally even if telemetry is misconfigured

This testing gave operators confidence: misconfiguration wouldn't cause a complete failure.

### Pattern Extraction: Building the Team's Playbook

Over two months of work, patterns emerged. Farnsworth and Fry extracted them:

- **Unserializable type filtering** — How to handle PowerShell types that can't serialize to JSON
- **Precomputed optional parameters** — Pass computed data to handlers to avoid duplicate work
- **CLI boolean flag patterns** — How to map PowerShell switch parameters to JSON schemas
- **Worktree PR merge workflows** — Git branching strategy for concurrent work
- **Property set discovery** — How to expose only the relevant properties of complex PowerShell objects

These patterns got documented in `.squad/skills/`. Future work on similar problems could reference these patterns instead of re-solving them.

### Documentation: Making It Real

Leela understood that documentation isn't something you write after the code is done. It's part of the product.

When Resources and Prompts shipped (spec 002), Leela didn't just update a README. She wrote a 4,600-word user guide:

- How to configure resources
- How to author prompts with argument substitution
- Real-world examples (Azure deployment scenarios, security analysis workflows)
- Troubleshooting guide (what to do when a resource fails to load)

When we shipped v0.8.4 (a security patch), Leela wrote release notes that told operators exactly what they needed to know:

- CVE-2026-40894 (OpenTelemetry vulnerability)
- Why it mattered (moderate DoS risk)
- What they had to do (upgrade, no config changes needed)
- Breaking changes (none)

This mattered. Operators could read the release notes and make confident decisions about whether to upgrade now or later.

---

## Part IV: Scaling Through Challenges

### The Hang: Large Result Sets

In April 2026, we discovered a critical problem: certain PowerShell commands would hang when called via MCP. Specifically, `Get-Process`.

Hermes diagnosed it: the system was trying to serialize ~300 Process objects, each with ~50 properties. Several of those properties (like `Modules`, `Threads`, `Handle`) make Win32 API calls. On busy systems, those API calls could block indefinitely.

The solution wasn't to add a timeout (that's a band-aid). The solution was to surface only the properties that matter. Hermes implemented **property-set discovery**: before exposing a command, discover the `DefaultDisplayPropertySet` metadata for its output type and surface only those properties. Reduce the serialization payload from 10,000+ property accesses to 50.

This pattern became foundational. Future commands that returned large object graphs would use the same approach.

### The Skip: All Parameter Sets Filtered

Hermes also discovered that some commands have all their parameter sets filtered out due to unserializable types. For example, a command might have a parameter typed as `ScriptBlock`. We can't represent that in JSON, so we drop the parameter. If that's the only parameter, we drop the entire parameter set. If all parameter sets get dropped, we drop the entire command from the MCP schema.

But then the `poshmcp doctor` command would show: `[✗] MyCommand (missing)`. That's not quite right. The command isn't missing; it's just not exposed because its parameters can't serialize.

Fry and Hermes worked together: Hermes added a `ResolutionReason` field to the diagnostic output. Now doctor shows:

```text
[✗] MyCommand (missing)
    reason: all parameter sets skipped due to unserializable types
```

Operators immediately understand: the issue isn't that the command isn't installed. It's that it can't be exposed via MCP.

### The Test Failure: Validator Assumptions

Fry discovered a subtle bug in the validator for Resources. The test expected the validator to warn when a resource had no MIME type. But the model had a C# default of `"text/plain"`, so the validator never saw a null value—it always saw the default.

Bender fixed it by making MimeType nullable: `string?` instead of `string`. Now the model could distinguish "not configured" from "explicitly set to text/plain". At runtime, the handler applies the `"text/plain"` default.

This became a pattern: **model defaults that prevent validators from firing should be moved to runtime handlers**. The model should reflect the truth (is this value set?). The handler should apply defaults.

---

## Part V: The PowerShell Summit

As the work neared completion, Steven asked the team to prepare for the PowerShell Summit. We had to introduce ourselves—not as job titles, but as contributors to something real.

### How the Squad Describes Itself

**Squad:** This is our delivery squad for PoshMcp. We combine architecture, PowerShell depth, operations, testing, and documentation to ship practical MCP capabilities.

**Farnsworth:** Farnsworth leads architecture and long-range technical direction. He is the reason dynamic PowerShell functions can be surfaced cleanly as MCP tools.

**Bender:** Bender focuses on backend implementation details and runtime behavior. He helped unify the `poshmcp` entry path so deployment and local development feel consistent.

**Hermes:** Hermes is our PowerShell and runspace specialist. He keeps execution semantics grounded in real shell behavior, which is critical for reliable tool calling.

**Amy:** Amy owns platform and cloud reliability concerns. She has driven observability and operational readiness so teams can run PoshMcp with confidence.

**Fry:** Fry represents test quality and release confidence. He expands automated coverage to catch regressions early and validate end-to-end MCP scenarios.

**Leela:** Leela connects engineering output to developer outcomes. She turns implementation details into usable guidance, examples, and talk tracks for real adopters.

**Scribe:** Scribe documents what the team decides and why. That record helps new contributors understand context quickly and avoid repeating past debates.

**Ralph:** Ralph monitors the work queue and execution flow. He keeps team status visible so risks are identified early and delivery stays predictable.

---

## Part VI: Key Learnings

### Architecture: Derived, Not Manual

The biggest insight: **MCP tool schemas are derived from PowerShell signatures, not handwritten.** This changes everything. It means:

- Adding a new PowerShell function automatically exposes it as an MCP tool.
- Changes to function signatures propagate instantly.
- No separate tool definition maintenance.
- But it also means handling all the edge cases: unserializable types, large object graphs, runspace lifecycle.

### Operations: Infrastructure as Source of Truth

Amy's principle: **infrastructure code is source of truth; scripts are wrappers.** Bicep templates and parameter files define the canonical defaults. Deploy scripts read those defaults and use them consistently. When defaults change, scripts follow. This eliminates inconsistency.

### Testing: Specifications in Code

Fry's approach: **tests are specifications.** When you write a test, you're documenting what the system is supposed to do. The next person who modifies that code will see immediately what the test expects. This is more powerful than any wiki documentation.

### Diagnostics: Tell Operators Why

Farnsworth's design: **diagnostics must explain why, not just what.** When `poshmcp doctor` shows a command as missing, it should explain: "module not in PSModulePath" or "command not found in PS session" or "all parameter sets skipped due to unserializable types." Operators need to know what to fix.

### PowerShell: Handle the Dynamic Parts Carefully

Hermes's insight: **PowerShell is dynamic, but MCP is strict.** The bridge between them requires careful handling:

- Not all PowerShell types can serialize to JSON.
- Not all properties are relevant to AI clients.
- Certain operations (enumerating Process objects) can have side effects.
- Runspaces are stateful and expensive.

Build the system knowing these constraints. Design around them, not against them.

### Documentation: Learn What Matters

Leela's lesson: **users care about outcomes, not implementation.** When you ship a new feature, don't document the code. Document what the user can do with it. Give examples. Show the before/after. Connect it to real problems they're trying to solve.

---

## Epilogue: What's Next

By April 2026, the squad had shipped:

- **Dynamic PowerShell-to-MCP tooling** — any PowerShell function can be a discoverable tool
- **Unified transport** — single `poshmcp` entry point supporting HTTP and stdio
- **Observability** — OpenTelemetry integration, health endpoints, structured diagnostics
- **Azure deployment** — complete infrastructure scaffolding and cloud-ready deployment scripts
- **Resources and Prompts** — MCP extensions for passing context and reusable interaction templates
- **Out-of-process execution** — safer execution model for untrusted scripts via subprocess isolation
- **Comprehensive testing** — 520+ unit, functional, and integration tests
- **Production documentation** — user guides, release notes, API documentation, troubleshooting

The team continues to work. Ralph monitors the backlog. Fry identifies regressions. Hermes handles edge cases in PowerShell execution. Amy refines observability. Bender ships features. Leela documents them. Farnsworth thinks about the next architectural challenge. Scribe logs everything.

That's the squad story: **a diverse team, clear roles, mutual respect, and a shared commitment to shipping something practical and reliable.**

---

## Project Artifacts

- **Repository:** https://github.com/usepowershell/PoshMcp
- **Documentation:** https://usepowershell.github.io/PoshMcp/
- **Release:** v0.8.4+ (with all documented features)
- **NuGet Package:** 700+ downloads from nuget.org
- **Test Coverage:** 520+ tests (unit, functional, integration)
- **Team:** 8-person squad + continuous community contribution
