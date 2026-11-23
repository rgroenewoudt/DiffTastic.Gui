namespace DiffTastic.Core.IO;

/// <summary>
/// Raises <see cref="Changed"/> once a burst of writes to one of the files has settled.
/// Also catches editors that save by writing a temp file and renaming it into place.
/// </summary>
public sealed class FileChangeWatcher : IDisposable
{
    private readonly HashSet<string> _files;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly Timer _timer;
    private readonly TimeSpan _delay;

    /// <summary>Raised on a thread pool thread.</summary>
    public event EventHandler? Changed;

    public FileChangeWatcher(IEnumerable<string> files, TimeSpan delay)
    {
        _delay = delay;
        _files = files.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _timer = new Timer(_ => Changed?.Invoke(this, EventArgs.Empty));

        foreach (var dir in _files.Select(Path.GetDirectoryName).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (dir is null || !Directory.Exists(dir))
                continue;
            var watcher = new FileSystemWatcher(dir)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            };
            watcher.Changed += OnEvent;
            watcher.Created += OnEvent;
            watcher.Renamed += OnEvent;
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }
    }

    private void OnEvent(object sender, FileSystemEventArgs e)
    {
        if (_files.Contains(e.FullPath))
            _timer.Change(_delay, Timeout.InfiniteTimeSpan);
    }

    public void Dispose()
    {
        foreach (var watcher in _watchers)
            watcher.Dispose();
        _timer.Dispose();
        Changed = null;
    }
}
