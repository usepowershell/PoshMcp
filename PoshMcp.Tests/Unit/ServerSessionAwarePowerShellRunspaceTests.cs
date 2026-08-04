using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Moq;
using PoshMcp.Server.PowerShell;
using Xunit;

namespace PoshMcp.Tests.Unit;

#pragma warning disable CS0618 // This file intentionally tests the obsolete SessionAwarePowerShellRunspace type until major-version removal.
[Trait("Category", "Unit")]
public class ServerSessionAwarePowerShellRunspaceTests : IDisposable
{
    private readonly Mock<IHttpContextAccessor> _mockHttpContextAccessor;
    private readonly Mock<ILogger<SessionAwarePowerShellRunspace>> _mockLogger;
    private readonly SessionAwarePowerShellRunspace _runspace;

    public ServerSessionAwarePowerShellRunspaceTests()
    {
        _mockHttpContextAccessor = new Mock<IHttpContextAccessor>();
        _mockLogger = new Mock<ILogger<SessionAwarePowerShellRunspace>>();
        _runspace = new SessionAwarePowerShellRunspace(_mockHttpContextAccessor.Object, _mockLogger.Object);
    }

    [Fact]
    public void DifferentMcpSessionIds_CreateDifferentRunspaces()
    {
        SetupMockHttpContextWithMcpSessionId("session-1");
        var runspace1 = GetSessionRunspaceViaReflection();

        SetupMockHttpContextWithMcpSessionId("session-2");
        var runspace2 = GetSessionRunspaceViaReflection();

        Assert.NotNull(runspace1);
        Assert.NotNull(runspace2);
        Assert.NotSame(runspace1, runspace2);
    }

    [Fact]
    public void SameMcpSessionId_ReturnsSameRunspace()
    {
        SetupMockHttpContextWithMcpSessionId("same-session");
        var runspace1 = GetSessionRunspaceViaReflection();
        var runspace2 = GetSessionRunspaceViaReflection();

        Assert.NotNull(runspace1);
        Assert.Same(runspace1, runspace2);
    }

    [Fact]
    public void NoHttpContext_UsesDedicatedDiscoveryRunspace()
    {
        _mockHttpContextAccessor.Setup(a => a.HttpContext).Returns((HttpContext?)null);

        var runspace1 = _runspace.Instance;
        var runspace2 = _runspace.Instance;

        Assert.NotNull(runspace1);
        Assert.Same(runspace1, runspace2);
        Assert.Empty(_runspace.GetStats().SessionIds);
    }

    [Fact]
    public void HeaderlessHttpRequests_UseCleanOneShotRunspaces()
    {
        var context1 = CreateHttpContextWithoutMcpSessionId("conn-1", "trace-1");
        var context2 = CreateHttpContextWithoutMcpSessionId("conn-2", "trace-2");

        _mockHttpContextAccessor.Setup(a => a.HttpContext).Returns(context1);
        _runspace.ExecuteThreadSafe(powerShell => InvokeScript(powerShell, "$global:HeaderlessRequestState = 'first-request'"));

        _mockHttpContextAccessor.Setup(a => a.HttpContext).Returns(context2);
        var state = _runspace.ExecuteThreadSafe(powerShell =>
            InvokeScript(powerShell, "if ($global:HeaderlessRequestState) { $global:HeaderlessRequestState } else { 'clean' }"));

        Assert.Equal("clean", state);
        Assert.Equal(0, _runspace.GetStats().ActiveSessions);
        Assert.Equal(0, _runspace.GetStats().OwnedSessionRunspaces);
    }

    [Fact]
    public void SameMcpSessionId_PreservesPowerShellState()
    {
        SetupMockHttpContextWithMcpSessionId("persistent-session");
        _runspace.ExecuteThreadSafe(powerShell => InvokeScript(powerShell, "$global:PersistentSessionState = 'retained'"));

        SetupMockHttpContextWithMcpSessionId("persistent-session");
        var state = _runspace.ExecuteThreadSafe(powerShell =>
            InvokeScript(powerShell, "$global:PersistentSessionState"));

        Assert.Equal("retained", state);
        Assert.Equal(new[] { "persistent-session" }, _runspace.GetStats().SessionIds);
    }

