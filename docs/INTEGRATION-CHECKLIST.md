# Environment Customization - Integration Checklist

Use this checklist to integrate environment customization into PoshMcp.

## Phase 1: Review & Understand ✅

- [ ] Read [ENVIRONMENT-CUSTOMIZATION-SUMMARY.md](ENVIRONMENT-CUSTOMIZATION-SUMMARY.md)
- [ ] Review implementation files:
  - [ ] `PowerShell/EnvironmentConfiguration.cs`
  - [ ] `PowerShell/PowerShellEnvironmentSetup.cs`
- [ ] Understand the execution flow (see diagram in IMPLEMENTATION-GUIDE.md)

## Phase 2: Code Integration ⬜

### 2.1: Register Services

**File:** `PoshMcp.Server/Program.cs` and `PoshMcp.Web/Program.cs`

- [ ] Add service registration after PowerShellConfiguration binding:
  ```csharp
  builder.Services.AddSingleton<PowerShellEnvironmentSetup>();
  ```

- [ ] Verify configuration binding includes Environment section:
  ```csharp
  builder.Services.Configure<PowerShellConfiguration>(
      builder.Configuration.GetSection("PowerShellConfiguration"));
  ```

### 2.2: Update PowerShellRunspaceInitializer

**File:** `PoshMcp.Server/PowerShell/PowerShellRunspaceInitializer.cs`

- [ ] Add async version of CreateInitializedRunspace:
  ```csharp
  public static async Task<PSPowerShell> CreateInitializedRunspaceAsync(
      EnvironmentConfiguration? envConfig = null,
      ILogger? logger = null,
      string? customScript = "")
  ```

- [ ] Add environment setup call in the async method
- [ ] Keep existing synchronous method for backward compatibility
- [ ] Test that both methods work

### 2.3: Update PowerShellRunspaceHolder

**File:** `PoshMcp.Server/PowerShell/PowerShellRunspaceHolder.cs`

- [ ] Add static fields for logger and config:
  ```csharp
  private static ILogger<PowerShellRunspaceHolder>? _logger;
  private static EnvironmentConfiguration? _envConfig;
  ```

- [ ] Add Initialize method:
  ```csharp
  public static void Initialize(
      ILogger<PowerShellRunspaceHolder> logger,
      EnvironmentConfiguration envConfig)
  ```

- [ ] Update Lazy initialization to use async method
- [ ] Handle Task.GetAwaiter().GetResult() for synchronous initialization

### 2.4: Wire Up in Startup

**File:** `PoshMcp.Server/Program.cs` (after builder.Build())

- [ ] Get environment configuration:
  ```csharp
  var envConfig = configuration
      .GetSection("PowerShellConfiguration:Environment")
      .Get<EnvironmentConfiguration>() ?? new EnvironmentConfiguration();
  ```

- [ ] Create logger for PowerShellRunspaceHolder
- [ ] Call PowerShellRunspaceHolder.Initialize with logger and config

**File:** `PoshMcp.Web/Program.cs` (similar changes for web server)

- [ ] Apply same changes as stdio server
- [ ] Ensure web-specific initialization still works

## Phase 3: Testing ⬜

### 3.1: Unit Tests

**File:** `PoshMcp.Tests/Unit/PowerShellEnvironmentSetupTests.cs` (new file)

- [ ] Test module installation
- [ ] Test module import
- [ ] Test module path configuration
- [ ] Test startup script execution
- [ ] Test error handling
- [ ] Test timeout scenarios

### 3.2: Integration Tests

**File:** `PoshMcp.Tests/Integration/EnvironmentSetupIntegrationTests.cs` (new file)

- [ ] Test full server startup with environment config
- [ ] Test module availability after startup
- [ ] Test startup script side effects
- [ ] Test with appsettings.basic.json
- [ ] Test with appsettings.advanced.json

### 3.3: Manual Testing

- [ ] Build the project: `dotnet build`
- [ ] Run with basic config:
  ```bash
  cp examples/appsettings.basic.json PoshMcp.Server/appsettings.json
  dotnet run --project PoshMcp.Server
  ```
- [ ] Check logs for environment setup messages
- [ ] Verify modules are loaded
- [ ] Test Docker scenarios from examples/

## Phase 4: Configuration Updates ⬜

### 4.1: Default Configurations

