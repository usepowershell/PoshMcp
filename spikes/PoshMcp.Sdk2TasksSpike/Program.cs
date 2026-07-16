using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Extensions.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.IO.Pipelines;
using System.Text.Json;

Pipe clientToServerPipe = new();
Pipe serverToClientPipe = new();
var store = new InMemoryMcpTaskStore
{
    DefaultPollIntervalMs = 20,
    DefaultTimeToLive = TimeSpan.FromMinutes(1)
};

var services = new ServiceCollection();
services.AddLogging();
services.AddMcpServer()
    // This is the same McpServerTool.Create shape used by McpToolFactoryV2.
    .WithTools([
        McpServerTool.Create(SpikeTools.RunReport, new() { Name = "run-report" }),
        McpServerTool.Create(SpikeTools.WaitForCancellation, new() { Name = "wait-for-cancellation" })
    ])
    .WithTasks(store);
services.AddSingleton<ITransport>(new StreamServerTransport(
    clientToServerPipe.Reader.AsStream(),
    serverToClientPipe.Writer.AsStream()));

await using var serviceProvider = services.BuildServiceProvider();
var server = McpServer.Create(
    serviceProvider.GetRequiredService<ITransport>(),
    serviceProvider.GetRequiredService<IOptions<McpServerOptions>>().Value,
    serviceProvider.GetRequiredService<ILoggerFactory>(),
    serviceProvider);
_ = server.RunAsync();

await using McpClient client = await McpClient.CreateAsync(new StreamClientTransport(
    serverInput: clientToServerPipe.Writer.AsStream(),
    serverOutput: serverToClientPipe.Reader.AsStream()));

var automaticResult = await client.CallToolWithPollingAsync(new CallToolRequestParams { Name = "run-report" });
AssertText(automaticResult, "report ready");
Console.WriteLine("PASS auto-poll: completed task returned tool result");

var taskCall = await client.CallToolAsTaskAsync(new CallToolRequestParams { Name = "run-report" });
if (!taskCall.IsTask)
{
    throw new InvalidOperationException("Tasks extension did not create a task for an opted-in tool call.");
}

var taskId = taskCall.TaskCreated!.TaskId;
GetTaskResult completed = await PollForTerminalStateAsync(client, taskId);
if (completed is not CompletedTaskResult completedTask)
{
    throw new InvalidOperationException($"Expected completed task, received {completed.Status}.");
}

CallToolResult? manualResult = completedTask.Result.Deserialize<CallToolResult>();
if (manualResult is null)
{
    throw new InvalidOperationException("Completed task did not contain a CallToolResult.");
}

AssertText(manualResult, "report ready");
Console.WriteLine("PASS manual-poll: created task was completed and preserved CallToolResult");

var cancellationCall = await client.CallToolAsTaskAsync(new CallToolRequestParams { Name = "wait-for-cancellation" });
if (!cancellationCall.IsTask)
{
    throw new InvalidOperationException("Cancellation test tool did not create a task.");
}

await SpikeTools.CancellationToolStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
await client.CancelTaskAsync(cancellationCall.TaskCreated!.TaskId);
await SpikeTools.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
GetTaskResult cancelled = await PollForTerminalStateAsync(client, cancellationCall.TaskCreated.TaskId);
if (cancelled is not CancelledTaskResult)
{
    throw new InvalidOperationException($"Expected cancelled task, received {cancelled.Status}.");
}

Console.WriteLine("PASS cancellation: tasks/cancel cancelled the tool CancellationToken and task state");
Console.WriteLine("SDK 2 Tasks spike completed successfully.");

static async Task<GetTaskResult> PollForTerminalStateAsync(McpClient client, string taskId)
{
    for (var attempt = 0; attempt < 100; attempt++)
    {
        GetTaskResult state = await client.GetTaskAsync(taskId);
        if (state is not WorkingTaskResult and not InputRequiredTaskResult)
        {
            return state;
        }

        await Task.Delay(TimeSpan.FromMilliseconds(state.PollIntervalMs ?? 20));
    }

    throw new TimeoutException($"Task '{taskId}' did not reach a terminal state.");
}

static void AssertText(CallToolResult result, string expected)
{
    var content = result.Content.SingleOrDefault() as TextContentBlock;
    if (content?.Text != expected)
    {
        throw new InvalidOperationException($"Expected '{expected}', received '{content?.Text ?? "(no text content)"}'.");
    }
}

internal static class SpikeTools
{
    internal static TaskCompletionSource CancellationToolStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal static TaskCompletionSource CancellationObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Description("Runs a short task to exercise task completion and result serialization.")]
    public static async Task<string> RunReport(CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        return "report ready";
    }

    [Description("Waits until the caller cancels the task.")]
    public static async Task<string> WaitForCancellation(CancellationToken cancellationToken)
    {
        CancellationToolStarted.TrySetResult();
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return "unexpected completion";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            CancellationObserved.TrySetResult();
            throw;
        }
    }
}
