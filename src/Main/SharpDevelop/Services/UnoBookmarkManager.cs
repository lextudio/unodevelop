using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Editor;
using ICSharpCode.SharpDevelop.Editor.Bookmarks;
using ICSharpCode.SharpDevelop.Project;

namespace UnoDevelop.Services;

internal sealed class UnoBookmarkManager : IBookmarkManager
{
    private readonly List<SDBookmark> _bookmarks = new();
    private bool _initialized;

    private void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;
        SD.ProjectService.SolutionClosed += delegate { Clear(); };
    }

    public IReadOnlyCollection<SDBookmark> Bookmarks
    {
        get
        {
            EnsureInitialized();
            SD.MainThread.VerifyAccess();
            return _bookmarks;
        }
    }

    public event EventHandler<BookmarkEventArgs> BookmarkAdded;
    public event EventHandler<BookmarkEventArgs> BookmarkRemoved;

    public IEnumerable<SDBookmark> GetBookmarks(FileName fileName)
    {
        EnsureInitialized();
        if (fileName == null)
            throw new ArgumentNullException(nameof(fileName));
        SD.MainThread.VerifyAccess();
        return _bookmarks.Where(b => b.FileName == fileName);
    }

    public IEnumerable<SDBookmark> GetProjectBookmarks(IProject project)
    {
        EnsureInitialized();
        SD.MainThread.VerifyAccess();
        var projectBookmarks = new List<SDBookmark>();
        foreach (var mark in _bookmarks)
        {
            if (mark.IsSaved && mark.FileName != null && project.IsFileInProject(mark.FileName))
                projectBookmarks.Add(mark);
        }
        return projectBookmarks;
    }

    public void AddMark(SDBookmark bookmark)
    {
        EnsureInitialized();
        SD.MainThread.VerifyAccess();
        if (bookmark == null) return;
        if (_bookmarks.Contains(bookmark)) return;
        if (_bookmarks.Exists(b => IsEqualBookmark(b, bookmark))) return;
        _bookmarks.Add(bookmark);
        OnAdded(new BookmarkEventArgs(bookmark));
    }

    public void AddMark(SDBookmark bookmark, IDocument document, int line)
    {
        EnsureInitialized();
        int lineStartOffset = document.GetLineByNumber(line).Offset;
        int column = 1 + DocumentUtilities.GetWhitespaceAfter(document, lineStartOffset).Length;
        bookmark.Location = new TextLocation(line, column);
        bookmark.FileName = FileName.Create(document.FileName);
        AddMark(bookmark);
    }

    private static bool IsEqualBookmark(SDBookmark a, SDBookmark b)
    {
        if (a == b)
            return true;
        if (a == null || b == null)
            return false;
        if (a.GetType() != b.GetType())
            return false;
        if (a.FileName != b.FileName)
            return false;
        return a.LineNumber == b.LineNumber;
    }

    public void RemoveMark(SDBookmark bookmark)
    {
        EnsureInitialized();
        SD.MainThread.VerifyAccess();
        if (_bookmarks.Remove(bookmark))
            OnRemoved(new BookmarkEventArgs(bookmark));
    }

    public void Clear()
    {
        EnsureInitialized();
        SD.MainThread.VerifyAccess();
        var copy = _bookmarks.ToList();
        _bookmarks.Clear();
        foreach (var b in copy)
            OnRemoved(new BookmarkEventArgs(b));
    }

    internal record BreakpointEntry(string FilePath, int Line);

    internal static void SerializeEntriesTo(string path, IEnumerable<BreakpointEntry> entries)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(entries.ToList());
        File.WriteAllText(path, json);
    }

    internal static List<BreakpointEntry> DeserializeEntriesFrom(string path)
    {
        if (!File.Exists(path)) return new List<BreakpointEntry>();
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<BreakpointEntry>>(json) ?? new List<BreakpointEntry>();
    }

    private static string GetPreferencesDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UnoDevelop", "preferences");
    }

    private static string GetBreakpointsFilePath(IProject project)
    {
        var projectFileName = project.FileName.ToString();
        var directory = GetPreferencesDirectory();
        var hash = projectFileName.ToUpperInvariant().GetStableHashCode().ToString("x");
        return Path.Combine(directory,
            Path.GetFileName(projectFileName) + "." + hash + ".breakpoints.json");
    }

    private static string GetBreakpointsFilePath(ISolution solution)
    {
        var solutionFileName = solution.FileName?.ToString() ?? "default";
        var directory = GetPreferencesDirectory();
        var hash = solutionFileName.ToUpperInvariant().GetStableHashCode().ToString("x");
        return Path.Combine(directory,
            Path.GetFileName(solutionFileName) + "." + hash + ".breakpoints.json");
    }

    public void SaveToProject(IProject project)
    {
        var path = GetBreakpointsFilePath(project);
        Dbg($"SaveToProject START: project={project.Name}, path={path}, total_bookmarks={_bookmarks.Count}");
        try
        {
            var filtered = _bookmarks
                .Where(b => b.IsSaved && b.FileName != null && project.IsFileInProject(b.FileName))
                .ToList();
            Dbg($"SaveToProject: after IsSaved+IsFileInProject filter: {filtered.Count}/{_bookmarks.Count}");
            var entries = filtered
                .Select(b => new BreakpointEntry(b.FileName.ToString(), b.LineNumber))
                .ToList();
            if (entries.Count > 0)
            {
                foreach (var e in entries)
                    Dbg($"SaveToProject entry: {e.FilePath}:{e.Line}");
            }
            else
            {
                Dbg("SaveToProject: NO entries to save");
            }
            SerializeEntriesTo(path, entries);
            Dbg($"SaveToProject END: wrote {entries.Count} entries to {path}");
        }
        catch (Exception ex)
        {
            Dbg($"SaveToProject FAILED: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        }
    }

    public void LoadFromProject(IProject project)
    {
        var path = GetBreakpointsFilePath(project);
        Dbg($"LoadFromProject START: project={project.Name}, path={path}");
        try
        {
            var entries = DeserializeEntriesFrom(path);
            if (entries.Count == 0)
            {
                Dbg("LoadFromProject: no entries to load");
                return;
            }
            Dbg($"LoadFromProject: deserialized {entries.Count} entries");
            foreach (var entry in entries)
            {
                var bookmark = new ICSharpCode.SharpDevelop.Editor.Bookmarks.Bookmark();
                bookmark.FileName = ICSharpCode.Core.FileName.Create(entry.FilePath);
                bookmark.Location = new TextLocation(entry.Line, 1);
                AddMark(bookmark);
                Dbg($"LoadFromProject added bookmark: {entry.FilePath}:{entry.Line}");
            }
            Dbg($"LoadFromProject END: total bookmarks after load = {_bookmarks.Count}");
        }
        catch (Exception ex)
        {
            Dbg($"LoadFromProject FAILED: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        }
    }

    public void SaveToSolution(ISolution solution)
    {
        var path = GetBreakpointsFilePath(solution);
        Dbg($"SaveToSolution START: solution={solution.Name}, path={path}, total_bookmarks={_bookmarks.Count}");
        try
        {
            var entries = _bookmarks
                .Where(b => b.IsSaved && b.FileName != null)
                .Select(b => new BreakpointEntry(b.FileName.ToString(), b.LineNumber))
                .ToList();
            Dbg($"SaveToSolution: saving {entries.Count} entries");
            SerializeEntriesTo(path, entries);
            Dbg($"SaveToSolution END: wrote {entries.Count} entries to {path}");
        }
        catch (Exception ex)
        {
            Dbg($"SaveToSolution FAILED: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        }
    }

    public void LoadFromSolution(ISolution solution)
    {
        var path = GetBreakpointsFilePath(solution);
        Dbg($"LoadFromSolution START: solution={solution.Name}, path={path}");
        try
        {
            var entries = DeserializeEntriesFrom(path);
            if (entries.Count == 0)
            {
                Dbg("LoadFromSolution: no entries to load");
                return;
            }
            Dbg($"LoadFromSolution: deserialized {entries.Count} entries");
            foreach (var entry in entries)
            {
                var bookmark = new ICSharpCode.SharpDevelop.Editor.Bookmarks.Bookmark();
                bookmark.FileName = ICSharpCode.Core.FileName.Create(entry.FilePath);
                bookmark.Location = new TextLocation(entry.Line, 1);
                AddMark(bookmark);
                Dbg($"LoadFromSolution added bookmark: {entry.FilePath}:{entry.Line}");
            }
            Dbg($"LoadFromSolution END: total bookmarks after load = {_bookmarks.Count}");
        }
        catch (Exception ex)
        {
            Dbg($"LoadFromSolution FAILED: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private static void Dbg(string msg)
    {
        try { File.AppendAllText("/tmp/unodevelop-debug.log", $"[{DateTime.Now:HH:mm:ss.fff}] UnoBookmarkManager: {msg}\n"); } catch { }
    }

    private void OnRemoved(BookmarkEventArgs e)
    {
        BookmarkRemoved?.Invoke(null, e);
    }

    private void OnAdded(BookmarkEventArgs e)
    {
        BookmarkAdded?.Invoke(null, e);
    }

    public bool RemoveBookmarkAt(FileName fileName, int line, Predicate<SDBookmark> predicate = null)
    {
        EnsureInitialized();
        foreach (var bookmark in GetBookmarks(fileName))
        {
            if (bookmark.CanToggle && bookmark.LineNumber == line)
            {
                if (predicate == null || predicate(bookmark))
                {
                    RemoveMark(bookmark);
                    return true;
                }
            }
        }
        return false;
    }

    public void RemoveAll(Predicate<SDBookmark> match)
    {
        EnsureInitialized();
        if (match == null)
            throw new ArgumentNullException(nameof(match));
        SD.MainThread.VerifyAccess();
        for (int index = _bookmarks.Count - 1; index >= 0; --index)
        {
            var bookmark = _bookmarks[index];
            if (match(bookmark))
            {
                _bookmarks.RemoveAt(index);
                OnRemoved(new BookmarkEventArgs(bookmark));
            }
        }
    }
}
