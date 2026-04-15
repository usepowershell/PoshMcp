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

# Session timeout (minutes)
export POSHMCP_SESSION_TIMEOUT_MINUTES=120

# Docker module pre-install
export POSHMCP_MODULES="Az.Accounts Az.Resources"
```

### Startup Scripts

Run custom PowerShell code at server startup:

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
poshmcp update-config --add-install-module Az.Accounts --minimum-version 2.0.0
poshmcp update-config --add-import-module Az.Accounts
poshmcp update-config --add-module-path /mnt/custom-modules
```

---

**See also:** ENVIRONMENT-CUSTOMIZATION.md in the repository for comprehensive guide
