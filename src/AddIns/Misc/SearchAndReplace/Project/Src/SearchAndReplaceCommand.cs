using ICSharpCode.Core;
using ICSharpCode.AvalonEdit;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Editor;

namespace UnoDevelop.AddIns.Misc.SearchAndReplace;

public sealed class ShowSearchAndReplaceCommand : AbstractMenuCommand
{
    public override void Run() => SD.Workbench.ShowView(new SearchAndReplaceViewContent());
}

public sealed class FindCommand : AbstractMenuCommand
{
    public override void Run()
    {
        if (GetActiveUnoEditor() is { } editor)
        {
            editor.OpenSearchPanel();
        }
    }

    internal static TextEditor? GetActiveUnoEditor()
    {
        if (SD.Workbench.ActiveViewContent?.GetService(typeof(ITextEditor)) is not ITextEditor textEditor)
        {
            return null;
        }

        return textEditor.GetService(typeof(TextEditor)) as TextEditor;
    }
}

public sealed class FindNextCommand : AbstractMenuCommand
{
    public override void Run()
    {
        if (FindCommand.GetActiveUnoEditor() is { } editor)
        {
            editor.FindNext();
        }
    }
}
