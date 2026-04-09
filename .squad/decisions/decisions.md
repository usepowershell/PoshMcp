# Decisions Ledger

## Active Decisions

### 2026-04-09T17:18Z: User directive — aggressive commit strategy
**By:** Steven Murawski (via Copilot)
**Status:** Active
**Decision:** Continue to cache status aggressively — commit after every logical chunk of work. Do not batch commits.
**Rationale:** Crash recovery protection. User request after losing in-flight work from Bender and Hermes during a crash.
**Impact:** All agents (Bender, Hermes, Scribe) should commit frequently. Pipeline analysis phases proceed with frequent state checkpoints.
**Created:** 2026-04-09T17:18Z
