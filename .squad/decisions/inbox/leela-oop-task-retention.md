# Bounded OOP cleanup tracking

## 2026-07-16

Out-of-process cleanup retains at most `MaxTrackedCleanupOperations` incomplete tasks (default 16) for shutdown waiting. Every cleanup task gets a completion observer, including untracked overflow tasks: completed entries are removed, faults are observed and logged, and overflow is logged explicitly. This prevents repeated stuck-worker replacement from retaining an unbounded task or exception collection while preserving bounded disposal waits and failure visibility.