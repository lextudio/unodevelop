using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop.Workbench;

namespace UnoDevelop.Services;

internal sealed class UnoFileService : IFileService
{
    private readonly Dictionary<string, OpenedFile> _openedFiles = new(StringComparer.OrdinalIgnoreCase);
    private Func<string, bool, IViewContent?>? _openFile;
    private Func<string, int, int, IViewContent?>? _jumpToFilePosition;

    // ── Events ────────────────────────────────────────────────────────────

    public event EventHandler<FileRenamingEventArgs>? FileRenaming;
    public event EventHandler<FileRenameEventArgs>? FileRenamed;
    public event EventHandler<FileRenamingEventArgs>? FileCopying;
    public event EventHandler<FileRenameEventArgs>? FileCopied;
    public event EventHandler<FileCancelEventArgs>? FileRemoving;
    public event EventHandler<FileEventArgs>? FileRemoved;
    public event EventHandler<FileEventArgs>? FileCreated;
    public event EventHandler<FileCancelEventArgs>? FileReplacing;
    public event EventHandler<FileEventArgs>? FileReplaced;

    // ── Notification helpers (called from MainPage) ────────────────────

    public void NotifyFileOpened(string filePath)
    {
        // File is now tracked via GetOrCreateOpenedFile; event subscribers will see FileCreated.
    }

    public void NotifyFileClosed(string filePath)
    {
        _openedFiles.Remove(filePath);
        FileRemoved?.Invoke(this, new FileEventArgs(filePath, isDirectory: false));
    }

    public void NotifyFileRenamed(string oldPath, string newPath)
    {
        if (_openedFiles.TryGetValue(oldPath, out var file))
        {
            _openedFiles.Remove(oldPath);
            file.FileName = FileName.Create(newPath);
            _openedFiles[newPath] = file;
        }

        FileRenamed?.Invoke(this, new FileRenameEventArgs(oldPath, newPath, isDirectory: false));
    }

    // ── Options ───────────────────────────────────────────────────────────

    public IRecentOpen RecentOpen => throw new NotImplementedException();

    public bool DeleteToRecycleBin { get; set; } = false;

    public bool SaveUsingTemporaryFile { get; set; } = false;

    public Encoding DefaultFileEncoding => Encoding.UTF8;

    public EncodingInfo DefaultFileEncodingInfo
    {
        get => throw new NotImplementedException();
        set => throw new NotImplementedException();
    }

    public IReadOnlyList<EncodingInfo> AllEncodings => throw new NotImplementedException();

    // ── GetFileContent ────────────────────────────────────────────────────

    public ITextSource GetFileContent(FileName fileName) => GetFileContent(fileName.ToString());

    public ITextSource GetFileContent(string fileName)
    {
        try
        {
            return new StringTextSource(System.IO.File.ReadAllText(fileName));
        }
        catch
        {
            return new StringTextSource(string.Empty);
        }
    }

    public ITextSource GetFileContentForOpenFile(FileName fileName) => GetFileContent(fileName);

    public ITextSource GetFileContentFromDisk(FileName fileName, CancellationToken cancellationToken = default)
        => GetFileContent(fileName);

    // ── BrowseForFolder ───────────────────────────────────────────────────

    public string BrowseForFolder(string description, string selectedPath = null)
        => throw new NotImplementedException();

    // ── OpenedFiles ───────────────────────────────────────────────────────

    public IReadOnlyList<OpenedFile> OpenedFiles => new List<OpenedFile>(_openedFiles.Values);

    public OpenedFile GetOpenedFile(FileName fileName) => GetOpenedFile(fileName.ToString());

    public OpenedFile GetOpenedFile(string fileName)
    {
        _openedFiles.TryGetValue(fileName, out var file);
        return file;
    }

    public OpenedFile GetOrCreateOpenedFile(FileName fileName) => GetOrCreateOpenedFile(fileName.ToString());

    public OpenedFile GetOrCreateOpenedFile(string fileName)
    {
        if (!_openedFiles.TryGetValue(fileName, out var file))
        {
            file = new UnoOpenedFile(fileName);
            _openedFiles[fileName] = file;
        }

        return file;
    }

    public OpenedFile CreateUntitledOpenedFile(string defaultName, byte[] content)
        => throw new NotImplementedException();

    public void BindWorkbenchFileOperations(
        Func<string, bool, IViewContent?> openFile,
        Func<string, int, int, IViewContent?> jumpToFilePosition)
    {
        _openFile = openFile;
        _jumpToFilePosition = jumpToFilePosition;
    }

    // ── CheckFileName ─────────────────────────────────────────────────────

    public bool CheckFileName(string path) => !string.IsNullOrEmpty(path) && path.IndexOfAny(System.IO.Path.GetInvalidPathChars()) < 0;

