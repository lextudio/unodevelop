using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Editor;
using ICSharpCode.SharpDevelop.Editor.Bookmarks;
using ICSharpCode.SharpDevelop.Gui;
using ICSharpCode.SharpDevelop.Workbench;
using Microsoft.UI.Xaml;
using UnoDevelop.Services;

namespace UnoDevelop.Commands;

internal sealed class NewFileShellCommand : AbstractMenuCommand
{
    public override void Run() => _ = RunAsync();

    private static async Task RunAsync()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "New File",
            Filter = "All files|*.*",
            FileName = "Untitled",
        };
        if (await dlg.ShowDialogAsync() != true) return;
        File.WriteAllText(dlg.FileName, string.Empty);
        var fileService = ServiceSingleton.GetRequiredService<IFileService>();
        fileService.OpenFile(FileName.Create(dlg.FileName)!);
    }
}

internal sealed class OpenFileShellCommand : AbstractMenuCommand
{
    public override void Run() => _ = RunAsync();

    private static async Task RunAsync()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open File",
            Filter = "All files|*.*",
        };
        if (await dlg.ShowDialogAsync() != true || string.IsNullOrEmpty(dlg.FileName))
            return;
        var fileService = ServiceSingleton.GetRequiredService<IFileService>();
        fileService.OpenFile(FileName.Create(dlg.FileName)!);
    }
}

internal sealed class SaveFileShellCommand : AbstractMenuCommand
{
    public override void Run() => _ = RunAsync();

    private static async Task RunAsync()
    {
        var window = SD.Workbench.ActiveWorkbenchWindow;
        if (window is null) return;
        foreach (var vc in window.ViewContents)
        {
            if (!vc.IsDirty || vc.IsViewOnly) continue;
            var customizedCommands = vc.GetService<ICustomizedCommands>();
            if (customizedCommands is not null && customizedCommands.SaveCommand())
                continue;
            foreach (var file in vc.Files.ToArray())
            {
                if (file.IsDirty)
                {
                    if (file.IsUntitled)
                        await SaveAsAsync(file);
                    else
                        await SaveToDiskAsync(file);
                }
            }
        }
    }

    internal static async Task SaveToDiskAsync(OpenedFile file)
    {
        await Task.Run(() =>
        {
            try
            {
                using var stream = file.OpenRead();
                if (stream is null) return;
                var dir = Path.GetDirectoryName(file.FileName);
                if (dir is not null) Directory.CreateDirectory(dir);
                using var fileStream = File.Create(file.FileName);
                stream.CopyTo(fileStream);
            }
            catch (Exception ex)
            {
                SD.Log.Error($"Error saving {file.FileName}", ex);
            }
        });
    }

    internal static async Task SaveAsAsync(OpenedFile file)
    {
        var fdiag = new Microsoft.Win32.SaveFileDialog
        {
            OverwritePrompt = true,
            Filter = "All files|*.*",
        };
        if (await fdiag.ShowDialogAsync() == true)
        {
            var fileName = FileName.Create(fdiag.FileName);
            if (fileName is null) return;
            var fileService = ServiceSingleton.GetRequiredService<IFileService>();
            if (!fileService.CheckFileName(fileName)) return;
            try
            {
                using var stream = file.OpenRead();
                if (stream is null) return;
                var dir = Path.GetDirectoryName(fileName);
                if (dir is not null) Directory.CreateDirectory(dir);
                using var fileStream = File.Create(fileName);
                await stream.CopyToAsync(fileStream).ConfigureAwait(false);
                SD.FileService.RecentOpen.AddRecentFile(fileName);
            }
            catch (Exception ex)
            {
                SD.Log.Error($"Error saving as {fileName}", ex);
            }
        }
    }
}

internal sealed class SaveAllShellCommand : AbstractMenuCommand
{
    public override void Run() => _ = RunAsync();

    private static async Task RunAsync()
    {
        foreach (var content in SD.Workbench.ViewContentCollection)
        {
            if (!content.IsDirty) continue;
            var customizedCommands = content.GetService<ICustomizedCommands>();
            if (customizedCommands is not null)
            {
                customizedCommands.SaveCommand();
                continue;
            }
            foreach (var file in content.Files)
            {
                if (file.IsDirty)
                {
                    if (file.IsUntitled)
                        await SaveFileShellCommand.SaveAsAsync(file);
                    else
                        await SaveFileShellCommand.SaveToDiskAsync(file);
                }
            }
        }
        foreach (var file in SD.FileService.OpenedFiles)
        {
            if (file.IsDirty)
            {
                if (file.IsUntitled)
                    await SaveFileShellCommand.SaveAsAsync(file);
                else
                    await SaveFileShellCommand.SaveToDiskAsync(file);
            }
        }
    }
}

internal sealed class CutShellCommand : AbstractMenuCommand
{
    public override void Run()
    {
        var editor = SD.GetActiveViewContentService<ITextEditor>();
        if (editor is null) return;
        var text = editor.SelectedText;
        if (text.Length > 0)
        {
            try
            {
                var data = new Windows.ApplicationModel.DataTransfer.DataPackage();
                data.SetText(text);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
            }
            catch { }
            editor.Document.Remove(editor.SelectionStart, text.Length);
        }
    }
}