- [ ] Update `PoshMcp.Server/appsettings.json` with empty Environment section
- [ ] Update `PoshMcp.Server/appsettings.Development.json` if needed
- [ ] Update `PoshMcp.Web/appsettings.json` with empty Environment section

### 4.2: Example Configurations

- [ ] Verify all example configs are valid
- [ ] Test each example configuration
- [ ] Update configs based on testing results

### 4.3: Documentation Updates

- [ ] Update inline code comments
- [ ] Add XML documentation to public methods
- [ ] Update API documentation if generated

## Phase 5: Docker Integration ⬜

### 5.1: Dockerfile Updates (Optional)

- [ ] Consider adding sample modules to base image
- [ ] Document module pre-installation in DOCKER.md
- [ ] Test Docker builds with new configuration

### 5.2: Docker Compose Testing

- [ ] Test examples/docker-compose.environment.yml
- [ ] Verify all three scenarios work:
  - [ ] Basic (runtime install)
  - [ ] Advanced (Azure + custom modules)
  - [ ] Multi-tenant
- [ ] Test volume mounts work correctly
- [ ] Test environment variables are passed correctly

## Phase 6: Health Checks (Optional) ⬜

### 6.1: Add Environment Setup Health Check

**File:** `PoshMcp.Server/Health/EnvironmentSetupHealthCheck.cs` (new)

- [ ] Create health check class
- [ ] Track last environment setup result
- [ ] Report degraded if setup had warnings
- [ ] Report unhealthy if setup failed

**File:** `Program.cs`

- [ ] Register health check:
  ```csharp
  builder.Services.AddHealthChecks()
      .AddCheck<EnvironmentSetupHealthCheck>("environment_setup");
  ```

## Phase 7: Documentation Review ⬜

- [ ] Review updated README.md
- [ ] Review ENVIRONMENT-CUSTOMIZATION.md for accuracy
- [ ] Review IMPLEMENTATION-GUIDE.md for completeness
- [ ] Review examples/README.md
- [ ] Verify all links work
- [ ] Check code examples for correctness

## Phase 8: Performance Testing ⬜

### 8.1: Startup Time Measurement

- [ ] Measure baseline startup time (no environment config)
- [ ] Measure startup with ImportModules only
- [ ] Measure startup with InstallModules
- [ ] Measure startup with full configuration
- [ ] Document findings

### 8.2: Optimization

- [ ] Identify bottlenecks
- [ ] Consider parallel module operations if needed
- [ ] Adjust default timeouts based on findings
- [ ] Document recommended configurations

## Phase 9: Security Review ⬜

- [ ] Review startup script execution security
- [ ] Verify module installation security
- [ ] Check volume mount recommendations
- [ ] Review publisher check settings
- [ ] Audit logging coverage
- [ ] Test with restricted permissions

## Phase 10: Deployment Preparation ⬜

### 10.1: Migration Guide

- [ ] Create upgrade guide for existing deployments
- [ ] Document breaking changes (if any)
- [ ] Provide migration examples
- [ ] Test upgrade path

### 10.2: Production Readiness

- [ ] Test in staging environment
- [ ] Create production configuration examples
- [ ] Document rollback procedure
- [ ] Prepare monitoring/alerting for setup failures

### 10.3: Team Communication

- [ ] Update team documentation
- [ ] Prepare demo/training materials
- [ ] Communicate changes to stakeholders
- [ ] Schedule knowledge transfer sessions

## Validation Checklist ✅

Before considering integration complete:

- [ ] All unit tests pass
- [ ] All integration tests pass
- [ ] Manual testing completed successfully
- [ ] Docker examples work correctly
- [ ] Documentation is accurate and complete
- [ ] Performance is acceptable
- [ ] Security review completed
- [ ] No breaking changes to existing functionality
- [ ] Backward compatibility maintained

## Issues & Questions

Document any issues or questions that arise during integration:

- **Issue 1:** [Description] - [Resolution]
- **Issue 2:** [Description] - [Resolution]
- **Question 1:** [Question] - [Answer]

## Sign-Off

- [ ] Development Lead: ____________________  Date: ________
- [ ] QA Lead: ____________________  Date: ________
- [ ] DevOps Lead: ____________________  Date: ________

---

**Notes:**
- This checklist can be tracked in GitHub Issues or Project Board
- Each checkbox represents a discrete task
- Update this file as integration progresses
- Keep this checklist in version control as part of the feature documentation
