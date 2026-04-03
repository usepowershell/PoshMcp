# Environment customization — feature summary

> **📍 This is a changelog summary.** For the full user guide, see [Environment customization guide](ENVIRONMENT-CUSTOMIZATION.md). For developer integration steps, see [Implementation guide](IMPLEMENTATION-GUIDE.md).

## What was added

PoshMcp supports environment customization through `appsettings.json`, enabling:

1. **Startup scripts** — execute custom PowerShell code during initialization
2. **Module installation** — install modules from PowerShell Gallery or custom repositories
3. **Local module loading** — import modules from custom paths (volumes, network shares)

## Files overview

| Area | Files | Details |
|------|-------|---------|
| Core implementation | `EnvironmentConfiguration.cs`, `PowerShellEnvironmentSetup.cs`, `appsettings.environment-example.json` | Configuration classes and setup service |
| Documentation | [ENVIRONMENT-CUSTOMIZATION.md](ENVIRONMENT-CUSTOMIZATION.md), [IMPLEMENTATION-GUIDE.md](IMPLEMENTATION-GUIDE.md), [examples/README.md](../examples/README.md) | User guide, developer guide, examples |
| Examples | `startup.ps1`, `docker-compose.environment.yml`, `appsettings.*.json` | Working configurations with Docker Compose |

## Integration status

- ✅ Implementation files created
- ✅ Documentation written
- ⬜ Wire up the service in `Program.cs`
- ⬜ Update `PowerShellRunspaceInitializer`
- ⬜ Add unit tests

---

## See also

- [Environment customization guide](ENVIRONMENT-CUSTOMIZATION.md) — comprehensive user guide with use cases
- [Implementation guide](IMPLEMENTATION-GUIDE.md) — developer integration steps
- [Examples](../examples/) — Dockerfile templates and sample configurations
- [DESIGN.md](../DESIGN.md) — architecture overview
