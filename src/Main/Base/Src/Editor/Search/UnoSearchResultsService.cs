using System;
using System.Collections.Generic;
using System.Linq;
using ICSharpCode.Core;

namespace ICSharpCode.SharpDevelop.Editor.Search;

public sealed class UnoSearchResultsService
{
    private readonly List<SearchResultEntry> _results = new();

    public string Title { get; private set; } = "Search Results";

    public IReadOnlyList<SearchResultEntry> Results => _results;

    public event EventHandler? ResultsChanged;

    public void ShowSearchResults(string title, IEnumerable<SearchResultEntry> results)
    {
        Title = string.IsNullOrWhiteSpace(title) ? "Search Results" : title;
        _results.Clear();
        _results.AddRange(results ?? Enumerable.Empty<SearchResultEntry>());
        ResultsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        _results.Clear();
        Title = "Search Results";
        ResultsChanged?.Invoke(this, EventArgs.Empty);
    }
}

public sealed record SearchResultEntry(
    FileName FileName,
    int Line,
    int Column,
    int Offset,
    int Length,
    string Preview)
{
    public string FilePath => FileName.ToString();
    public string Location => $"{FilePath}:{Line}:{Column}";
}
