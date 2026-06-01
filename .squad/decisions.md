# Decisions

## Recent Decisions
> Older entries archived to `decisions-archive.md` (entries >7d removed when file >= 50KB).

### 2026-06-01T00:00:00Z: Auth diagnostics use safe claim allowlist
**By:** Steven Murawski (via Bender)
**What:** Validated JWT diagnostic logging should report only auth-relevant safe fields: audience, scope/scp, roles/role, and issuer. The previous all-claims diagnostic is removed so arbitrary token claim names or values are not written to logs.
**Why:** Operators still need enough context to diagnose audience, scope, role, and issuer mismatches, but arbitrary claim logging can disclose token, user, tenant, or other sensitive details.

---

### 2026-06-01T00:00:00Z: PoshMcp scenario Container Apps Terraform uses prebuilt images
**By:** Steven Murawski (via Amy)
**What:** The generated `infra/` Terraform provisions Azure Container Apps scenario infrastructure but does not build or push container images during Terraform apply. Image references are configurable globally and per scenario, with optional ACR creation and managed identity-based ACR pull.
**Why:** Keeping image build/push outside Terraform makes the scenario environment easier to use from local, CI, and release workflows without coupling `terraform apply` to Docker/Podman availability or registry admin credentials.

---

---

### 2026-06-01T00:00:00Z: Plan-first Terraform for multi-scenario Container Apps
**By:** Steven Murawski (via Amy)
**What:** Prepare Azure Container Apps Terraform through `.azure/deployment-plan.md` first, with `infra/` generation blocked until user approval. The proposed shape uses shared core resources, optional identity/auth, and a scenario map to create multiple PoshMcp Container Apps for basic, advanced, Azure, and auth testing.
**Why:** The Azure prepare workflow requires an approved plan before infrastructure artifacts are generated, and scenario testing needs repeatable multi-instance deployment without hard-coding one app per Terraform file.

---

---

### 2026-06-01T00:00:00Z: App Insights suppresses metrics console exporter
**By:** Steven Murawski (via Farnsworth)
**What:** When Application Insights is enabled and has a resolved connection string, HTTP OpenTelemetry metrics should export through Azure Monitor and must not also register the console exporter. Console metric export remains the fallback when App Insights is disabled or not fully configured.
**Why:** Duplicate console metric output makes operator logs harder to read in App Insights-backed deployments, while preserving console metrics keeps local/default observability behavior intact.

---

---

### 2026-05-28: MCP prompts/get enforces required args and renders templates post-source
**By:** Steven (via Bender)

**Decision:** For `prompts/get`, validate required prompt arguments from prompt metadata (`Arguments[].Required`) before file or command source execution. Missing or empty required args return MCP `InvalidParams` with the missing argument names. Render template placeholders only after source text is produced for both file-backed and command-backed prompts, supporting `{{argName}}` and backward-compatible `{argName}` replacement.

**Why:** Align prompt behavior with expected templating semantics while preserving command argument variable injection into PowerShell execution.
