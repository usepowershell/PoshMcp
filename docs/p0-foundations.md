# P0 foundations, feasibility, and CI/CD

## Boundary and prerequisites

Implements only the P0 milestone and the approved snapshot in roadmap #424.
The source in `fresh/` never references the legacy assembly; existing switch,
schema, and subprocess tests are behavioral reference, not execution code to
import. Production dispatch, pools/queues, discovery, MCP adapters, authorization
implementation, containers, and release promotion remain P1-P5.

Native dependencies were read before implementation: 402 has none; 405 depends
on 402; final 404 budgets depend on 402/405; 403 depends on 402; 406 depends on
402/403; 407 depends on 402/406. Qualification gaps do not become satisfied
acceptance criteria merely because pipeline code exists.

## Contracts and package decision (402)

The fresh executable owns immutable runtime profiles and catalog contracts.
Generation fingerprints cover runtime selection, absolute paths, ordered
startup content hashes, pinned modules, and allowed commands. One deployment
generation owns one catalog; configuration changes require restart/rollout.
Workers transition Starting -> Initializing -> Ready -> Leased -> Executing ->
Retiring -> Exited, or Faulted -> Retiring -> Exited. A lease is consumed once,
including on errors/cancellation. Discovery will use separate disposable
workers counted against the total process budget.

Worker protocol v1 uses length-prefixed UTF-8 JSON, correlation IDs, a 1 MiB
frame limit and separately drained, bounded stdout/stderr. Errors, partial
output, native exit status and serialization failures must be explicit. No
arbitrary-output JSON recovery, shell interpolation, replay, or runtime fallback.
MCP revision 2026-07-28 is a **target**, not a P0 conformance claim. SDK 2.0.0
(existing reference) must be qualified with real clients/wire tests in P4;
the P0 executable intentionally advertises no MCP capabilities.

Pin .NET SDK 11.0.100 / net11.0 (approved during implementation) and external
PowerShell 7.6.0. The legacy release line remains net10.0. Windows PowerShell
5.1 uses the host-serviced 5.1 build, whose complete identity must be recorded
by native tests; it cannot be pinned by redistributing Windows binaries.

| Package RID | Native qualification host | Runtime |
| --- | --- | --- |
| linux-x64 | Ubuntu 24.04 amd64 | PowerShell 7.6.0 x64 |
| linux-arm64 | Ubuntu 24.04 arm64 | PowerShell 7.6.0 arm64 |
| win-x64 | Windows Server 2025 amd64 | PowerShell 7.6.0 x64; explicit Desktop 5.1 x64 |
| win-arm64 | Windows 11 arm64 | PowerShell 7.6.0 arm64 |

Windows ARM64/5.1 availability and executable architecture remain an
investigation, **not** a supported combination. No emulation claim substitutes
for native coverage. Only Ubuntu 24.04 is the initial Linux qualification target.

Four independent framework-dependent .NET-tool packages use the IDs in
`fresh/README.md`, all at the same version. Each contains the worker bundle and,
on Linux, a native matching containment launcher. No PowerShell is bundled.
This proposed layout avoids a selector that could silently choose a runtime,
but it is **not yet demonstrated installable**: the local pack gate rejects
loose PowerShell content as NuGet installation scripts (see the ledger below).
Alpha/beta/stable use 1.0.0-alpha.N, 1.0.0-beta.N and 1.0.0. Main uses a unique
numeric prerelease with run ID, attempt, and commit identity. Existing
`poshmcp` identity/entry point and releases stay intact; replacement needs the
later migration/promotion decision.

## Identity and migration policy

One trusted workload identity/security domain per deployment. Stdio uses the
launching account; hosted deployments use a least-privilege service account.
Caller authorization is distinct from OS/target identity: no automatic
impersonation, credential forwarding, or per-profile tenant boundary.
Preinstall pinned modules, preferably read-only. Auth caches have restrictive
permissions within that identity boundary. Request secrets never belong in
startup baselines, command lines, general diagnostics or persistent files.
Process containment is **not a sandbox**; shared files/network/cache side
effects require separate deployments when identities must be isolated.

