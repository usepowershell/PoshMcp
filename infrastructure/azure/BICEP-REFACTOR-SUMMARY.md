# Bicep modularization refactor — historical summary

> **Note:** This is a historical record of the 2026-03-27 Bicep refactor.
> For current architecture details, see [MODULARIZATION.md](./MODULARIZATION.md).
> For the overall infrastructure design, see [ARCHITECTURE.md](./ARCHITECTURE.md).

**Date:** 2026-03-27
**Author:** Amy Wong (DevOps/Platform/Azure Specialist)

## What changed

Refactored Azure Bicep infrastructure from a flat subscription-scoped file to a **modular deployment pattern** with `main.bicep` (subscription scope) and `resources.bicep` (resource group scope). This fixed 10 compilation errors and enabled subscription-scoped role assignments for Managed Identity.

## Compilation errors resolved

| Error | Count | Root cause |
|-------|-------|------------|
| BCP139 — scope mismatch | 5 | RG-scoped resources deployed from subscription scope |
| BCP265 — `resourceGroup` not a function | 3 | Outdated syntax; updated to `az.resourceGroup()` |
| BCP037 — invalid `scope` property | 2 | Used modules instead of `scope:` on resources |
| BCP120 — non-deterministic GUID | 1 | Changed role assignment GUID to use deployment-time values |

## Deployment impact

No breaking changes. Parameters, resources, outputs, and deployment commands are all unchanged. Existing deployments update in place with zero downtime.

## See also

- [MODULARIZATION.md](./MODULARIZATION.md) — Full module architecture, parameter flow, migration guide, and troubleshooting
- [ARCHITECTURE.md](./ARCHITECTURE.md) — Overall infrastructure design
- [QUICKSTART.md](./QUICKSTART.md) — Deployment commands
