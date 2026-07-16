# 2026-07-16: Benchmark quality gates use a discoverability contract

**By:** Fry (issue #336)

**What:** Shared CI verifies that the HTTP session benchmark scenarios build and
are discoverable, then uploads that case list as an artifact. It does not make
timing assertions. The benchmark suite measures first-session startup,
warm-session calls, concurrent warm sessions, and bounded-capacity behavior
using a deterministic benchmark configuration.

**Why:** Benchmark timings are machine-dependent and therefore unsuitable as
required checks on shared CI runners. The contract gate prevents accidental
scenario removal while controlled-machine BenchmarkDotNet reports provide
reproducible comparison evidence.
