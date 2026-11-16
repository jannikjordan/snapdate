using Snapdate.Application.Ports;
using Snapdate.Infrastructure.FileSystem;
using Snapdate.Infrastructure.Tests.Support;

namespace Snapdate.Infrastructure.Tests.FileSystem;

public sealed class LocalFileSystemTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly LocalFileSystem _fs = new();

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Copy_creates_identical_file_keeps_original_and_timestamps()
    {
        var stamp = new DateTime(2021, 2, 3, 4, 5, 6);
        var source = _temp.Write("src/a.jpg", [1, 2, 3, 4], stamp);
        var target = _temp.Combine("dst", "b.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        await _fs.CopyAsync(source, target, Ct);

        Assert.True(File.Exists(source));
        Assert.Equal([1, 2, 3, 4], await File.ReadAllBytesAsync(target, Ct));
        Assert.Equal(stamp, File.GetLastWriteTime(target));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(target)!, "*.snapdate-tmp"));
    }

    [Fact]
    public async Task Copy_never_overwrites_an_existing_file()
    {
        var source = _temp.Write("a.jpg", [1]);
        var target = _temp.Write("b.jpg", [9, 9]);

        await Assert.ThrowsAsync<TargetExistsException>(() => _fs.CopyAsync(source, target, Ct));

        Assert.Equal([9, 9], await File.ReadAllBytesAsync(target, Ct));
    }

    [Fact]
    public async Task Move_on_same_drive_renames_and_removes_original()
    {
        var source = _temp.Write("a.jpg", [5, 6]);
        var target = _temp.Combine("moved.jpg");

        await _fs.MoveAsync(source, target, Ct);

        Assert.False(File.Exists(source));
        Assert.Equal([5, 6], await File.ReadAllBytesAsync(target, Ct));
    }

    [Fact]
    public async Task Move_never_overwrites_and_keeps_the_original()
    {
        var source = _temp.Write("a.jpg", [1]);
        var target = _temp.Write("b.jpg", [2]);

        await Assert.ThrowsAsync<TargetExistsException>(() => _fs.MoveAsync(source, target, Ct));

        Assert.True(File.Exists(source));
        Assert.Equal([2], await File.ReadAllBytesAsync(target, Ct));
    }

    [Fact]
    public async Task Cancelled_copy_leaves_no_partial_file()
    {
        var source = _temp.Write("big.jpg", new byte[4 * 1024 * 1024]);
        var target = _temp.Combine("copy.jpg");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync().WaitAsync(Ct);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _fs.CopyAsync(source, target, cts.Token));

        Assert.False(File.Exists(target));
        Assert.Single(Directory.GetFiles(_temp.Path));
    }

    [Fact]
    public void Enumeration_skips_hidden_files_and_respects_recursion()
    {
        _temp.Write("a.jpg", [1]);
        _temp.Write("sub/b.jpg", [1]);
        var hidden = _temp.Write("hidden.jpg", [1]);
        File.SetAttributes(hidden, FileAttributes.Hidden);

        Assert.Equal(["a.jpg"], _fs.EnumerateFiles(_temp.Path, recursive: false).Select(Path.GetFileName));
        Assert.Equal(2, _fs.EnumerateFiles(_temp.Path, recursive: true).Count());
    }

    [Fact]
    public void Earliest_timestamp_is_used_as_fallback_date()
    {
        var path = _temp.Write("a.jpg", [1]);
        File.SetCreationTime(path, new DateTime(2022, 1, 1));
        File.SetLastWriteTime(path, new DateTime(2020, 6, 6));

        Assert.Equal(new DateTime(2020, 6, 6), _fs.GetEarliestTimestamp(path));
    }

    [Fact]
    public async Task Log_writing_never_overwrites()
    {
        var path = _temp.Write("log.csv", [1]);
        await Assert.ThrowsAsync<IOException>(() => _fs.WriteAllTextAsync(path, "x", Ct));
    }
}
