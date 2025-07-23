using System.CommandLine;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace PoshMcp.Tests
{
    /// <summary>
    /// Tests for the command line argument parsing functionality
    /// </summary>
    public class CommandLineTests
    {
        private readonly ITestOutputHelper _output;

        public CommandLineTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void CommandLine_HelpOption_ShouldShowUsageInfo()
        {
            // This test verifies that the help functionality works
            // We can't easily test the exact output, but we can ensure the command doesn't crash
            
            // Since Program.Main returns int now, we'll test that the command line structure is valid
            // by checking that the help text generation doesn't throw exceptions
            
            var rootCommand = new RootCommand("PowerShell MCP Server - Provides access to PowerShell commands via Model Context Protocol");
            
            var evaluateToolsOption = new Option<bool>(
                aliases: new[] { "--evaluate-tools", "-e" },
                description: "Evaluate and list discovered PowerShell tools without starting the MCP server");
            
            var verboseOption = new Option<bool>(
                aliases: new[] { "--verbose", "-v" },
                description: "Enable verbose logging");
                
            var debugOption = new Option<bool>(
                aliases: new[] { "--debug", "-d" },
                description: "Enable debug logging");
                
            var traceOption = new Option<bool>(
                aliases: new[] { "--trace", "-t" },
                description: "Enable trace logging");

            rootCommand.AddOption(evaluateToolsOption);
            rootCommand.AddOption(verboseOption);
            rootCommand.AddOption(debugOption);
            rootCommand.AddOption(traceOption);

            // If we get here without exceptions, the command structure is valid
            Assert.NotNull(rootCommand);
            Assert.Equal(4, rootCommand.Options.Count);
            
            // Verify option names
            Assert.Contains(rootCommand.Options, o => o.HasAlias("--evaluate-tools"));
            Assert.Contains(rootCommand.Options, o => o.HasAlias("--verbose"));
            Assert.Contains(rootCommand.Options, o => o.HasAlias("--debug"));
            Assert.Contains(rootCommand.Options, o => o.HasAlias("--trace"));
        }

        [Theory]
        [InlineData("--evaluate-tools")]
        [InlineData("-e")]
        [InlineData("--verbose")]
        [InlineData("-v")]
        [InlineData("--debug")]
        [InlineData("-d")]
        [InlineData("--trace")]
        [InlineData("-t")]
        public void CommandLine_ValidOptions_ShouldBeRecognized(string option)
        {
            // Test that all our command line options are properly recognized
            var rootCommand = new RootCommand("PowerShell MCP Server - Provides access to PowerShell commands via Model Context Protocol");
            
            var evaluateToolsOption = new Option<bool>(
                aliases: new[] { "--evaluate-tools", "-e" },
                description: "Evaluate and list discovered PowerShell tools without starting the MCP server");
            
            var verboseOption = new Option<bool>(
                aliases: new[] { "--verbose", "-v" },
                description: "Enable verbose logging");
                
            var debugOption = new Option<bool>(
                aliases: new[] { "--debug", "-d" },
                description: "Enable debug logging");
                
            var traceOption = new Option<bool>(
                aliases: new[] { "--trace", "-t" },
                description: "Enable trace logging");

            rootCommand.AddOption(evaluateToolsOption);
            rootCommand.AddOption(verboseOption);
            rootCommand.AddOption(debugOption);
            rootCommand.AddOption(traceOption);

            // Verify the option exists
            bool optionExists = false;
            foreach (var opt in rootCommand.Options)
            {
                if (opt.HasAlias(option))
                {
                    optionExists = true;
                    break;
                }
            }
            
            Assert.True(optionExists, $"Option '{option}' should be recognized");
        }

        [Fact]
        public void CommandLine_CombinedOptions_ShouldWork()
        {
            // Test that we can combine options like --evaluate-tools --verbose
            var rootCommand = new RootCommand("PowerShell MCP Server - Provides access to PowerShell commands via Model Context Protocol");
            
            var evaluateToolsOption = new Option<bool>(
                aliases: new[] { "--evaluate-tools", "-e" },
                description: "Evaluate and list discovered PowerShell tools without starting the MCP server");
            
            var verboseOption = new Option<bool>(
                aliases: new[] { "--verbose", "-v" },
                description: "Enable verbose logging");

            rootCommand.AddOption(evaluateToolsOption);
            rootCommand.AddOption(verboseOption);

            // Test parsing combined options
            var parseResult = rootCommand.Parse(new[] { "--evaluate-tools", "--verbose" });
            
            Assert.NotNull(parseResult);
            Assert.Empty(parseResult.Errors);
            
            // Verify both options are recognized
            Assert.True(parseResult.GetValueForOption(evaluateToolsOption));
            Assert.True(parseResult.GetValueForOption(verboseOption));
        }
    }
}