| Legacy feature | Fresh-core boundary |
| --- | --- |
| Embedded runtime, reusable execution/session workers, dynamic IL | Removed by design |
| Legacy initialization/session/SSE | Deferred; modern stateless stdio/HTTP in P4 |
| Interactive resume/replay, Tasks/result handles, implicit last output | Deferred |
| Resources/prompts/examples, client-driven configuration mutation | Deferred |
| Declarative profiles, explicit schemas/aliases, command allowlists | Contracts now; production implementation P1/P3 |
| Caller authorization, protocol interoperability, containers/migration | P4/P5, not implemented by P0 |

## Worker feasibility (405)

`WorkerProbe` is a disposable **prototype**, not the P1 execution foundation.
It creates a current-user-only named pipe, passes a fresh nonce over redirected
stdin, validates the hello/Ready correlation and runtime identity, and drains
stdout/stderr independently without searching them for protocol data. Startup
hashes and pinned module imports are checked before Ready; the startup deadline
kills a worker that is slow or fails. Invocation names are allowlisted and
parameters are splatted from structured data. Shared scripts use 5.1 syntax and
.NET Framework APIs; actual 5.1 execution remains mandatory native evidence.

Plain data conversion preserves null, strings, booleans, arrays and objects.
Int64/Decimal are tagged invariant strings; DateTime is a tagged UTC round-trip
string. Unsupported types and depth >16 fail, rather than stringify/truncate
silently. Native nonzero exit codes and nonterminating errors fail the result;
ordinary error results retain successfully converted partial output.
Serialization failure discards unserializable output and never reports success.
The wire cap is 1 MiB; stdout/stderr are drained without retention and each has
a 32 Ki-character post-invocation failure budget. A continuously noisy/hung
command is bounded by its invocation deadline, not a production streaming
output-quota implementation (P1).

Linux's native launcher establishes the child process group, uses parent-death
signals with startup-race checks, and kills the group after exit, timeout or
supervisor death. Descendants that deliberately escape with `setsid()` require
the mandatory deployment backstop: systemd `KillMode=control-group` or an
equivalent container/cgroup lifecycle boundary. Zombies are reported as
non-running, not falsely claimed reaped; PID 1/service must reap orphans.
Windows's native launcher uses an atomic process-creation job-list attribute,
a non-inherited kill-on-close Job Object, restricted inherited stdio handles,
and a supervisor process handle. The child cannot execute in a pre-assignment
gap. Windows native execution, Job Object nesting/host policy, and ARM64
compiler/API qualification are still pending.

Tests cover structured Unicode/multiline output, nonterminating errors, normal
descendant retirement, failed/slow startup, timeout, worker crash, supervisor
crash and repeated one-use cycles. Missing runtime/launcher is a failure, never
a skipped test. More adversarial/native-output/serialization cases and native
results are required before 405 can close.

## Measurements and sizing (404)

Run `poshmcp-foundation --measure /absolute/pwsh PowerShell7 report.json`, or
select `WindowsPowerShell51` explicitly on Windows x64. Provision
PSScriptAnalyzer 1.24.0 in `POSHMCP_TEST_MODULE_PATH` first. The heavy profile
imports that real module; it is not an arbitrary workload throughput claim.
The minimal, heavy, controlled five-second and failing profiles are repeatable.
Reports separate initialization, acknowledged warm dispatch (an IPC metric,
**not yet the required admitted-request-to-command-start measurement**),
execution, retirement, worker CPU/peak working
set, supervisor CPU/peak memory and before/after handles or Linux descriptors.
Queue wait is explicitly **unmeasured** until P2 admission exists.

