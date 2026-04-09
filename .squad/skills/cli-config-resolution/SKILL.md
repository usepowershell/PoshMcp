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
