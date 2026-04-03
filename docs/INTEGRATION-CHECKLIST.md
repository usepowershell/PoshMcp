# Environment customization — integration checklist

> **📍 This checklist supplements the [Implementation guide](IMPLEMENTATION-GUIDE.md).** Use both together when integrating the environment customization feature.

## Phase 1: Review and understand

- [ ] Read the [environment customization guide](ENVIRONMENT-CUSTOMIZATION.md)
- [ ] Review implementation files:
  - [ ] `PowerShell/EnvironmentConfiguration.cs`
  - [ ] `PowerShell/PowerShellEnvironmentSetup.cs`
- [ ] Understand the execution flow (see diagram in [Implementation guide](IMPLEMENTATION-GUIDE.md))

## Phase 2: Code integration

### Register services

**Files:** `PoshMcp.Server/Program.cs` and `PoshMcp.Web/Program.cs`

- [ ] Add `builder.Services.AddSingleton<PowerShellEnvironmentSetup>()`
- [ ] Verify configuration binding includes the `Environment` section

### Update runspace initializer

**File:** `PoshMcp.Server/PowerShell/PowerShellRunspaceInitializer.cs`

- [ ] Add async version of `CreateInitializedRunspace`
- [ ] Add environment setup call in the async method
- [ ] Keep existing synchronous method for backward compatibility

### Update runspace holder

**File:** `PoshMcp.Server/PowerShell/PowerShellRunspaceHolder.cs`

- [ ] Add static fields for logger and config
- [ ] Add `Initialize` method
- [ ] Update lazy initialization to use async method

### Wire up in startup

**File:** `PoshMcp.Server/Program.cs` (after `builder.Build()`)

- [ ] Get `EnvironmentConfiguration` from configuration
- [ ] Create logger for `PowerShellRunspaceHolder`
- [ ] Call `PowerShellRunspaceHolder.Initialize` with logger and config

## Phase 3: Testing

- [ ] Add unit tests in `PoshMcp.Tests/Unit/PowerShellEnvironmentSetupTests.cs`
- [ ] Add integration tests in `PoshMcp.Tests/Integration/EnvironmentSetupIntegrationTests.cs`
- [ ] Manual test: build, run with `examples/appsettings.basic.json`, check logs

## Phase 4: Verification

- [ ] All unit tests pass
- [ ] All integration tests pass
- [ ] Docker examples work correctly
- [ ] No breaking changes to existing functionality

---

## See also

- [Implementation guide](IMPLEMENTATION-GUIDE.md) — full developer integration instructions
- [Environment customization guide](ENVIRONMENT-CUSTOMIZATION.md) — user-facing documentation
- [Examples](../examples/) — sample configurations and Docker Compose files
