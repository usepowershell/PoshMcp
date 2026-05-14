using Xunit;

// Many tests create PowerShell runspaces and child server processes.
// Running tests in parallel can cause nondeterministic host aborts.
[assembly: CollectionBehavior(DisableTestParallelization = true, MaxParallelThreads = 1)]

// ─── Spec 009 — Test Category Policy ──────────────────────────────────────────
//
// Every test class in this assembly SHOULD carry exactly one
// [Trait("Category", "<X>")] attribute, where <X> is one of:
//
//     Unit, Integration, OutOfProcess, Http, Azure, Functional
//
// Per FR-417, tests with no explicit Category trait fall back to the
// "Integration" bucket (permissive default). The default-bucket fallback
// MUST NOT be "Unit" — an untagged test must never silently appear in the
// fast unit tier (which guarantees no subprocess, no port, no shared temp
// per FR-401/FR-402/FR-403). Treating untagged tests as "Integration" keeps
// them runnable but excludes them from the fast pre-commit tier until they
// are explicitly tagged.
//
// See specs/009-test-suite-consistency/spec.md for the full category
// definitions and FR list. Per-category run commands and the contributor
// TESTING.md guide are tracked separately under issue #214.
