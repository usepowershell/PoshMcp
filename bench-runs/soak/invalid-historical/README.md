# Invalid historical soak runs — DO NOT use as acceptance evidence

These two runs are retained **for historical context only**. They are **not authoritative**
and must not be cited as pass/fail evidence for issue #349, nor used to attribute any
root cause.

## Why they are invalid

1. **Dirty working tree.** Both runs executed against uncommitted working-tree state. Their
   `summary.json` files record the parent commit `cc50524…`, which does **not** contain the
   harness code that produced them. The results are therefore not reproducible from any
   committed SHA.
2. **Discredited handle gate.** These runs were evaluated by the original whole-run OLS-over-raw-
   `HandleCount` gate. Handle count under load is a **bounded sawtooth**; a linear fit over the
   raw series is dominated by peak amplitude and the run's end phase, producing a **false-positive**
   "leak" signal. Independent recomputation showed the handle **floor** was flat/negative and the
   terminal value ≈ the initial value — i.e., **no leak was demonstrated**.
3. **Incomplete pool observability.** ~12% of samples (14/120 in run 2) lacked pool/health stats,
   so pool gates were evaluated on a subset without reporting the gap.

## What they are NOT

- Not a pass. Not a fail. Not proof of a product handle leak.
- Not evidence of any component attribution (no PowerShell/SDK/pool root cause is established).

## Contents

| Directory | Notes |
|---|---|
| `20260804-094300/` | Run 1. `summary.json` (parent `cc50524`) + raw `samples.csv`. Old gate flagged a flat 191-wide handle band as a leak. |
| `20260804-run2/20260804-105936/` | Run 2. `summary.json` (parent `cc50524`) + raw `samples.csv`. 103,217 requests / 0 errors; raw whole-run handle slope 0.741/s with R²≈0.14 (invalid); floor slope ≈ −0.04/s (flat). |

## Secret / PII scan

The retained `samples.csv` and `summary.json` files contain only numeric process/pool metrics,
timestamps, request counts, and runtime/OS strings. No credentials, tokens, connection strings,
or personal data are present.

Authoritative evidence for #349 comes only from a clean, committed harness SHA run on
`windows-latest` under the redesigned floor/plateau contract (see the PR body and the top-level
soak harness in `PoshMcp.Tests/Soak/`).