internal sealed class CopyShellCommand : AbstractMenuCommand
{
    public override void Run()
    {
        var editor = SD.GetActiveViewContentService<ITextEditor>();
        if (editor is null) return;
        var text = editor.SelectedText;
        if (text.Length > 0)
        {
            try
            {
                var data = new Windows.ApplicationModel.DataTransfer.DataPackage();
                data.SetText(text);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
            }
            catch { }
        }
    }
}

internal sealed class PasteShellCommand : AbstractMenuCommand
{
    public override void Run() => _ = PasteAsync();

    private static async Task PasteAsync()
    {
        var editor = SD.GetActiveViewContentService<ITextEditor>();
        if (editor is null) return;
        try
        {
            var data = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
            if (data.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
            {
                var text = await data.GetTextAsync();
                if (text is not null)
                {
                    if (editor.SelectionLength > 0)
                        editor.Document.Replace(editor.SelectionStart, editor.SelectionLength, text);
                    else
                        editor.Document.Insert(editor.Caret.Offset, text);
                }
            }
        }
        catch { }
    }
}

internal sealed class DeleteShellCommand : AbstractMenuCommand
{
    public override void Run()
    {
        var editor = SD.GetActiveViewContentService<ITextEditor>();
        if (editor is null) return;
        if (editor.SelectionLength > 0)
            editor.Document.Remove(editor.SelectionStart, editor.SelectionLength);
        else if (editor.Caret.Offset < editor.Document.TextLength)
            editor.Document.Remove(editor.Caret.Offset, 1);
    }
}

internal sealed class UndoShellCommand : AbstractMenuCommand
{
    public override void Run()
    {
        var editor = SD.GetActiveViewContentService<ITextEditor>();
        if (editor is null) return;
        if (editor.Document is TextDocument doc)
            doc.UndoStack.Undo();
    }
}

internal sealed class RedoShellCommand : AbstractMenuCommand
{
    public override void Run()
    {
        var editor = SD.GetActiveViewContentService<ITextEditor>();
        if (editor is null) return;
        if (editor.Document is TextDocument doc)
            doc.UndoStack.Redo();
    }
}

internal sealed class BuildProjectShellCommand : AbstractMenuCommand
{
    public override void Run() => _ = MainPage.Current?.BuildSolutionAsync();
}

internal sealed class NavigateBackShellCommand : AbstractMenuCommand
{
    public override void Run() { }
}

internal sealed class NavigateForwardShellCommand : AbstractMenuCommand
{
    public override void Run() { }
}

internal sealed class CommentRegionShellCommand : AbstractMenuCommand
{
    public override void Run()
    {
        var editor = SD.GetActiveViewContentService<ITextEditor>();
        if (editor is null) return;
        using (editor.Document.OpenUndoGroup())
            editor.Language.FormattingStrategy.SurroundSelectionWithComment(editor);
    }
}

internal sealed class ToggleBookmarkShellCommand : AbstractMenuCommand
{
    public override void Run()
    {
        var editor = SD.GetActiveViewContentService<ITextEditor>();
        if (editor is null) return;
        int lineNumber = editor.Caret.Line;
        if (!SD.BookmarkManager.RemoveBookmarkAt(editor.FileName, lineNumber, b => b is Bookmark))
            SD.BookmarkManager.AddMark(new Bookmark(), editor.Document, lineNumber);
    }
}

internal sealed class PrevBookmarkShellCommand : AbstractMenuCommand
{
    public override void Run()
    {
        var editor = SD.GetActiveViewContentService<ITextEditor>();
        if (editor is null) return;
        int line = editor.Caret.Line;
        var bookmarks = (from b in SD.BookmarkManager.Bookmarks.OfType<Bookmark>()
                         where b.CanToggle && b.FileName == editor.FileName
                         orderby b.LineNumber
                         select b).ToList();
        var bookmark = bookmarks.LastOrDefault(b => b.LineNumber < line);
        if (bookmark is null && bookmarks.Count > 0)
            bookmark = bookmarks[bookmarks.Count - 1];
        if (bookmark is not null)
            SD.FileService.JumpToFilePosition(bookmark.FileName, bookmark.LineNumber, bookmark.ColumnNumber);
    }
}

internal sealed class NextBookmarkShellCommand : AbstractMenuCommand
{
    public override void Run()
    {
        var editor = SD.GetActiveViewContentService<ITextEditor>();
        if (editor is null) return;
        int line = editor.Caret.Line;
        var bookmarks = (from b in SD.BookmarkManager.Bookmarks.OfType<Bookmark>()
                         where b.CanToggle && b.FileName == editor.FileName
                         orderby b.LineNumber
                         select b).ToList();
        var bookmark = bookmarks.FirstOrDefault(b => b.LineNumber > line);
        if (bookmark is null && bookmarks.Count > 0)
            bookmark = bookmarks[0];
        if (bookmark is not null)
            SD.FileService.JumpToFilePosition(bookmark.FileName, bookmark.LineNumber, bookmark.ColumnNumber);
    }
}

internal sealed class ClearBookmarksShellCommand : AbstractMenuCommand
{
    public override void Run()
    {
        var bookmarks = (from b in SD.BookmarkManager.Bookmarks.OfType<Bookmark>()
                         where b.CanToggle
                         select b).ToList();
        foreach (var b in bookmarks)
            SD.BookmarkManager.RemoveMark(b);
    }
}
