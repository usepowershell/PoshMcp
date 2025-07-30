using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PoshMcp.Server.PowerShell;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests.Functional.ReturnType;

/// <summary>
/// Shared setup for return type tests
/// </summary>
public partial class GeneratedMethod : PowerShellTestBase
{
    public GeneratedMethod(ITestOutputHelper output) : base(output) { }
}
