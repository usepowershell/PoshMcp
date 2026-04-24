---
uid: troubleshooting
title: FAQ & Troubleshooting
---

# FAQ & Troubleshooting

Common issues and solutions.

## Installation & Setup

### "PoshMcp command not found"

**Solution:**

```bash
# Check installation
dotnet tool list --global

# Reinstall if needed
dotnet tool uninstall -g poshmcp
dotnet tool install -g poshmcp

# Verify
poshmcp --version
```

### No tools discovered

**Solution:**

```bash
# View configuration
cat ./appsettings.json

# Verify PowerShell access
pwsh -Command "Get-Command Get-Process"

# Add commands manually
poshmcp update-config --add-command Get-Process
poshmcp update-config --add-include-pattern "Get-*"
```

## Execution Issues

### Tool call fails

**Solution:**

```bash
# Enable debug logging
poshmcp serve --transport http --port 8080 --log-level debug

# Check exclude patterns
cat ./appsettings.json

# Test the command manually
pwsh -Command "Get-Process"
```

### Module installation fails

**Solution:**

```bash
# Check logs with debug level
poshmcp serve --transport stdio --log-level debug

# Test module installation manually
pwsh -Command "Install-Module Az.Accounts -Force"

# Add module to discovery list
poshmcp update-config --add-module Az.Accounts
```

### Slow performance

**Solution:**

```bash
# Enable result caching
poshmcp update-config --enable-result-caching true

# Pre-install modules in Docker
docker build --build-arg MODULES="Az.Accounts Az.Resources" -t poshmcp:fast .

# Use out-of-process mode (requires PowerShell 7)
export POSHMCP_RUNTIME_MODE=out-of-process
poshmcp serve
```

## Connection Issues

### Can't connect to server

**Solution:**

```bash
# Check if server is running
Get-NetTCPConnection -LocalPort 8080  # Windows
lsof -i :8080  # Linux/Mac

# Test connectivity
curl http://localhost:8080/health

# Check firewall
# (Allow port 8080 or configure reverse proxy)
```

## Docker Issues

### Container exits immediately

**Solution:**

```bash
# Check logs
docker run --rm poshmcp:latest 2>&1 | tail -20

# Run in interactive mode
docker run -it poshmcp:latest bash

# Inside container, test
poshmcp serve --transport http --log-level debug
```

## Authentication Issues

### 401 Unauthorized

**Solution:**

1. Check if authentication is enabled in `appsettings.json`
2. Send `Authorization: Bearer {token}` header with requests
3. Verify token isn't expired (check `exp` claim at jwt.io)

### Invalid token signature

**Solution:**

1. Acquire token from the correct Entra ID tenant
2. Use HTTPS for production (not HTTP)
3. Decode token at jwt.io and check `iss`, `aud`, `scp` claims

---

**See also:** [Getting Started](getting-started.md) | [Entra ID Authentication](authentication.md)

For more detailed troubleshooting, see user-guide.md in the repository.
