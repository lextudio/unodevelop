using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.SharpDevelop.Editor;
using UnoDevelop.OptionPanels;

namespace UnoDevelop.Services;

internal sealed class UnoEditorControlService : IEditorControlService
{
    public ITextEditorOptions GlobalOptions { get; } = UnoCodeEditorOptions.Instance;

    public ITextEditor CreateEditor(out object control)
    {
        control = new object();
        return new AvalonEditTextEditorAdapter(control);
    }

    public IHighlighter CreateHighlighter(IDocument document) => null!;
}
