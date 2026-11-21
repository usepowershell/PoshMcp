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
