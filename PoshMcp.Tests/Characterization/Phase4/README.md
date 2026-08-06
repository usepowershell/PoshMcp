# Phase 4 Performance Gate

Phase 4 compares v2 MCP SDK performance against the v1 baseline artifact captured in Phase 0.

## Gate Logic (Decision C — 2026-08-06)

### Blocking gates (exit code 1 on breach)

| Scenario | Threshold | Comparison |
|----------|-----------|------------|
| `cold_start_http_with_script` p95 | ≤ 1.10× | v2 vs v1 like-for-like |
| `cold_start_http_no_script` p95 | ≤ 1.10× | v2 vs v1 like-for-like |
| `memory_idle_mb` mean | ≤ 1.10× | v2 vs v1 like-for-like |
| `memory_light_load_mb` mean | ≤ 1.10× | v2 vs v1 like-for-like |
| `memory_moderate_load_mb` mean | ≤ 1.10× | v2 vs v1 like-for-like |
| `warm_call_latency_ms` p95 | ≤ 1.10× | v2-pool-reset vs v2-ephemeral (same-SDK) |
| `concurrent_throughput_ms` mean | ≤ 1.10× | v2-pool-reset vs v2-ephemeral (same-SDK) |

Constant names: `SameSdkWarmCallP95MaxRatio = 1.10`, `SameSdkThroughputMeanMaxRatio = 1.10`.

### Informational only (never block — `IsBlocking=false`)

| Scenario | Threshold (recorded) | Comparison |
|----------|----------------------|------------|
| `warm_call_latency_ms` p95 | ≤ 1.05× | v2-pool-reset vs v1-ephemeral (cross-SDK) |
| `concurrent_throughput_ms` mean | ≤ 1/0.95× | v2-pool-reset vs v1-ephemeral (cross-SDK) |

Cross-SDK warm/throughput ratios are logged in `ThresholdChecks` with `IsBlocking=false` and do **not** influence `AllPassed`.

## Rationale for Decision C

SDK v1→v2 migration added Streamable HTTP protocol overhead (~100–200 µs/call) that is not a
product regression — it is a structural protocol change. Gating on cross-SDK warm/throughput
would permanently fail the gate for reasons outside the product's control.

Decision C replaces the cross-SDK warm gate with a **same-SDK isolation gate**: v2-pool-reset
vs v2-ephemeral measures only the cost of pool reuse vs create-dispose, which is the actual
isolation regression signal.

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | All blocking gates passed |
| 1 | One or more blocking gates breached |
| 2 | Methodology validation failure (isolation mode mismatch, N mismatch, etc.) |

## Schema Version

`poshmcp/v4-comparison/1.1` — `IsBlocking` field added to `Phase4ThresholdCheck`;
`SameSdkIsolationChecks` list added to `Phase4ModeComparison`.

## Methodology Contract

`poshmcp/methodology-contract/1.1` — `V2EphemeralIsolationMode` field added.

## Artifact Files

- `phase4-stateless-v2pool.appsettings.json` — v2 pool-reset Stateless config
- `phase4-stateful-v2pool.appsettings.json` — v2 pool-reset Stateful config
- `phase4-stateless-v2ephemeral.appsettings.json` — v2 ephemeral Stateless config (EphemeralMode=true)
- `phase4-stateful-v2ephemeral.appsettings.json` — v2 ephemeral Stateful config (EphemeralMode=true)
