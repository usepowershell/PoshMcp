# OOP cancellation and reload isolation

Interrupted subprocess-pool workers are quarantined and replaced rather than reused. Configuration reload advances a pool generation: idle workers are configured immediately, active workers complete their existing request and retire on return, and replacements inherit the latest cached configuration. This prevents setup frames from being sent into a worker with an active command.
