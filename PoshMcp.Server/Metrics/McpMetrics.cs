using System;
using System.Diagnostics.Metrics;
using System.Diagnostics;

namespace PoshMcp.Server.Metrics;

/// <summary>
/// Centralizes all OpenTelemetry metrics for the MCP server
/// </summary>
public class McpMetrics
{
    public static readonly string MeterName = "PoshMcp";
    public static readonly string MeterVersion = "1.0.0";

    private readonly Meter _meter;

    // Tool Execution Metrics
    public Counter<long> ToolInvocationTotal { get; }
    public Histogram<double> ToolExecutionDurationSeconds { get; }
    public Counter<long> ToolExecutionErrorsTotal { get; }

    // Intent Mapping Metrics (placeholder for future AI integration)
    public Counter<long> IntentResolutionSuccessTotal { get; }
    public Counter<long> IntentResolutionFailureTotal { get; }
    public Histogram<double> IntentResolutionLatencySeconds { get; }

    // Usage & Adoption Metrics
    public Counter<long> ToolUsageTotal { get; }
    public Counter<long> ToolUsageByAgentTotal { get; }

    // Tool Registration & Lifecycle
    public Counter<long> ToolRegistrationTotal { get; }
    public Counter<long> ToolUpdateTotal { get; }
    public Counter<long> ToolDeprecationTotal { get; }

    // AI Agent Interaction Metrics (placeholder for future AI integration)
    public Histogram<double> PromptSuccessRate { get; }
    public Counter<long> PromptRetryTotal { get; }
    public Histogram<double> PromptParameterCompletionRate { get; }

    // Agent Engagement
    public Counter<long> AgentInvocationTotal { get; }
    public Histogram<double> AgentToolDiversity { get; }

    public McpMetrics()
    {
        _meter = new Meter(MeterName, MeterVersion);

        // Tool Execution Metrics
        ToolInvocationTotal = _meter.CreateCounter<long>(
            "mcp_tool_invocation_total",
            description: "Count of tool executions, labeled by tool name, user ID, and status");

        ToolExecutionDurationSeconds = _meter.CreateHistogram<double>(
            "mcp_tool_execution_duration_seconds",
            description: "Histogram of execution times per tool");

        ToolExecutionErrorsTotal = _meter.CreateCounter<long>(
            "mcp_tool_execution_errors_total",
            description: "Count of failed executions, labeled by error type");

        // Intent Mapping Metrics
        IntentResolutionSuccessTotal = _meter.CreateCounter<long>(
            "mcp_intent_resolution_success_total",
            description: "Number of successful AI-to-tool mappings");

        IntentResolutionFailureTotal = _meter.CreateCounter<long>(
            "mcp_intent_resolution_failure_total",
            description: "Number of failed mappings");

        IntentResolutionLatencySeconds = _meter.CreateHistogram<double>(
            "mcp_intent_resolution_latency_seconds",
            description: "Time taken to resolve user intent to a tool");

        // Usage & Adoption Metrics
        ToolUsageTotal = _meter.CreateCounter<long>(
            "mcp_tool_usage_total",
            description: "Count of invocations per tool");

        ToolUsageByAgentTotal = _meter.CreateCounter<long>(
            "mcp_tool_usage_by_agent_total",
            description: "Count of invocations by AI agent vs. human user");

        // Tool Registration & Lifecycle
        ToolRegistrationTotal = _meter.CreateCounter<long>(
            "mcp_tool_registration_total",
            description: "Number of tools registered, labeled by source");

        ToolUpdateTotal = _meter.CreateCounter<long>(
            "mcp_tool_update_total",
            description: "Number of tool updates");

        ToolDeprecationTotal = _meter.CreateCounter<long>(
            "mcp_tool_deprecation_total",
            description: "Number of tools marked deprecated or removed");

        // AI Agent Interaction Metrics
        PromptSuccessRate = _meter.CreateHistogram<double>(
            "mcp_prompt_success_rate",
            description: "Ratio of successful tool invocations from AI-generated prompts");

        PromptRetryTotal = _meter.CreateCounter<long>(
            "mcp_prompt_retry_total",
            description: "Number of retries due to failed or incomplete prompts");

        PromptParameterCompletionRate = _meter.CreateHistogram<double>(
            "mcp_prompt_parameter_completion_rate",
            description: "Percentage of prompts that auto-filled all required parameters");

        // Agent Engagement
        AgentInvocationTotal = _meter.CreateCounter<long>(
            "mcp_agent_invocation_total",
            description: "Count of tool invocations initiated by AI agents");

        AgentToolDiversity = _meter.CreateHistogram<double>(
            "mcp_agent_tool_diversity",
            description: "Number of unique tools used per agent over time");
    }

    public void Dispose()
    {
        _meter?.Dispose();
    }
}