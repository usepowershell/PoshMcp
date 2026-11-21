# P0 fresh foundation

This isolated source area implements **foundations and feasibility only**, not a
production MCP server. It has no reference to the legacy server, PowerShell SDK,
runspaces, sessions, or generated tool assemblies. See
[the P0 decisions and qualification ledger](../docs/p0-foundations.md).

Build from this directory with the pinned .NET SDK: `dotnet build`.
The .NET 11 runtime and separately provisioned PowerShell 7.6.0 are prerequisites.
Windows PowerShell 5.1 must be explicitly selected on qualified Windows x64.

The four independent .NET-tool package IDs are
`PoshMcp.Foundation.win-x64`, `PoshMcp.Foundation.win-arm64`,
`PoshMcp.Foundation.linux-x64`, and `PoshMcp.Foundation.linux-arm64`.
Install exactly the matching ID and version, then run
`poshmcp-foundation --foundation-info`. All use the distinct command
`poshmcp-foundation`; they do not replace the existing `poshmcp` release line.
No selector/companion package or embedded PowerShell installation is needed.
Wrong-host packages fail explicitly. Managed code portability is not native
runtime qualification.

Trusted worker scripts are assembly resources, extracted to a per-process
temporary directory for external `-File` execution and removed on normal
shutdown. Only script source is embedded, **not** the PowerShell runtime.
No request data or credentials are written there. A crashing supervisor can
leave this non-secret fixture bundle for ordinary host temp cleanup.

On a provisioned matching host, installation qualification also runs:
`poshmcp-foundation --probe /absolute/path/to/pwsh PowerShell7`.
Use `WindowsPowerShell51` explicitly on qualified Windows x64; no fallback.
The Linux x64 package has been packed, locally installed, and tested. Other
three native packages remain pending, not established by that Linux run.

Shared commands: `python eng/validate.py unit`, `all`, or `lint`.
`all` requires `POSHMCP_TEST_RUNTIME` to name the absolute external executable.
`lint` needs PSScriptAnalyzer 1.24.0 and `POSHMCP_LINT_RUNTIME` (or `pwsh` on PATH).
Set `POSHMCP_TEST_MODULE_PATH` for pinned modules provisioned outside the repo.
`python eng/verify-gates.py` runs five deliberately failing controls in a
disposable clone. P0 PR/main/tag workflows are implemented; actual native
matrix and registry execution still need qualification and administrator
setup documented in the ledger. No tag or package was manually published.
