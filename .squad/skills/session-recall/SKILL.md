---
name: "session-recall"
description: "Use the session-recall CLI to recover working context at coordinator startup after crashes, compaction, or new sessions"
domain: "workflow-recovery"
confidence: "high"
source: "manual"
tools:
  - name: "powershell"
    description: "Run session-recall CLI commands to query session history"
    when: "Coordinator startup when now.md shows active work, or after any suspected interruption"
---

## Context

The `session-recall` CLI provides fast, structured access to Copilot CLI session history. It is the **preferred** recovery tool when available (see Fallback section for the SQL alternative). Use it at coordinator startup to reconstruct context — which files were in flight, where work stopped, and what the last checkpoint said — before spawning agents.

Run session-recall commands **only at coordinator startup**. Do not run them on every agent spawn or in the middle of active work.

## Patterns

### 1. Startup Sequence (lean — 2–3 commands only)

Run these at session start when `now.md` shows active work or after any suspected crash/compaction:

```bash
# 1. Recent sessions — find out what was happening
session-recall list --json --limit 5

# 2. Recently touched files — understand what was in flight
session-recall files --json --limit 10

# 3. Last checkpoints — see where work stopped (run only if sessions found above)
session-recall checkpoints --days 3
```

Do **not** run all available commands every startup. The three above are sufficient for a cold-start brief.

### 2. Drill Into a Specific Session

If `list` returns a session that looks relevant, inspect it fully before briefing agents:

```bash
session-recall show <id> --json
```

Use the `summary`, checkpoint titles, and file list from this output to build the agent spawn prompt.

### 3. Targeted Search

When the coordinator needs to find work on a specific topic or file:

```bash
session-recall search '<term>' --json               # full-text search all history
session-recall search '<term>' --days 5             # search last 5 days only
session-recall files --days 7 --json                # files touched in last 7 days
```

### 4. Health Check (first run / diagnostics only)

Run once if session-recall itself behaves unexpectedly:

```bash
session-recall health --json        # 8-dimension health check
session-recall schema-check         # validate DB schema
```

### 5. Passing Context to Spawned Agents

Include session-recall output in spawn prompts as follows:

- **Recent files list** → `INPUT ARTIFACTS` block in the spawn prompt
  ```
  INPUT ARTIFACTS (from session-recall):
  - src/Foo.cs (last touched 2h ago)
  - appsettings.json (last touched 2h ago)
  ```

- **Last checkpoint** → a `PRIOR CONTEXT` block near the top of the spawn prompt
  ```
  PRIOR CONTEXT (last checkpoint):
  "Implementing runspace pooling — wrote AcquireRunspace(), TODO: ReleaseRunspace() and tests"
  ```

- **Session summary** → one sentence before the task description
  ```
  Previous session was working on: <summary from session-recall show>
  ```

Keep the context block concise. Agents do not need the full JSON dump — extract the essential facts.

## Fallback

If `session-recall` is unavailable or returns an error:

1. **Continue silently** — session-recall is a convenience, not a blocker. Do not halt coordinator startup.
2. **Fall back to SQL patterns** in `.squad/templates/skills/session-recovery/SKILL.md`:
   - Use the `sql` tool with `database: "session_store"`
   - Query the `sessions`, `checkpoints`, `session_files`, and `search_index` tables directly
   - The SQL skill has full patterns for recent sessions, FTS5 search, and orphaned issue detection

The relationship: **session-recall wraps the same underlying data** as the SQL skill — it just provides a cleaner CLI interface. When both are available, prefer session-recall. When session-recall errors, the SQL fallback reads the same store.

## Anti-Patterns

- ❌ Running session-recall commands on every agent spawn — run at coordinator startup only
- ❌ Blocking work or failing loud when session-recall errors — it's a convenience layer; continue without it
- ❌ Dumping full raw JSON into spawn prompts — extract the relevant facts; keep prompts lean
- ❌ Running all commands at every startup — the 3-command startup sequence is sufficient; run `show` and `search` only when needed
- ❌ Using session-recall output as a substitute for reading actual file state — it tells you *what was touched*, not current content; always read current files before making changes
