# Session Log: deploy.ps1 ACR EOF hardening

**Timestamp:** 2026-04-08T00:00:00Z  
**Requested by:** Steven Murawski  
**Topic:** Harden ACR authentication flow in deployment script

## Summary
Recorded session outcomes for deployment reliability hardening in `infrastructure/azure/deploy.ps1`.

## Notes
- Deploy script now treats intermittent ACR OAuth EOF/network failures as transient.
- `az acr login` and Docker push operations use bounded retries with exponential backoff.
- Failure output is trimmed and surfaced with clearer diagnostics to speed troubleshooting.
- ACR endpoint reachability probing was added before login/push to separate network failures from auth issues.

## Decision Inbox Check
- Reviewed `.squad/decisions/inbox/` for ACR EOF fix decisions.
- No inbox decision files were relevant to this specific work, so no merge or inbox cleanup was performed.

## Status
- Completed documentation/logging updates under `.squad/` only.
- No product code modified by Scribe session logging actions.
