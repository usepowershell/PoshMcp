using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using PoshMcp.Server.PowerShell;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Functional.McpServerSetup;

/// <summary>
/// Shared setup for MCP server setup tests
/// </summary>
public partial class SetupTests : PowerShellTestBase
{
    public SetupTests(ITestOutputHelper output) : base(output) { }
}
