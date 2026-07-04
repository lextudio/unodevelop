using System;
using System.IO;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop.Project;
using ICSharpCode.SharpDevelop.Workbench;
using ICSharpCode.SharpDevelop;

namespace UnoDevelop.Services;

internal sealed class UnoProjectChangeWatcher : IProjectChangeWatcher
{
    private readonly IMessageLoop? _messageLoop;
    private FileSystemWatcher? _watcher;
    private string _fileName;
    private bool _enabled = true;
    private DateTime _lastWriteTimeUtc;

    public UnoProjectChangeWatcher(string fileName)
    {
        _messageLoop = ServiceSingleton.ServiceProvider.GetService(typeof(IMessageLoop)) as IMessageLoop;
        _fileName = fileName;
        UpdateLastWriteTime();
        SetWatcher();
    }

    public event EventHandler<FileRenameEventArgs>? ChangedExternally;

    public void Enable()
    {
        _enabled = true;
        SetWatcher();
    }

    public void Disable()
    {
        _enabled = false;
        SetWatcher();
    }

    public void Rename(string newFileName)
    {
        _fileName = newFileName;
        UpdateLastWriteTime();
        SetWatcher();
    }

    public void Dispose()
    {
        if (_watcher is null)
        {
            return;
        }

        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _watcher = null;
    }

    private void SetWatcher()
    {
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
        }

        if (!_enabled || string.IsNullOrWhiteSpace(_fileName) || !Path.IsPathRooted(_fileName))
        {
            return;
        }

        var directory = Path.GetDirectoryName(_fileName);
        var filter = Path.GetFileName(_fileName);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(filter) || !Directory.Exists(directory))
        {
            return;
        }

        _watcher ??= new FileSystemWatcher();
        if (_messageLoop?.SynchronizingObject is not null)
        {
            _watcher.SynchronizingObject = _messageLoop.SynchronizingObject;
        }

        _watcher.Path = directory;
        _watcher.Filter = filter;
        _watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName;
        _watcher.Changed -= OnWatcherChanged;
        _watcher.Created -= OnWatcherChanged;
        _watcher.Deleted -= OnWatcherChanged;
        _watcher.Renamed -= OnWatcherRenamed;
        _watcher.Changed += OnWatcherChanged;
        _watcher.Created += OnWatcherChanged;
        _watcher.Deleted += OnWatcherChanged;
        _watcher.Renamed += OnWatcherRenamed;
        _watcher.EnableRaisingEvents = true;
    }

    private void OnWatcherChanged(object sender, FileSystemEventArgs e)
    {
        if (!HasMeaningfulWriteTimeChange() && e.ChangeType == WatcherChangeTypes.Changed)
        {
            return;
        }

        LoggingService.DebugFormatted("Uno project watcher noticed external change for {0}: {1}", e.FullPath, e.ChangeType);
        UpdateLastWriteTime();
        ChangedExternally?.Invoke(this, new FileRenameEventArgs(_fileName, _fileName, isDirectory: false));
    }

    private void OnWatcherRenamed(object sender, RenamedEventArgs e)
    {
        LoggingService.DebugFormatted("Uno project watcher noticed external rename for {0}: {1}", e.OldFullPath, e.FullPath);
        _fileName = e.FullPath;
        UpdateLastWriteTime();
        ChangedExternally?.Invoke(this, new FileRenameEventArgs(e.OldFullPath, e.FullPath, isDirectory: false));
        SetWatcher();
    }

    private void UpdateLastWriteTime()
    {
        _lastWriteTimeUtc = File.Exists(_fileName)
            ? File.GetLastWriteTimeUtc(_fileName)
            : DateTime.MinValue;
    }

    private bool HasMeaningfulWriteTimeChange()
    {
        if (!File.Exists(_fileName))
        {
            return true;
        }

        return File.GetLastWriteTimeUtc(_fileName) != _lastWriteTimeUtc;
    }
}
