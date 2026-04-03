# Decision: Documentation gap review protocol

**Date:** 2026-04-03
**Author:** Amy Wong (DevOps/Platform/Azure)
**Status:** Proposed

## Context

During the team's major documentation cleanup (removing ~3,100 lines of duplication across 23 files), 4 issues were introduced that would have caused user-facing problems:

1. An unclosed code block in DESIGN.md that hid all content below the architecture diagram
2. Broken code block structure in DOCKER.md that garbled the Docker commands section
3. A stale reference to EXAMPLES.md (now a redirect stub) in ARCHITECTURE.md
4. An incorrect deployment command in the test README that would fail due to the Bicep modularization

## Decision

After any bulk documentation edit (deduplication, restructuring, redirect creation), the team should run a verification pass that checks:

1. **Code block fences** — every opening ` ``` ` has a matching close
2. **Link targets** — every `[text](path)` points to a file that exists and contains the expected content
3. **Redirect stubs** — any file converted to a stub must have its old inbound links verified
4. **Command correctness** — deployment/build commands in docs must match current infrastructure (e.g., `az deployment sub create` vs `group create`)

## Impact

Prevents broken rendering and incorrect instructions from reaching users after large-scale documentation changes.
