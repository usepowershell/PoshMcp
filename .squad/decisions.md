# Decisions Log

Canonical record of decisions, actions, and outcomes.


## 2026-04-03

### Documentation gap review protocol

**Author:** Amy Wong (DevOps/Platform/Azure)
**Status:** Proposed

After any bulk documentation edit (deduplication, restructuring, redirect creation), run a verification pass:

1. **Code block fences** — every opening  + '`' +  has a matching close
2. **Link targets** — every [text](path) points to a file that exists and contains the expected content
3. **Redirect stubs** — any file converted to a stub must have its old inbound links verified
4. **Command correctness** — deployment/build commands in docs must match current infrastructure

Prevents broken rendering and incorrect instructions from reaching users after large-scale documentation changes.
