using System.Linq;
using System.Management.Automation;
using System.Threading.Tasks;
using Xunit;

namespace PoshMcp.Tests.Functional.GeneratedAssembly;

public partial class GeneratedInstance : PowerShellTestBase
{
    [Fact]
    public async Task GeneratedMethods_ShouldIncludeFrameworkParametersAndCancellationTokenLast()
    {
        var command = await PowerShellRunspace.ExecuteThreadSafeAsync<CommandInfo?>(ps =>
        {
            ps.Commands.Clear();
            ps.AddCommand("Get-Command").AddParameter("Name", "Get-Process");
            var cmd = ps.Invoke<CommandInfo>().FirstOrDefault();
            ps.Commands.Clear();
            return Task.FromResult(cmd);
        });

        Assert.NotNull(command);

        _ = AssemblyGenerator.GenerateAssembly(new[] { command! }, Logger);
        var methods = AssemblyGenerator.GetGeneratedMethods()
            .Where(m => m.Key.StartsWith("get_process", System.StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.NotEmpty(methods);

        foreach (var method in methods)
        {
            var parameters = method.Value.GetParameters();

            Assert.True(parameters.Length >= 4);
            Assert.Equal("_AllProperties", parameters[parameters.Length - 4].Name);
            Assert.Equal("_MaxResults", parameters[parameters.Length - 3].Name);
            Assert.Equal("_RequestedProperties", parameters[parameters.Length - 2].Name);
            Assert.Equal("cancellationToken", parameters[parameters.Length - 1].Name);
        }
    }
}
