using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using PoshMcp.Server.PowerShell;
using Xunit;

namespace PoshMcp.Tests.Unit;

public class HttpToolFactoryParityTests
{
    [Fact]
    public void ToolFactory_WithSessionAwareRunspace_ProducesSameToolNamesAsIsolatedRunspace()
    {
        var config = new PowerShellConfiguration
        {
            FunctionNames = new List<string> { "Get-Date" },
            Modules = new List<string>(),
            ExcludePatterns = new List<string>(),
            IncludePatterns = new List<string>()
        };

        using var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
        var logger = loggerFactory.CreateLogger("ToolFactoryParity");

        using var isolatedRunspace = new IsolatedPowerShellRunspace();
        using var sessionRunspace = new SessionAwarePowerShellRunspace(
            new HttpContextAccessor(),
            loggerFactory.CreateLogger<SessionAwarePowerShellRunspace>());

        var stdioFactory = new McpToolFactoryV2(isolatedRunspace);
        var httpFactory = new McpToolFactoryV2(sessionRunspace);

        var stdioTools = stdioFactory.GetToolsList(config, logger);
        var httpTools = httpFactory.GetToolsList(config, logger);

        var stdioToolNames = GetToolNames(stdioTools);
        var httpToolNames = GetToolNames(httpTools);

        Assert.Equal(stdioToolNames, httpToolNames);
    }

    private static List<string> GetToolNames(List<ModelContextProtocol.Server.McpServerTool> tools)
    {
        var names = new List<string>();

        foreach (var tool in tools)
        {
            var name = TryGetName(tool, 0);
            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Add(name);
            }
        }

        return names
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? TryGetName(object? value, int depth)
    {
        if (value is null || depth > 3)
        {
            return null;
        }

        var type = value.GetType();
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        var directNameProperty = type.GetProperty("Name", flags);
        if (directNameProperty is not null && directNameProperty.PropertyType == typeof(string))
        {
            var direct = directNameProperty.GetValue(value) as string;
            if (!string.IsNullOrWhiteSpace(direct))
            {
                return direct;
            }
        }

        foreach (var property in type.GetProperties(flags))
        {
            if (property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            object? nestedValue;
            try
            {
                nestedValue = property.GetValue(value);
            }
            catch
            {
                continue;
            }

            if (nestedValue is null || nestedValue is string)
            {
                continue;
            }

            var nestedName = TryGetName(nestedValue, depth + 1);
            if (!string.IsNullOrWhiteSpace(nestedName))
            {
                return nestedName;
            }
        }

        return null;
    }
}
