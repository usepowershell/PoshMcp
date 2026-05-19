---
name: "cli-config-resolution"
description: "Resolve configuration file paths in CLI commands using the same shared pipeline as the doctor/diagnostics so all commands target the same effective config file. WHEN: adding a new CLI command that reads or writes config, debugging a command that mutates a different config file than doctor reports, or implementing config-aware CLI behavior in a multi-location config environment."
domain: "cli-design"
confidence: "medium"
source: "earned"
---

# Skill: CLI Config Resolution Parity

## Pattern

When adding CLI commands that read or write configuration, reuse the same resolution pipeline used by diagnostics (`doctor`) so all commands target the same effective file.

## Why it matters

- Prevents one command from mutating a different file than the one diagnostics reports.
- Reduces user confusion in environments with multiple possible config locations.
- Keeps behavior consistent across local runs, CI, and environment-variable overrides.

## Implementation guidance

1. Resolve config path through existing shared logic (`ResolveCommandSettingsAsync` / `ResolveConfigurationPathWithSourceAsync`).
2. Avoid introducing command-specific path heuristics unless explicitly required.
3. When command behavior mutates config, print the resolved path in output.
4. Add tests that run from a temp working directory and verify the expected file is changed.