    [Fact]
    public void CapacityEvictionAndReplacement_AreBoundedAndSessionAffine()
    {
        var time = new FakeTimeProvider();
        using var runspaces = CreateManagedRunspaces(new SessionRunspaceOptions
        {
            Capacity = 1,
            WarmStandbyCount = 1,
            IdleTtl = TimeSpan.FromMinutes(1),
            SweepInterval = TimeSpan.FromHours(1),
            AcquisitionTimeout = TimeSpan.Zero
        }, time);

        SetSession("first");
        var first = GetSessionRunspaceViaReflection(runspaces);
        Assert.Equal(1, runspaces.GetStats().ActiveSessions);
        Assert.Equal(1, runspaces.GetStats().OwnedSessionRunspaces);

        SetSession("second");
        Assert.Throws<TargetInvocationException>(() => GetSessionRunspaceViaReflection(runspaces));

        runspaces.CleanupSession("first");
        var second = GetSessionRunspaceViaReflection(runspaces);
        Assert.NotSame(first, second);
        Assert.Equal(1, runspaces.GetStats().ActiveSessions);
        Assert.Equal(1, runspaces.GetStats().OwnedSessionRunspaces);
        Assert.InRange(runspaces.GetStats().WarmStandbyCount, 0, 1);
    }

    [Fact]
    public void IdleSweep_EvictsExpiredSessionAndDoesNotTransferItsState()
    {
        var time = new FakeTimeProvider();
        using var runspaces = CreateManagedRunspaces(new SessionRunspaceOptions
        {
            Capacity = 2,
            WarmStandbyCount = 0,
            IdleTtl = TimeSpan.FromSeconds(10),
            SweepInterval = TimeSpan.FromHours(1)
        }, time);

        SetSession("expired");
        var expired = GetSessionRunspaceViaReflection(runspaces);
        time.Advance(TimeSpan.FromSeconds(11));
        runspaces.SweepIdleRunspaces();

        Assert.Equal(0, runspaces.GetStats().ActiveSessions);
        SetSession("replacement");
        var replacement = GetSessionRunspaceViaReflection(runspaces);
        Assert.NotSame(expired, replacement);
        Assert.Equal(new[] { "replacement" }, runspaces.GetStats().SessionIds);
    }

    [Fact]
    public async Task CleanupDuringActiveInvocation_DefersDisposalUntilInvocationCompletes()
    {
        using var runspaces = CreateManagedRunspaces(new SessionRunspaceOptions
        {
            Capacity = 1,
            WarmStandbyCount = 0,
            AcquisitionTimeout = TimeSpan.Zero
        });
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        SetSession("active");
        var invocation = runspaces.ExecuteThreadSafeAsync(async _ =>
        {
            started.SetResult();
            await complete.Task;
            return 1;
        });
        await started.Task;

        runspaces.CleanupSession("active");
        Assert.Equal(0, runspaces.GetStats().ActiveSessions);
        Assert.Equal(1, runspaces.GetStats().OwnedSessionRunspaces);

        complete.SetResult();
        Assert.Equal(1, await invocation);
        Assert.Equal(0, runspaces.GetStats().OwnedSessionRunspaces);
    }

    private void SetupMockHttpContextWithMcpSessionId(string sessionId)
    {
        var mockHttpContext = new Mock<HttpContext>();
        var mockRequest = new Mock<HttpRequest>();
        var mockHeaders = new Mock<IHeaderDictionary>();

        mockHeaders.Setup(h => h.TryGetValue("Mcp-Session-Id", out It.Ref<StringValues>.IsAny))
            .Returns((string _, out StringValues value) =>
            {
                value = new StringValues(sessionId);
                return true;
            });

        mockRequest.Setup(r => r.Headers).Returns(mockHeaders.Object);
        mockHttpContext.Setup(c => c.Request).Returns(mockRequest.Object);
        _mockHttpContextAccessor.Setup(a => a.HttpContext).Returns(mockHttpContext.Object);
    }

    private static DefaultHttpContext CreateHttpContextWithoutMcpSessionId(string connectionId, string traceIdentifier)
    {
        var context = new DefaultHttpContext
        {
            TraceIdentifier = traceIdentifier
        };
        context.Connection.Id = connectionId;
        return context;
    }

    private SessionAwarePowerShellRunspace CreateManagedRunspaces(SessionRunspaceOptions options, TimeProvider? timeProvider = null)
    {
        return new SessionAwarePowerShellRunspace(
            _mockHttpContextAccessor.Object,
            _mockLogger.Object,
            options,
            timeProvider);
    }

    private void SetSession(string sessionId) => SetupMockHttpContextWithMcpSessionId(sessionId);

    private static string? InvokeScript(System.Management.Automation.PowerShell powerShell, string script)
    {
        powerShell.Commands.Clear();
        powerShell.AddScript(script);
        var result = powerShell.Invoke().SingleOrDefault()?.ToString();
        powerShell.Commands.Clear();
        return result;
    }

    private object? GetSessionRunspaceViaReflection(SessionAwarePowerShellRunspace? runspace = null)
    {
        var method = typeof(SessionAwarePowerShellRunspace).GetMethod("GetSessionRunspace", BindingFlags.NonPublic | BindingFlags.Instance);
        return method?.Invoke(runspace ?? _runspace, null);
    }

    public void Dispose()
    {
        _runspace.Dispose();
    }
}
#pragma warning restore CS0618
