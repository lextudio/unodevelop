using System;

namespace ICSharpCode.SharpDevelop.Workbench;

public interface IFileService
{
    event EventHandler<FileEventArgs> FileOpened;
    event EventHandler<FileEventArgs> FileClosed;
    event EventHandler<FileRenameEventArgs> FileRenamed;

    void NotifyFileOpened(string filePath);
    void NotifyFileClosed(string filePath);
    void NotifyFileRenamed(string oldPath, string newPath);
}
