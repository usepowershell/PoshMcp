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
This layout avoids a selector that could silently choose a runtime. The
Linux x64 package now packs, installs and executes an external worker on its
matching native host. Trusted script source is stored as assembly resources,
not loose NuGet installation hooks; no packaging diagnostics are suppressed.
Scripts are extracted to a per-process temporary directory and removed on
normal shutdown. Only scripts, **not PowerShell**, are embedded; crashes can
leave non-secret script assets for normal host temp cleanup. The other three
native packages remain pending qualification.
Alpha/beta/stable naming is 1.0.0-alpha.N, 1.0.0-beta.N and 1.0.0. The planned main lane uses a unique
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

Linux's native launcher establishes the child process group, monitors a
supervisor **pidfd**, and kills the group after exit, timeout or supervisor
death. This requires the Ubuntu 24.04 kernel's `pidfd_open` support; unavailable
support fails launch explicitly. A supervisor `PR_SET_PDEATHSIG` was proven
unreliable because Linux can signal the exit of the creating .NET thread even
while the supervisor process is alive. A regression test exits that thread
and verifies the initialized worker still runs. The worker itself retains a
parent-death backstop under the single-threaded native launcher.
Descendants that deliberately escape with `setsid()` require
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
crash, launching-thread exit and repeated one-use cycles. All 20 currently
implemented .NET tests (including 10 unit tests) pass with real Linux x64
PowerShell 7.6.0; none are skipped. Redirected readers are explicitly disposed
after draining. Missing runtime/launcher is a failure, never a skipped test.
More adversarial/native-output/serialization cases and the remaining native
combinations are required before 405 can close.

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

The reproducible [Linux x64 report](p0-evidence/linux-x64.json) is **incomplete**:
the heavy module is unavailable and its installation was denied. The harness
records that failure, preserves independent minimal/slow/failing evidence, and
exits nonzero rather than claiming a complete measurement gate. On Ubuntu
24.04.5, AMD EPYC 9V74 with two available logical CPUs, .NET 11.0.0 and native
PowerShell 7.6.0:

| Fixture | Samples | Mean initialize | Ready-to-executing ACK p95 | Two-initializer init/retire batch rate | Peak worker RSS |
| --- | --- | --- | --- | --- | --- |
| Minimal | 10 | 400.83 ms | 10.19 ms | 3.814/s | 111.83 MiB |
| Five-second startup | 10 | 5394.00 ms | 9.80 ms | 0.366/s | 112.53 MiB |

These are shared-host feasibility observations, not approved budgets or
command-start latency promises. The first sample includes .NET lazy
initialization descriptor growth; subsequent minimal/slow samples show zero
before/after descriptor delta after explicit redirected-reader disposal.
That is not a resource-recovery soak or Windows handle-floor qualification.

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
remain **unchanged** because workflow replacement/creation is still denied.
Shared validation source is now present, but downstream publication
prerequisites are unresolved:

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

### Shared commands delivered

`python fresh/eng/validate.py unit` restores locked dependencies, builds and
runs all classified unit tests. Its reflection guard rejects unclassified,
unknown/multiple-category, inaccessible and explicitly skipped tests. The
source guard rejects direct subprocess/worker/pipe/file use labeled Unit.
`all` has **no category filter**, rejects an absent runtime, compiles the
native launcher, executes all implemented tests and rejects failed/skipped/
empty reports. It also checks source-discoverable test methods against TRX.
Future implemented categories therefore do not silently disappear.

`python fresh/eng/verify-gates.py` demonstrated five negative controls in a
disposable clone: an actual failed unit assertion, unclassified test,
misclassified subprocess call, compiler diagnostic and formatting violation
each fail for the intended reason. `python -m unittest discover -s fresh/eng
-p 'test_*.py' -v` runs three focused shared-gate tests.

`lint` retains C# formatting/analyzers and **all default** PowerShell
error/warning rules, then adds an explicit 5.1 syntax pass. No default rule is
disabled. Negative PowerShell lint/syntax controls are included but not yet
executed because PSScriptAnalyzer 1.24.0 installation remains denied.
`eng/provision.py` prepares checksum-pinned native PowerShell and that pinned
module for dedicated CI runners; it has not been executed in the shared
workspace and does not install anything globally.