Ten serial samples and a two-initializer batch are feasibility data only, not
a statistically qualified p95 or sustained-load result. Record CPU, OS,
architecture, exact runtime identity, .NET version and hardware with each run.
Native matrix measurement artifacts are the place for measured values; no
measurements or approved numeric defaults are invented here.

For measured mean startup S, mean execution E and retirement R, sustained
replacement is at most B/S for B initializer slots; reserve Q only absorbs a
burst and cannot increase that steady-state rate. At target arrival rate
lambda, choose B > lambda*S with measured headroom. Budget at least
Q + ceil(lambda*(S+E+R)) plus discovery and failure/drain margin; also reserve
initializer slots separately from execution and account for per-profile
memory/CPU. Select execution concurrency, total live-process/memory caps,
bounded queues and startup/queue/invocation/drain deadlines together from
native measurements and downstream latency limits, not arbitrary defaults.
For the five-second fixture, B=2 implies an ideal ceiling of 0.4 replacements/s,
not ten requests/s sustained from a ready reserve.

**Blocked:** final capacity/default/release budgets depend on qualified 405
and reviewed native measurement data. The proposed slow-fixture warm-dispatch
p95 <250 ms is still a hypothesis. The Windows handle-floor follow-up
<=0.010 handles/s and 60-minute mixed-load release soak remain P5 concerns;
the P0 ten-cycle test does not establish either.

## CI/CD audit and administrator setup (403, 406, 407)

The existing workflows were inspected before proposing replacements. They
remain **unchanged** in this draft because the CI/shared-command patch was
denied and downstream publication prerequisites are unresolved:

| Existing workflow | Gap against approved P0 |
| --- | --- |
| `ci.yml` | Path-filtered PR/main triggers; legacy solution and benchmark/session contract; category filters omit unclassified tests; no fresh-core or worker 5.1 lint gate |
| `preview-packages.yml` | Main path/message skips; independent Unit/Functional gate; one legacy package; workflow-wide package-write permission; blind duplicate suppression |
| `publish-packages.yml` | Legacy tag/manual publication; only Unit/Functional tests; no native four-target gate; blind duplicate/release-error suppression; container publication belongs to later delivery |

Do not merge this draft assuming the requested publication cadence is
operational. Existing releases are untouched; there is no new publishing
workflow and no package/tag/release was created by this work.

Required follow-through once the package/shared-gate prerequisites are resolved:

* Read-only PR build/all-unit/C# analyzer-format/PowerShell 5.1 lint gates;
  exhaustive classification must reject new/unclassified/skipped tests and
  external-resource access labeled Unit. Negative gate fixtures must be
  demonstrated failing. No `pull_request_target` or publishing secrets in PRs.
* Main: unfiltered execution of every implemented applicable suite (including
  fault and short P0 repeated-cycle/soak fixtures), plus native package
  install/start checks, before publication. No custom path/message skips or
  cancellation of older main runs. Use immutable NuGet prerelease versions
  containing run ID/attempt/commit provenance, not only SemVer build metadata.
* Tags: exact tagged commit and strict `v1.0.0-alpha.N`/`beta.N`/`v1.0.0`
  validation, complete native test matrix, the same validated package bytes
  uploaded to both feeds, checksums/provenance/release notes and correct
  prerelease status. Resume partial publication using preserved canonical
  artifacts; verify an existing version instead of hiding conflicts.
* Provision native Ubuntu 24.04 x64/arm64 and Windows Server 2025 x64/Windows 11
  arm64 runners with SDK 11.0.100, native PowerShell 7.6.0, native C compilers,
  and PSScriptAnalyzer 1.24.0. A required runner that is absent must queue/block
  or fail visibly, never succeed by skipping. Do not run untrusted PR code on
  privileged self-hosted release runners. Windows x64 also runs explicitly
  selected host-serviced Desktop 5.1; investigate ARM64/5.1 separately.
