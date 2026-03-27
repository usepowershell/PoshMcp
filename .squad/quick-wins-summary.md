# Quick Wins Implementation Summary

**Branch:** feature/quick-wins-observability-resilience  
**Timeline:** 2-3 weeks  
**Lead Architect:** Farnsworth

## Implementation Plan Overview

### Phase 1: Foundation (Week 1) - Parallel Tracks

#### Track A: Health Check Endpoint
- **Lead:** Amy  
- **Support:** Hermes (PowerShell health checks), Fry (testing)
- **Deliverable:** `/health` and `/health/ready` endpoints with PowerShell runspace health status
- **Pattern:** ASP.NET Core IHealthCheck implementation

#### Track B: Correlation IDs  
- **Lead:** Amy
- **Support:** Bender (integration), Fry (testing)
- **Deliverable:** Correlation ID middleware, AsyncLocal propagation, X-Correlation-ID headers
- **Pattern:** Middleware + AsyncLocal<string> + structured logging scopes

---

### Phase 2: Error Infrastructure (Week 2)

#### Structured Error Codes
- **Lead:** Bender
- **Support:** Hermes (PowerShell error mapping), Amy (metrics), Fry (testing)
- **Dependencies:** Correlation IDs complete
- **Deliverable:** McpErrorCode enum, McpException hierarchy, error code tracking
- **Pattern:** Custom exception base class with error codes and correlation IDs

---

### Phase 3: Resilience & Validation (Week 2-3) - Parallel Tracks

#### Track A: Configuration Validation
- **Lead:** Bender
- **Support:** Hermes (PowerShell rules), Fry (testing)
- **Dependencies:** Structured error codes
- **Deliverable:** IValidateOptions<T> validators, startup validation, clear error messages
- **Pattern:** Fail-fast validation on startup

#### Track B: Command Execution Timeouts
- **Lead:** Hermes
- **Support:** Bender (config integration), Amy (metrics), Fry (testing)
- **Dependencies:** Configuration validation (for timeout settings), Structured error codes
- **Deliverable:** Configurable timeouts, graceful timeout handling, timeout metrics
- **Pattern:** Task.WaitAsync + CancellationTokenSource with cleanup

---

## Key Architectural Decisions

1. **Use existing ASP.NET Core patterns** - Health checks via IHealthCheck, validation via IValidateOptions
2. **Correlation ID propagation** - AsyncLocal<T> ensures propagation across async boundaries
3. **Error code hierarchy** - Categorized by subsystem (1xxx config, 2xxx execution, 3xxx runspace, 4xxx params)
4. **Fail-fast configuration** - Invalid config prevents server startup
5. **Per-command timeouts** - Default 5min, max 30min, configurable per command eventually

## Integration Points

### Stdio Mode (PoshMcp.Server)
- ✅ Correlation IDs (logged only)
- ✅ Timeout handling
- ✅ Error codes
- ✅ Config validation
- ❌ Health checks (N/A for stdio)

### HTTP Mode (PoshMcp.Web)
- ✅ All features including health checks
- ✅ Correlation ID in response headers

## Configuration Schema Additions

```json
{
  "PowerShellConfiguration": {
    "DefaultCommandTimeout": "00:05:00",
    "MaxCommandTimeout": "00:30:00",
    // ... existing config
  }
}
```

## Files to Create/Modify

### New Files
- `PoshMcp.Server/Exceptions/McpException.cs`
- `PoshMcp.Server/Exceptions/McpErrorCode.cs`
- `PoshMcp.Server/Middleware/CorrelationIdMiddleware.cs`
- `PoshMcp.Server/Infrastructure/CorrelationContext.cs`
- `PoshMcp.Server/HealthChecks/PowerShellRunspaceHealthCheck.cs`
- `PoshMcp.Server/Configuration/PowerShellConfigurationValidator.cs`

### Modified Files
- `PoshMcp.Server/PowerShell/PowerShellRunspaceHolder.cs` (timeout support)
- `PoshMcp.Server/PowerShell/PowerShellAssemblyGenerator.cs` (timeout integration)
- `PoshMcp.Web/Program.cs` (health checks, middleware)
- `PoshMcp.Server/Program.cs` (config validation)
- `PoshMcp.Server/Metrics/McpMetrics.cs` (timeout metrics, error code metrics)

## Testing Requirements

- **Health Checks:** Endpoint returns 200 OK when healthy, includes PowerShell status
- **Correlation IDs:** Propagate through all async operations, appear in all logs
- **Error Codes:** All exception paths assign correct error codes
- **Config Validation:** Invalid configs fail startup with clear messages
- **Timeouts:** Commands respect timeout, runspace stable after timeout

## Success Metrics

1. **Health Checks:** < 500ms response time, suitable for K8s probes
2. **Correlation IDs:** 100% of logs include correlation ID
3. **Error Codes:** 100% of exceptions have structured codes
4. **Config Validation:** Catches 100% of invalid configurations at startup
5. **Timeouts:** No runspace corruption after timeout events

## Next Steps

1. **Farnsworth:** Present plan to team, get approval/feedback
2. **Week 1:** Amy kicks off health checks + correlation IDs in parallel
3. **Week 2:** Bender implements error codes
4. **Week 2-3:** Hermes implements timeouts, Bender implements validation
5. **Throughout:** Fry provides test coverage for each completed feature

## Decision Record

Full architectural details and rationale: `.squad/decisions/inbox/farnsworth-quick-wins-plan.md`
