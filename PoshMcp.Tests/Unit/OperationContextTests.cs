using PoshMcp.Server.Observability;
using System.Threading.Tasks;
using Xunit;

namespace PoshMcp.Tests.Unit;

[Trait("Category", "Unit")]
public class OperationContextTests : System.IDisposable
{
    public OperationContextTests()
    {
        ResetContext();
    }

    [Fact]
    public void SetAndGetCycle_RoundTripsCorrelationIdAndOperationName()
    {
        // Arrange
        const string correlationId = "corr-12345678";
        const string operationName = "tool-discovery";

        // Act
        OperationContext.CorrelationId = correlationId;
        OperationContext.OperationName = operationName;

        // Assert
        Assert.Equal(correlationId, OperationContext.CorrelationId);
        Assert.Equal(operationName, OperationContext.OperationName);
    }

    [Fact]
    public void BeginOperation_NestedScopes_RestoreOuterAndPreviousValuesOnDispose()
    {
        // Arrange
        OperationContext.CorrelationId = "root-correlation";
        OperationContext.OperationName = "root-operation";

        // Act / Assert
        using (OperationContext.BeginOperation("outer-operation"))
        {
            var outerCorrelationId = OperationContext.CorrelationId;

            Assert.NotEqual("root-correlation", outerCorrelationId);
            Assert.Equal("outer-operation", OperationContext.OperationName);

            using (OperationContext.BeginOperation("inner-correlation", "inner-operation"))
            {
                Assert.Equal("inner-correlation", OperationContext.CorrelationId);
                Assert.Equal("inner-operation", OperationContext.OperationName);
            }

            Assert.Equal(outerCorrelationId, OperationContext.CorrelationId);
            Assert.Equal("outer-operation", OperationContext.OperationName);
        }

        Assert.Equal("root-correlation", OperationContext.CorrelationId);
        Assert.Equal("root-operation", OperationContext.OperationName);
    }

    [Fact]
    public void GenerateCorrelationId_ReturnsExpectedTimestampAndSuffixFormat()
    {
        var correlationId = OperationContext.GenerateCorrelationId();

        Assert.Matches("^[0-9]{8}-[0-9]{6}-[0-9a-f]{8}$", correlationId);
    }

    [Fact]
    public async Task CorrelationId_DoesNotLeakAcrossParallelAsyncContexts()
    {
        var firstContextReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowFirstContextToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var firstContext = Task.Run(async () =>
        {
            OperationContext.CorrelationId = "context-one";
            OperationContext.OperationName = "operation-one";
            firstContextReady.SetResult(true);

            await allowFirstContextToFinish.Task;

            Assert.Equal("context-one", OperationContext.CorrelationId);
            Assert.Equal("operation-one", OperationContext.OperationName);
        });

        var secondContext = Task.Run(async () =>
        {
            await firstContextReady.Task;

            Assert.Null(OperationContext.OperationName);

            var generatedCorrelationId = OperationContext.CorrelationId;
            Assert.Matches("^[0-9]{8}-[0-9]{6}-[0-9a-f]{8}$", generatedCorrelationId);
            Assert.NotEqual("context-one", generatedCorrelationId);

            allowFirstContextToFinish.SetResult(true);
        });

        await Task.WhenAll(firstContext, secondContext);

        Assert.Null(OperationContext.OperationName);
    }

    public void Dispose()
    {
        ResetContext();
    }

    private static void ResetContext()
    {
        OperationContext.CorrelationId = null!;
        OperationContext.OperationName = null;
    }
}
