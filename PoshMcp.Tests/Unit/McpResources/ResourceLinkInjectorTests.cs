using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using PoshMcp.Server.McpResources;
using PoshMcp.Server.PowerShell;
using Xunit;

namespace PoshMcp.Tests.Unit.McpResources;

[Trait("Category", "Unit")]
public class ResourceLinkInjectorTests
{
    [Fact]
    public void WrapToolsWithResourceLinks_InvalidAssociatedResourceUri_LogsWarning_AndKeepsNounFallbackWrapped()
    {
        var logger = new ListLogger();
        var nounRegistry = EffectiveNounResourceRegistry.Build(
            NounRegistry.Build(
                new[] { "Get-NounResourceFixture", "Assert-NounResourceFixture" },
                logger),
            overrides: null);

        var tool = MakeTool("assert_noun_resource_fixture", "Assert-NounResourceFixture");
        var commandOverrides = new Dictionary<string, FunctionOverride>(StringComparer.OrdinalIgnoreCase)
        {
            ["Assert-NounResourceFixture"] = new() { AssociatedResourceUri = "poshmcp://resources/not-exposed" }
        };

        var wrappedTools = ResourceLinkInjector.WrapToolsWithResourceLinks(
            new List<McpServerTool> { tool },
            nounRegistry,
            commandOverrides,
            new McpResourcesConfiguration(),
            logger);

        Assert.Single(wrappedTools);
        Assert.NotSame(tool, wrappedTools[0]);
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Warning
                && entry.Message.Contains("AssociatedResourceUri", StringComparison.OrdinalIgnoreCase)
                && entry.Message.Contains("Assert-NounResourceFixture", StringComparison.OrdinalIgnoreCase)
                && entry.Message.Contains("poshmcp://resources/not-exposed", StringComparison.OrdinalIgnoreCase));
    }

    private static McpServerTool MakeTool(string toolName, string commandName)
    {
        var stub = new Func<string>(() => "stub");
        return McpServerTool.Create(stub, new McpServerToolCreateOptions
        {
            Name = toolName,
            Title = commandName,
            Description = "stub",
        });
    }

    private sealed class ListLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }
}