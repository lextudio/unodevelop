using System;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;

namespace UnoDevelop.Workbench;

/// <summary>
/// Adapts AvalonEdit XSHD definitions to UnoEdit's explicit highlighted-line pipeline.
/// </summary>
internal sealed class XshdHighlightedLineSource : IHighlightedLineSource
{
    readonly IHighlightingDefinition _definition;
    DocumentHighlighter? _highlighter;

    public XshdHighlightedLineSource(IHighlightingDefinition definition)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
    }

    public event EventHandler? HighlightingInvalidated;

    public void SetDocument(TextDocument? document)
    {
        _highlighter?.Dispose();
        _highlighter = document is null ? null : new DocumentHighlighter(document, _definition);
        HighlightingInvalidated?.Invoke(this, EventArgs.Empty);
    }

    public HighlightedLine HighlightLine(int lineNumber)
        => _highlighter?.HighlightLine(lineNumber)
            ?? throw new InvalidOperationException("No document attached to the highlighter.");

    public void Dispose()
    {
        _highlighter?.Dispose();
        _highlighter = null;
    }
}