## Local evidence and blocking ledger

Evidence was produced on Ubuntu 24.04 **linux-x64**, SDK/runtime 11.0.100/11.0.0.

| Issue | Preserved implementation/evidence | Acceptance still pending |
| --- | --- | --- |
| 402 | Isolated contracts, native matrix/identity policy; Linux x64 tool now packs, installs and invokes the intended external runtime without diagnostic suppression | Three other native package targets; SDK/client/protocol and ARM64/5.1 qualifications |
| 405 | Real Linux x64 initialization/invocation/error/timeout/crash/descendant and thread-exit regression tests pass; native launcher and reader cleanup corrected | Native Windows/x64/arm64 and Linux arm64; native output/API/serialization edge cases and containment backstops |
| 404 | Persisted incomplete native minimal/slow/failing report; full measurement harness and sizing formulas | Heavy-module installation/run denied; remaining native reports, exact command-start/queue measurements, sustained capacity, reviewed defaults and handle-growth evidence |
| 403 | Shared build/unit/all/lint/provision source; classification guard; three engineering tests and five negative controls pass; C# formatting passes | PR workflow writes still denied; PSScriptAnalyzer lint and negative PowerShell controls not run |
| 406 | Existing preview pipeline audit and required contract recorded | Blocked by incomplete 403 workflow gates and native qualification; no new publication workflow or registry evidence |
| 407 | Existing tag pipeline audit and required contract recorded | Blocked by 402/406; exact-commit dual-feed workflow, safe-rerun implementation and feed installation qualification |

Available validation:

```text
python fresh/eng/validate.py unit
  PASS, no warnings/errors
  10 unit tests, zero skipped
POSHMCP_TEST_RUNTIME=/absolute/pwsh python fresh/eng/validate.py all
  PASS, 20 tests, zero skipped; includes native launcher compilation
python -m unittest discover -s fresh/eng -p 'test_*.py' -v
  PASS, 3 tests
python fresh/eng/verify-gates.py
  PASS, all 5 negative controls rejected for the intended reason
cd fresh && dotnet format --verify-no-changes --no-restore
  PASS
dotnet pack ... -p:TargetRid=linux-x64 -p:Version=1.0.0-alpha.2
dotnet tool update ... --tool-path /private/session/foundation-tool ...
poshmcp-foundation --probe /absolute/pwsh PowerShell7
  PASS, actual installed local package initializes, invokes and retires
poshmcp-foundation --measure /absolute/pwsh PowerShell7 report.json
  FAIL explicitly for missing heavy module; independent evidence persisted
PowerShell 7 Parser.ParseFile over all 8 source scripts/settings
  PASS; not a substitute for PSScriptAnalyzer or native 5.1
```

The maintainer approved .NET 11, then explicitly authorized checksum-verified
PowerShell installation and corrective packaging/format/classification/CI
edits. That resolved the earlier SDK/runtime/package/source-edit blockers.
Additional worker diagnostics and local PSScriptAnalyzer executable-module
installation were denied. An allowlisted-lint proposal was rejected and
replaced by the materially stronger all-default-rules plus compatibility
passes; no default lint diagnostic was suppressed. CI deletion was denied as
disabling existing verification, and new workflow creation remained denied
even after explicitly preserving pipeline failures with `pipefail`.
No workflow was installed, removed or disabled. An accidental unidentified
dotnet target was rejected and never executed. The earlier remote-inspection
denial was respected; no credentials or remote URL were inspected.

Administrator follow-through now requires explicit authorization/provisioning
of PSScriptAnalyzer 1.24.0 and the exact in-place CI workflow replacement,
without deleting controls or masking failures, before dependent main/tag
pipelines can proceed. The publishing-credential and native-runner actions
listed above are still required.

Completed work is preserved rather than inventing measurements, suppressing
tests/diagnostics, or implementing publication ahead of its prerequisites.
The working branch and this draft do not satisfy any issue's complete
acceptance criteria; **all six issues must remain open**.
