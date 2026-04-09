using System;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Moq;
using PoshMcp.Server.PowerShell;
using Xunit;

namespace PoshMcp.Tests.Unit;

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
    public void NoHttpContext_UsesDefaultSessionRunspace()
    {
        _mockHttpContextAccessor.Setup(a => a.HttpContext).Returns((HttpContext?)null);

        var runspace1 = GetSessionRunspaceViaReflection();
        var runspace2 = GetSessionRunspaceViaReflection();

        Assert.NotNull(runspace1);
        Assert.Same(runspace1, runspace2);
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

    private object? GetSessionRunspaceViaReflection()
    {
        var method = typeof(SessionAwarePowerShellRunspace).GetMethod("GetSessionRunspace", BindingFlags.NonPublic | BindingFlags.Instance);
        return method?.Invoke(_runspace, null);
    }

    public void Dispose()
    {
        _runspace.Dispose();
    }
}
