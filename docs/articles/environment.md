---
uid: environment
title: Environment Customization
---

# Environment Customization

Configure PoshMcp's runtime environment for your specific use case.

For detailed environment customization guide, see [ENVIRONMENT-CUSTOMIZATION.md](../archive/ENVIRONMENT-CUSTOMIZATION.md) in the repository.

## Quick Reference

### Environment Variables

```bash
# Transport mode
export POSHMCP_TRANSPORT=http

# Log level
export POSHMCP_LOG_LEVEL=debug

# Configuration file path
export POSHMCP_CONFIGURATION=/config/appsettings.json

# Docker module pre-install
export POSHMCP_MODULES="Az.Accounts Az.Resources"
```

### Startup Scripts

Runs custom PowerShell **per pooled runspace worker** at warm-up and
replenishment (not once at server startup). See the
[Startup Scripts Guide](startup-scripts.md) for idempotency requirements:

```json
{
  "PowerShellConfiguration": {
    "Environment": {
      "StartupScript": "$Global:CompanyName = 'Acme'"
    }
  }
}
```

Or load from a file:

```json
{
  "PowerShellConfiguration": {
    "Environment": {
      "StartupScriptPath": "/config/startup.ps1"
    }
  }
}
```

### Module Installation

```bash
poshmcp update-config --add-module Az.Accounts
```

---

**See also:** ENVIRONMENT-CUSTOMIZATION.md in the repository for comprehensive guide