    public bool CheckDirectoryEntryName(string name) => FileUtility.IsValidDirectoryEntryName(name);

    // ── OpenFile ──────────────────────────────────────────────────────────

    public bool IsOpen(FileName fileName) => _openedFiles.ContainsKey(fileName.ToString());

    public IViewContent OpenFile(FileName fileName, bool switchToOpenedView = true)
        => _openFile?.Invoke(fileName.ToString(), switchToOpenedView)
            ?? throw new InvalidOperationException("Workbench file operations are not initialized.");

    public IViewContent OpenFileWith(FileName fileName, IDisplayBinding displayBinding, bool switchToOpenedView = true)
        => throw new NotImplementedException();

    public IEnumerable<IViewContent> ShowOpenWithDialog(IEnumerable<FileName> fileNames, bool switchToOpenedView = true)
        => throw new NotImplementedException();

    public IViewContent NewFile(string defaultName, string content)
        => throw new NotImplementedException();

    public IViewContent NewFile(string defaultName, byte[] content)
        => throw new NotImplementedException();

    public IReadOnlyList<FileName> OpenPrimaryFiles => new List<FileName>(_openedFiles.Keys.Select(FileName.Create).Where(f => f is not null)!);

    public IViewContent GetOpenFile(FileName fileName)
    {
        var workbench = ServiceSingleton.GetRequiredService<IWorkbench>();
        return workbench.ViewContentCollection.FirstOrDefault(view =>
            string.Equals(view.PrimaryFileName?.ToString(), fileName.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    public IViewContent JumpToFilePosition(FileName fileName, int line, int column)
        => _jumpToFilePosition?.Invoke(fileName.ToString(), line, column)
            ?? throw new InvalidOperationException("Workbench file operations are not initialized.");

    // ── Remove/Rename/Copy ────────────────────────────────────────────────

    public void RemoveFile(string fileName, bool isDirectory)
    {
        var removing = new FileCancelEventArgs(fileName, isDirectory);
        FileRemoving?.Invoke(this, removing);
        if (removing.Cancel) return;

        if (isDirectory) System.IO.Directory.Delete(fileName, recursive: true);
        else System.IO.File.Delete(fileName);

        _openedFiles.Remove(fileName);
        FileRemoved?.Invoke(this, new FileEventArgs(fileName, isDirectory));
    }

    public bool RenameFile(string oldName, string newName, bool isDirectory)
    {
        var renaming = new FileRenamingEventArgs(oldName, newName, isDirectory);
        FileRenaming?.Invoke(this, renaming);
        if (renaming.Cancel) return false;

        if (isDirectory) System.IO.Directory.Move(oldName, newName);
        else System.IO.File.Move(oldName, newName, overwrite: false);

        NotifyFileRenamed(oldName, newName);
        return true;
    }

    public bool CopyFile(string oldName, string newName, bool isDirectory, bool overwrite)
    {
        var copying = new FileRenamingEventArgs(oldName, newName, isDirectory);
        FileCopying?.Invoke(this, copying);
        if (copying.Cancel) return false;

        if (isDirectory)
        {
            foreach (var file in System.IO.Directory.EnumerateFiles(oldName, "*", System.IO.SearchOption.AllDirectories))
            {
                var dest = System.IO.Path.Combine(newName, System.IO.Path.GetRelativePath(oldName, file));
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dest)!);
                System.IO.File.Copy(file, dest, overwrite);
            }
        }
        else
        {
            System.IO.File.Copy(oldName, newName, overwrite);
        }

        FileCopied?.Invoke(this, new FileRenameEventArgs(oldName, newName, isDirectory));
        return true;
    }

    // ── FireFile* ─────────────────────────────────────────────────────────

    public bool FireFileReplacing(string fileName, bool isDirectory)
    {
        var args = new FileCancelEventArgs(fileName, isDirectory);
        FileReplacing?.Invoke(this, args);
        return !args.Cancel;
    }

    public void FireFileReplaced(string fileName, bool isDirectory)
        => FileReplaced?.Invoke(this, new FileEventArgs(fileName, isDirectory));

    public void FireFileCreated(string fileName, bool isDirectory)
        => FileCreated?.Invoke(this, new FileEventArgs(fileName, isDirectory));

    // ── Minimal OpenedFile implementation ─────────────────────────────────

    private sealed class UnoOpenedFile : OpenedFile
    {
        public UnoOpenedFile(string filePath)
        {
            FileName = ICSharpCode.Core.FileName.Create(filePath);
        }

        public override event EventHandler? FileClosed;

        public void NotifyClosed()
        {
            FileClosed?.Invoke(this, EventArgs.Empty);
        }
    }
}