* Configure GitHub Packages access for the new IDs using job-scoped
  `packages: write` only after trusted validation. Reserve/authorize the IDs on
  nuget.org; provide a restricted `NUGET_API_KEY` or supported trusted-publishing
  identity through a protected release environment with reviewers and tag
  restrictions. No real secret is provided or invented in this draft.
* Protect main and version tags, approve release credentials/environments,
  retain package manifests/checksums/test logs and canonical release artifacts,
  and require the new stable check names **after the actual workflows exist**.
  The legacy `build` check is not evidence of P0 acceptance. Stable promotion
  additionally requires P5 qualification, not merely a P0 pipeline.

## Local evidence and blocking ledger

Evidence was produced on Ubuntu 24.04 **linux-x64**, SDK/runtime 11.0.100/11.0.0.

| Issue | Preserved implementation/evidence | Acceptance still pending |
| --- | --- | --- |
| 402 | Isolated skeleton, immutable contracts, package/native matrix, identity/migration decisions; build succeeds; contract unit fixtures pass | Four-target actual install/start; package layout is blocked; SDK/client/protocol and ARM64/5.1 qualifications |
| 405 | Private framed IPC prototype; external-only native launchers and worker/fault fixtures; Linux launcher compiles with `-Wall -Wextra -Werror`; shell-descendant normal/timeout/supervisor-crash checks pass | All PowerShell execution; native Windows/x64/arm64 and Linux arm64; native output/API/serialization edge cases; runtime/containment qualification |
| 404 | Minimal, real module, five-second and failing fixtures; ten-sample harness; sizing formulas | Actual native measurement reports, exact command-start/queue measurements, sustained capacity, reviewed numeric defaults and handle-growth evidence |
| 403 | Existing workflow audit | Shared commands, exhaustive classification and PR gates were not applied; PowerShell lint and negative gate qualification |
| 406 | Existing preview pipeline audit and required contract recorded | Blocked by unresolved 402 layout and 403 gates; no new publication workflow or registry evidence |
| 407 | Existing tag pipeline audit and required contract recorded | Blocked by 402/406; exact-commit dual-feed workflow, safe-rerun implementation and feed installation qualification |

Available validation:

```text
cd fresh && dotnet build -c Release --no-restore
  PASS, no warnings/errors
cd fresh && dotnet test -c Release --no-build --filter Category=Unit
  PASS, 8 tests, zero skipped (contracts and framing only)
cd fresh && dotnet test -c Release --no-build
  FAIL, 8 passed / 9 failed / zero skipped; each external-runtime test fails
  explicitly because POSHMCP_TEST_RUNTIME is unprovisioned
cc -std=c11 -Wall -Wextra -Werror -O2 native/launcher-linux.c ...
  PASS on Linux x64
Native launcher normal/timeout/supervisor-crash checks with /bin/sh descendants
  PASS; not PowerShell or cross-platform runtime qualification
dotnet run --project PoshMcp.Foundation -c Release --no-build -- --foundation-info
  PASS; explicitly reports P0 / no MCP implementation / no embedded runtime
dotnet pack ... -p:TargetRid=linux-x64
  FAIL NU5110/NU5111: loose .ps1 files treated as NuGet installation scripts
dotnet format --verify-no-changes --no-restore
  Remaining Program.cs whitespace findings; corrective writes are blocked
```

The environment denied installation/execution of downloaded SDK/PowerShell
binaries. The maintainer subsequently approved .NET 11, resolving the SDK
blocker using the installed SDK, but not authorizing PowerShell installation.
An attempted NuGet diagnostic suppression was denied; no suppression was
applied. A safer resource-bundle packaging edit was also denied as a previously
denied file write, as was the classification/shared-command/CI patch. No denied
action was performed through another tool. A remote-inspection probe was
denied because it could reveal credentials; no remote URL was inspected.

Completed work is preserved rather than inventing measurements, suppressing
tests/diagnostics, or implementing publication ahead of its prerequisites.
The working branch and this draft do not satisfy any issue's complete
acceptance criteria; **all six issues must remain open**.
