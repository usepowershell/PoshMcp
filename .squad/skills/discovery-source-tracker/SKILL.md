---
name: "discovery-source-tracker"
description: "Implement discovery-source attribution tracking for MCP tool enumeration by creating a dedicated tracker interface with first-writer-wins semantics, recording at discovery call sites (not in the doctor/reporter), and wiring both in-process and OOP paths to agree on provenance. WHEN: the doctor or a reporting surface infers tool source heuristically, adding source attribution to a new discovery path, or aligning in-process and OOP discovery provenance."
domain: "api-design"
confidence: "high"
source: "earned"
---

## Context
Use this when a doctor/reporting surface needs authoritative provenance for discovered tools, but the existing implementation is inferring source after the fact. It is especially useful when both in-process and out-of-process discovery paths must agree byte-for-byte.

## Patterns
- Create a dedicated tracker interface that matches the existing provenance seam shape (`RecordToolSource(...)`, snapshot property, first-writer-wins semantics).
- Populate the tracker at the discovery call sites, not in the doctor/reporting consumer.
- In-process: record during the existing `Get-Command` enumeration loops so no extra probes are added.
- Out-of-process: read the wire-format `Source*` fields directly and translate missing fields to `unknown` rather than inferring.
- Make the doctor/reporting consumer depend on the tracker and treat missing entries as unknown.

## Examples
- `PoshMcp.Server/PowerShell/IToolImportSourceTracker.cs`
- `PoshMcp.Server/McpToolFactoryV2.cs`
- `PoshMcp.Server/Diagnostics/DoctorService.cs`
- `PoshMcp.Tests/Integration/ToolImportParityTests.cs`

## Anti-Patterns
- Re-running discovery commands inside doctor/report generation just to recover provenance.
- Keeping an old heuristic alive once authoritative data is available.
- Treating older OOP hosts as if they had authoritative source data.
