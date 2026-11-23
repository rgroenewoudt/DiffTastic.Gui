using DiffTastic.Core.IO;

namespace DiffTastic.Core.Tests;

public class FileChangeWatcherTests
{
    private static async Task<bool> RaisedAsync(TaskCompletionSource tcs)
    {
        try
        {
            await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    [Fact]
    public async Task Raises_once_after_writes_settle()
    {
        var dir = Directory.CreateTempSubdirectory("difftastic-watch").FullName;
        var file = Path.Combine(dir, "a.txt");
        await File.WriteAllTextAsync(file, "one");
        var count = 0;
        var tcs = new TaskCompletionSource();
        using var watcher = new FileChangeWatcher([file], TimeSpan.FromMilliseconds(200));
        watcher.Changed += (_, _) => { Interlocked.Increment(ref count); tcs.TrySetResult(); };

        await File.WriteAllTextAsync(file, "two");
        await File.WriteAllTextAsync(file, "three");

        Assert.True(await RaisedAsync(tcs));
        await Task.Delay(400);
        Assert.Equal(1, count);
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task Raises_when_file_is_replaced_by_rename()
    {
        var dir = Directory.CreateTempSubdirectory("difftastic-watch").FullName;
        var file = Path.Combine(dir, "a.txt");
        await File.WriteAllTextAsync(file, "one");
        var tcs = new TaskCompletionSource();
        using var watcher = new FileChangeWatcher([file], TimeSpan.FromMilliseconds(100));
        watcher.Changed += (_, _) => tcs.TrySetResult();

        var temp = Path.Combine(dir, "a.txt.tmp");
        await File.WriteAllTextAsync(temp, "two");
        File.Move(temp, file, overwrite: true);

        Assert.True(await RaisedAsync(tcs));
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task Ignores_other_files_in_same_folder()
    {
        var dir = Directory.CreateTempSubdirectory("difftastic-watch").FullName;
        var file = Path.Combine(dir, "a.txt");
        await File.WriteAllTextAsync(file, "one");
        var tcs = new TaskCompletionSource();
        using var watcher = new FileChangeWatcher([file], TimeSpan.FromMilliseconds(100));
        watcher.Changed += (_, _) => tcs.TrySetResult();

        await File.WriteAllTextAsync(Path.Combine(dir, "b.txt"), "other");

        await Task.Delay(500);
        Assert.False(tcs.Task.IsCompleted);
        Directory.Delete(dir, recursive: true);
    }
}
