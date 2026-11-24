namespace PoshMcp.Foundation.Tests;

[CollectionDefinition("External workers", DisableParallelization = true)]
public sealed class WorkerCollection : ICollectionFixture<WorkerAssetCleanup>;

public sealed class WorkerAssetCleanup : IDisposable
{
    public void Dispose() => WorkerAssets.Cleanup();
}
