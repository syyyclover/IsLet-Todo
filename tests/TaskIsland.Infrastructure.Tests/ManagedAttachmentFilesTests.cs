using TaskIsland.Application;
using TaskIsland.Infrastructure;
using Xunit;

public sealed class ManagedAttachmentFilesTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "TaskIsland.Copy.Tests", Guid.NewGuid().ToString("N"));
    [Fact] public async Task CopiesHaveUniqueNamesAndNeverChangeSource()
    {
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "用户文件.txt"); await File.WriteAllTextAsync(source, "preserved");
        var files = new ManagedAttachmentFiles(Path.Combine(root, "managed"));
        var one = files.CreateRelativePath(Guid.NewGuid(), Guid.NewGuid(), "用户文件.txt");
        var two = files.CreateRelativePath(Guid.NewGuid(), Guid.NewGuid(), "用户文件.txt");
        Assert.NotEqual(one, two);
        await files.CopyAsync(source, one, null, default);
        Assert.Equal("preserved", await File.ReadAllTextAsync(files.GetManagedPath(one)));
        var moved = source + ".moved"; File.Move(source, moved);
        Assert.Equal("preserved", await File.ReadAllTextAsync(files.GetManagedPath(one)));
        File.Move(moved, source);
        Assert.True(await files.DeleteAsync(one, default));
        Assert.Equal("preserved", await File.ReadAllTextAsync(source));
    }
    [Fact] public async Task PartialCopyFailureCleansTemporaryAndFinalFiles()
    {
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "source.txt"); await File.WriteAllTextAsync(source, "preserved");
        var files = new ManagedAttachmentFiles(Path.Combine(root, "managed"), new PartialFailure());
        var relative = files.CreateRelativePath(Guid.NewGuid(), Guid.NewGuid(), "source.txt");
        await Assert.ThrowsAsync<IOException>(() => files.CopyAsync(source, relative, null, default));
        Assert.Empty(Directory.GetFiles(Path.Combine(root, "managed"), "*", SearchOption.AllDirectories));
        Assert.Equal("preserved", await File.ReadAllTextAsync(source));
    }
    [Fact] public void UnsafePathsAndDirectoriesAreRejected()
    {
        Directory.CreateDirectory(root);
        var files = new ManagedAttachmentFiles(Path.Combine(root, "managed"));
        Assert.Throws<InvalidOperationException>(() => files.Inspect(root));
        Assert.Throws<InvalidOperationException>(() => files.GetManagedPath("../source.txt"));
        Assert.Throws<InvalidOperationException>(() => files.GetManagedPath(@"C:\source.txt"));
    }
    [Fact] public async Task CleanupReservationWithoutCreatedDirectoryCountsAsComplete()
    {
        var files = new ManagedAttachmentFiles(Path.Combine(root, "managed"));
        Assert.True(await files.DeleteAsync(Path.Combine(Guid.NewGuid().ToString("N"), "never-created.txt"), default));
    }
    private sealed class PartialFailure : IAttachmentStreamCopier
    {
        public async Task CopyAsync(Stream source, Stream destination, IProgress<long>? progress, CancellationToken ct)
        { await destination.WriteAsync(new byte[] { 1, 2 }, ct); throw new IOException("simulated partial copy"); }
    }
    public void Dispose()
    {
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "TaskIsland.Copy.Tests")) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(root).StartsWith(parent, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
