# Decisions

## Recent Decisions
> Older entries archived to `decisions-archive.md` (entries >7d removed when file >= 50KB).

### 2026-06-01T20:35:00Z: OAuth authorize proxy forwards only one Entra-compatible prompt value
**By:** Steven Murawski (via Bender)
**What:** `OAuthProxyEndpoints` normalizes `prompt` values before redirecting to Entra v2.0. Unsupported `create` is stripped. If a client sends multiple prompt tokens in one query value, the proxy forwards one supported value in this priority order: `consent`, `select_account`, `login`, `none`. If no supported token remains, `prompt` is omitted.
**Why:** Copilot CLI can send `prompt=consent select_account`, but Entra v2.0 returns `AADSTS90023` for space-separated combined prompt values. Preferring `consent` preserves the strongest client intent while producing an Entra-compatible authorize URL.

---

### 2026-06-01T20:20:00Z: OAuth authorize proxy strips unsupported prompt=create
**By:** Steven Murawski (via Bender)
**What:** The OAuth proxy `/authorize` endpoint strips `prompt=create` before redirecting to Entra v2.0, while preserving supported prompt values such as `select_account` unchanged.
**Why:** Entra v2.0 rejects `prompt=create` with AADSTS90023. Mapping it to `login`, `consent`, or `select_account` would change client intent, so omitting only the unsupported value keeps the existing flow as neutral as possible.

---

### 2026-06-01T19:39:02Z: Malformed config JSON exits as configuration error
**By:** Steven Murawski (via Bender)
**What:** Treat invalid runtime `appsettings.json` content discovered during `serve` settings resolution as a configuration error. The resolver wraps JSON parse failures with the config file path, `serve` catches that as `Configuration error`, and `Program.Main` returns the handler-set exit code when command-line parsing succeeds.
**Why:** Container startup can resolve `/app/appsettings.json` before server startup begins. A malformed mounted or working-directory config previously escaped the `serve` error boundary as an unhandled `JsonReaderException`, obscuring the path and making container failure noisy instead of actionable.

---

### 2026-06-01T19:12:44Z: OAuth DCR proxy echoes client metadata for Copilot CLI
**By:** Steven Murawski (via Bender)
**What:** The OAuth proxy `/register` endpoint should treat GitHub Copilot CLI registration requests as dynamic client registration metadata and return requested client metadata fields, especially `redirect_uris`, alongside the configured static `client_id`. The response also preserves `token_endpoint_auth_method: none` and may echo supplied `client_name`, `grant_types`, `response_types`, and `scope`.
**Why:** Copilot CLI validates the `/register` response body and fails authentication when `redirect_uris` is absent, even when the server returns HTTP 201. Echoing requested redirect URIs keeps the static-client proxy compatible with clients expecting an RFC 7591-shaped response while preserving Entra's configured client ID and public-client authentication method.

---

### 2026-06-01T00:00:00Z: v0.16.3 release remains blocked until test gate completes cleanly
**By:** Steven Murawski (via Amy)
**What:** Do not push `main`, create a release commit, or create/push tag `v0.16.3` yet. The version bump is present in `PoshMcp.Server/PoshMcp.csproj`, Leela prepared `CHANGELOG.md` and `docs/release-notes/0.16.3.md`, and the format gate completed cleanly, but the full `dotnet test PoshMcp.sln --no-restore` run stalled during `PoshMcp.Tests` execution and was stopped before a completion summary.
**Why:** The release process requires committed release artifacts plus green readiness gates before tag publication. Publishing now would bypass the test gate and leave the release notes/version bump uncommitted.

---

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
