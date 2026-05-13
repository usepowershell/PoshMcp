# Cold-start regression check — run-6 vs run-5

**Gate:** spec-010 FR-572 — any mode's Mean regressing more than 50% versus
[`run-5-pre-spec010`](../run-5-pre-spec010/) blocks merge.

**Outcome:** ✅ **PASS** — all three modes regressed under 13%, well under the 50% gate.

## Mean per cold start

| Mode        | run-5 Mean | run-6 Mean | Δ        | % Δ     | Gate (<50%) |
|-------------|-----------:|-----------:|---------:|--------:|:-----------:|
| Single      |    5.790 s |    6.470 s | +0.680 s | +11.74% |    ✅ PASS  |
| Pool        |    5.784 s |    6.483 s | +0.699 s | +12.08% |    ✅ PASS  |
| ProcessPool |    6.996 s |    7.742 s | +0.746 s | +10.66% |    ✅ PASS  |

## P95

| Mode        | run-5 P95 | run-6 P95 | % Δ     |
|-------------|----------:|----------:|--------:|
| Single      |   5.794 s |   6.557 s | +13.17% |
| Pool        |   5.812 s |   6.526 s | +12.28% |
| ProcessPool |   7.046 s |   7.875 s | +11.77% |

## P99

| Mode        | run-5 P99       | run-6 P99       | % Δ     |
|-------------|----------------:|----------------:|--------:|
| Single      |   5,793.955 ms  |   6,569.304 ms  | +13.38% |
| Pool        |   5,815.280 ms  |   6,528.059 ms  | +12.26% |
| ProcessPool |   7,048.350 ms  |   7,916.422 ms  | +12.32% |

## Notes

- Per-mode regression is consistent (~11–13%) across Mean, P95, and P99 for
  all three host modes, suggesting a uniform overhead added by spec-010
  rather than a mode-specific pathology.
- run-5 was captured on .NET SDK 10.0.107 / Runtime 10.0.7; run-6 on
  .NET SDK 10.0.108 / Runtime 10.0.8. Toolchain drift is minor and not
  expected to account for the full delta.
- Same machine (SJMDEVBOX), same `[InvocationCount(1)]` BDN config, same
  `--filter "*ColdStart*"` invocation.
