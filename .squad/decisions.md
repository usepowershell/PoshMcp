# Decisions Log

Canonical record of decisions, actions, and outcomes.


## 2026-07

### Restore explicit resource group creation in deploy.ps1

**Author:** Amy Wong (DevOps/Platform/Azure)
**Date:** 2026-07
**Status:** Implemented

When Bicep was modularized to subscription scope, the `New-ResourceGroupIfNeeded` function in `deploy.ps1` was commented out under the assumption that `main.bicep` would handle resource group creation. However, `Initialize-ContainerRegistry` runs before `Deploy-Infrastructure`, so the resource group must exist before Bicep runs.

**Decision:** Keep explicit `az group create` in the deployment script **and** the declarative resource group in `main.bicep`. Both are idempotent. The script ensures the RG exists for imperative steps (ACR creation); Bicep re-declares it for completeness and drift correction.

**Rationale:**
- Azure resource group creation is idempotent — creating one that already exists is a no-op
- Mixed imperative/declarative pipelines need the RG available before any `--resource-group` flag is used
- Removing either one creates a fragile dependency on execution order

**Impact:** `deploy.ps1` — `New-ResourceGroupIfNeeded` uncommented and restored in workflow. No changes to `main.bicep` or `validate.ps1`. No breaking changes to any parameters or interfaces.


## 2026-04-03

### Documentation gap review protocol

**Author:** Amy Wong (DevOps/Platform/Azure)
**Status:** Proposed

After any bulk documentation edit (deduplication, restructuring, redirect creation), run a verification pass:

1. **Code block fences** — every opening ` has a matching close
2. **Link targets** — every [text](path) points to a file that exists and contains the expected content
3. **Redirect stubs** — any file converted to a stub must have its old inbound links verified
4. **Command correctness** — deployment/build commands in docs must match current infrastructure

Prevents broken rendering and incorrect instructions from reaching users after large-scale documentation changes.

### User directive

**By:** Steven Murawski (via Copilot)
**Date:** 2026-04-03T14:07:03Z

When asked for tenant IDs or domain names, use `C:\Users\stmuraws\source\gim-home\AdvocacyBami\data\tenants.psd1` as the lookup source, plus the hardcoded entry `72f988bf-86f1-41af-91ab-2d7cd011db47` for `microsoft.onmicrosoft.com`. User request — captured for team memory.
