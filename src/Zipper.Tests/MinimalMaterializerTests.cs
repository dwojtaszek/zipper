using Xunit;

namespace Zipper.Tests;

/// <summary>
/// Contract tests for the <see cref="IFileMaterializer"/> default-overload fix: the 4-argument
/// <c>CreateZipAsync</c> overload is pinned as abstract, so an implementation that forgets it
/// fails to compile instead of inheriting two mutually recursive default bodies that overflow
/// the stack. <see cref="MinimalMaterializer"/> is the compile-time proof: it overrides only
/// the abstract overload.
/// </summary>
public class MinimalMaterializerTests
{
    [Fact]
    public void CreateZipAsync_FourArgumentOverload_IsAbstract()
    {
        var fourArg = typeof(IFileMaterializer).GetMethod(
            nameof(IFileMaterializer.CreateZipAsync),
            new[]
            {
                typeof(string), typeof(string), typeof(Config.ZipCompressionMethod), typeof(CancellationToken),
            });

        Assert.NotNull(fourArg);
        Assert.True(
            fourArg!.IsAbstract,
            "CreateZipAsync(sourceDir, zipPath, method) must stay abstract so an implementation that overrides nothing cannot inherit mutually recursive defaults.");
    }

    [Fact]
    public void CreateZipAsync_ThreeArgumentOverload_CompletesWithoutRecursion()
    {
        IFileMaterializer materializer = new MinimalMaterializer();

        // Default interface members are only reachable through the interface type. MinimalMaterializer
        // overrides only the abstract 4-argument overload, so this call exercises the inherited
        // 3-argument default and must reach the implementation instead of recursing.
        var task = materializer.CreateZipAsync("source", "target.zip");

        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task DirectoryExistsAsync_OnEmptyStore_ReturnsFalse()
    {
        var materializer = new MinimalMaterializer();

        Assert.False(await materializer.DirectoryExistsAsync(Path.Combine(Path.GetTempPath(), "missing-dir")));
    }

    [Fact]
    public async Task FileExistsAsync_OnEmptyStore_ReturnsFalse()
    {
        var materializer = new MinimalMaterializer();

        Assert.False(await materializer.FileExistsAsync(Path.Combine(Path.GetTempPath(), "missing-file.bin")));
    }
}
