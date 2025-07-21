#!/bin/bash
cd "$(dirname "$0")/PoshMcpServer"
rm -f Program.cs PowerShellRunspaceHolder.cs PowerShellCleanupService.cs Program2.cs
mv ProgramClean.cs Program.cs
echo "Files cleaned up successfully"
