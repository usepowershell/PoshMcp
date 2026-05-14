using System;
using System.IO;
using PoshMcp.Tests.Shared;
using Xunit;

namespace PoshMcp.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class TempDirectoryTests
{
    [Fact]
    public void Constructor_CreatesDirectoryUnderTempPath()
    {
        using var tmp = new TempDirectory();

        Assert.True(Directory.Exists(tmp.Path), "TempDirectory must create the directory eagerly.");
        Assert.StartsWith(Path.GetTempPath(), tmp.Path, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(TempDirectory.Prefix, Path.GetFileName(tmp.Path));
    }

    [Fact]
    public void Constructor_WithLabel_IncludesLabelInName()
    {
        using var tmp = new TempDirectory("oop-pool");

        var name = Path.GetFileName(tmp.Path);
        Assert.Contains("oop-pool", name);
        Assert.StartsWith(TempDirectory.Prefix, name);
    }

    [Fact]
    public void Constructor_ProducesUniquePathsAcrossInstances()
    {
        using var a = new TempDirectory();
        using var b = new TempDirectory();

        Assert.NotEqual(a.Path, b.Path);
    }

    [Fact]
    public void Combine_ReturnsPathRootedAtTempDirectory()
    {
        using var tmp = new TempDirectory();

        var combined = tmp.Combine("sub", "file.txt");

        Assert.StartsWith(tmp.Path, combined);
        Assert.EndsWith(Path.Combine("sub", "file.txt"), combined);
    }

    [Fact]
    public void Dispose_RemovesDirectoryAndContents()
    {
        string path;
        using (var tmp = new TempDirectory())
        {
            path = tmp.Path;
            File.WriteAllText(Path.Combine(path, "a.txt"), "x");
            Directory.CreateDirectory(Path.Combine(path, "nested"));
            File.WriteAllText(Path.Combine(path, "nested", "b.txt"), "y");
        }

        Assert.False(Directory.Exists(path), "Dispose must recursively delete the directory.");
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var tmp = new TempDirectory();
        tmp.Dispose();

        var ex = Record.Exception(() => tmp.Dispose());

        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_DoesNotThrow_WhenDirectoryAlreadyRemoved()
    {
        var tmp = new TempDirectory();
        Directory.Delete(tmp.Path, recursive: true);

        var ex = Record.Exception(() => tmp.Dispose());

        Assert.Null(ex);
    }

    [Fact]
    public void AuditLeftoverDirectories_FindsLiveInstance()
    {
        using var tmp = new TempDirectory("audit-probe");

        var leftovers = TempDirectory.AuditLeftoverDirectories();

        Assert.Contains(tmp.Path, leftovers, StringComparer.OrdinalIgnoreCase);
    }
}
