using System;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Moq;
using PoshMcp.Web.PowerShell;
using Xunit;
using System.Reflection;

namespace PoshMcp.Tests.Unit;

/// <summary>
/// Unit tests for SessionAwarePowerShellRunspace session ID extraction logic
/// </summary>
public class SessionAwarePowerShellRunspaceTests : IDisposable
{
    private readonly Mock<IHttpContextAccessor> _mockHttpContextAccessor;
    private readonly Mock<ILogger<SessionAwarePowerShellRunspace>> _mockLogger;
    private readonly SessionAwarePowerShellRunspace _runspace;

    public SessionAwarePowerShellRunspaceTests()
    {
        _mockHttpContextAccessor = new Mock<IHttpContextAccessor>();
        _mockLogger = new Mock<ILogger<SessionAwarePowerShellRunspace>>();
        _runspace = new SessionAwarePowerShellRunspace(_mockHttpContextAccessor.Object, _mockLogger.Object);
    }

    [Fact]
    public void DifferentMcpSessionIds_CreateDifferentRunspaces()
    {
        // Arrange
        var sessionId1 = "test-session-123";
        var sessionId2 = "test-session-456";

        // Setup first session
        SetupMockHttpContextWithMcpSessionId(sessionId1);
        var runspace1 = GetSessionRunspaceViaReflection();

        // Setup second session
        SetupMockHttpContextWithMcpSessionId(sessionId2);
        var runspace2 = GetSessionRunspaceViaReflection();

        // Assert
        Assert.NotNull(runspace1);
        Assert.NotNull(runspace2);
        Assert.NotSame(runspace1, runspace2);
    }

    [Fact]
    public void SameMcpSessionId_ReturnsSameRunspace()
    {
        // Arrange
        var sessionId = "test-session-123";

        // Setup session
        SetupMockHttpContextWithMcpSessionId(sessionId);
        var runspace1 = GetSessionRunspaceViaReflection();
        var runspace2 = GetSessionRunspaceViaReflection();

        // Assert
        Assert.NotNull(runspace1);
        Assert.Same(runspace1, runspace2);
    }

    [Fact]
    public void NoHttpContext_ReturnsDefaultRunspace()
    {
        // Arrange
        _mockHttpContextAccessor.Setup(a => a.HttpContext).Returns((HttpContext)null);

        // Act
        var runspace1 = GetSessionRunspaceViaReflection();
        var runspace2 = GetSessionRunspaceViaReflection();

        // Assert
        Assert.NotNull(runspace1);
        Assert.Same(runspace1, runspace2); // Should return same runspace for "default" session
    }

    [Fact]
    public void NoMcpSessionIdHeader_FallsBackToConnectionId()
    {
        // Arrange
        var connectionId = "connection-123";
        SetupMockHttpContextWithConnectionId(connectionId, includeMcpHeader: false);

        // Act
        var runspace1 = GetSessionRunspaceViaReflection();
        var runspace2 = GetSessionRunspaceViaReflection();

        // Assert
        Assert.NotNull(runspace1);
        Assert.Same(runspace1, runspace2); // Should return same runspace for same connection
    }

    private void SetupMockHttpContextWithMcpSessionId(string sessionId)
    {
        var mockHttpContext = new Mock<HttpContext>();
        var mockRequest = new Mock<HttpRequest>();
        var mockHeaders = new Mock<IHeaderDictionary>();

        mockHeaders.Setup(h => h.TryGetValue("Mcp-Session-Id", out It.Ref<StringValues>.IsAny))
            .Returns((string key, out StringValues value) =>
            {
                value = new StringValues(sessionId);
                return true;
            });

        mockRequest.Setup(r => r.Headers).Returns(mockHeaders.Object);
        mockHttpContext.Setup(c => c.Request).Returns(mockRequest.Object);
        _mockHttpContextAccessor.Setup(a => a.HttpContext).Returns(mockHttpContext.Object);
    }

    private void SetupMockHttpContextWithConnectionId(string connectionId, bool includeMcpHeader = true)
    {
        var mockHttpContext = new Mock<HttpContext>();
        var mockRequest = new Mock<HttpRequest>();
        var mockHeaders = new Mock<IHeaderDictionary>();
        var mockConnection = new Mock<ConnectionInfo>();

        if (!includeMcpHeader)
        {
            mockHeaders.Setup(h => h.TryGetValue("Mcp-Session-Id", out It.Ref<StringValues>.IsAny))
                .Returns(false);
        }

        mockRequest.Setup(r => r.Headers).Returns(mockHeaders.Object);
        mockConnection.Setup(c => c.Id).Returns(connectionId);
        mockHttpContext.Setup(c => c.Request).Returns(mockRequest.Object);
        mockHttpContext.Setup(c => c.Connection).Returns(mockConnection.Object);
        _mockHttpContextAccessor.Setup(a => a.HttpContext).Returns(mockHttpContext.Object);
    }

    private object GetSessionRunspaceViaReflection()
    {
        var method = typeof(SessionAwarePowerShellRunspace).GetMethod("GetSessionRunspace",
            BindingFlags.NonPublic | BindingFlags.Instance);
        return method?.Invoke(_runspace, null);
    }

    public void Dispose()
    {
        _runspace?.Dispose();
    }
}