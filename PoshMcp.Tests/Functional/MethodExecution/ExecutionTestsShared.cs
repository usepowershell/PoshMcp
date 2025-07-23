using PoshMcp.PowerShell;
using System;
using System.Linq;
using System.Management.Automation;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Functional.MethodExecution;

/// <summary>
/// Shared setup for method execution tests
/// </summary>
public partial class ExecutionTests : PowerShellTestBase
{
    public ExecutionTests(ITestOutputHelper output) : base(output) { }
}
