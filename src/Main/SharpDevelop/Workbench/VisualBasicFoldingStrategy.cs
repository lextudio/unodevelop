using System;
using System.Collections.Generic;
using System.Linq;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Folding;
using Microsoft.CodeAnalysis.VisualBasic;

namespace UnoDevelop.Workbench;

internal sealed class VisualBasicFoldingStrategy
{
    public void UpdateFoldings(FoldingManager manager, TextDocument document)
    {
        var tree = VisualBasicSyntaxTree.ParseText(document.Text);
        var text = tree.GetText();
        var foldings = new List<NewFolding>();

        foreach (var node in tree.GetRoot().DescendantNodes())
        {
            if (!node.GetType().Name.EndsWith("BlockSyntax", StringComparison.Ordinal))
                continue;

            var startLine = text.Lines.GetLineFromPosition(node.SpanStart);
            var endLine = text.Lines.GetLineFromPosition(node.Span.End);
            if (startLine.LineNumber >= endLine.LineNumber)
                continue;

            var heading = startLine.ToString().Trim();
            foldings.Add(new NewFolding(node.SpanStart, node.Span.End)
            {
                Name = string.IsNullOrEmpty(heading) ? "..." : heading + " ..."
            });
        }

        manager.UpdateFoldings(foldings.OrderBy(folding => folding.StartOffset), -1);
    }
}
