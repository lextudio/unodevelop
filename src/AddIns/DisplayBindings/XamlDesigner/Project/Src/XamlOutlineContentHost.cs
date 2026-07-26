using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using ICSharpCode.SharpDevelop.Editor;
using ICSharpCode.SharpDevelop.Gui;
using ICSharpCode.SharpDevelop.Workbench;
using Microsoft.UI.Xaml.Controls;

namespace ICSharpCode.XamlDesigner;

public sealed class XamlOutlineContentHost : UserControl, IOutlineContentHost
{
    readonly IViewContent _primary;
    readonly TreeView _tree = new();
    IReadOnlyList<OutlineEntry> _entries = Array.Empty<OutlineEntry>();
    public string? LastError { get; private set; }

    public XamlOutlineContentHost(IViewContent primary)
    {
        _primary = primary;
        _tree.ItemInvoked += OnItemInvoked;
        Content = _tree;
        Refresh();
    }

    public object OutlineContent
    {
        get
        {
            Refresh();
            return this;
        }
    }

    void Refresh()
    {
        LastError = null;
        try
        {
            var text = (_primary.GetService(typeof(ITextEditor)) as ITextEditor)?.Document.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var document = XDocument.Parse(text, LoadOptions.SetLineInfo);
            var entries = document.Root is null
                ? Array.Empty<OutlineEntry>()
                : new[] { CreateEntry(document.Root) };
            _tree.RootNodes.Clear();
            foreach (var entry in entries)
                _tree.RootNodes.Add(CreateTreeNode(entry));
            _entries = entries;
        }
        catch (Exception ex)
        {
            LastError = ex.ToString();
        }
    }

    static OutlineEntry CreateEntry(XElement element)
    {
        var lineInfo = (IXmlLineInfo)element;
        var name = element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName == "Name")?.Value;
        return new OutlineEntry(
            element.Name.LocalName,
            name,
            lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1,
            element.Elements().Select(CreateEntry).ToArray());
    }

    static TreeViewNode CreateTreeNode(OutlineEntry entry)
    {
        var node = new TreeViewNode
        {
            Content = entry,
            IsExpanded = true
        };
        foreach (var child in entry.Children)
            node.Children.Add(CreateTreeNode(child));
        return node;
    }

    void OnItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is OutlineEntry entry
            && _primary.GetService(typeof(ITextEditor)) is ITextEditor editor)
        {
            editor.JumpTo(entry.Line, 1);
        }
    }

    public IReadOnlyList<object> GetSnapshot()
    {
        var result = new List<object>();
        foreach (var entry in _entries)
            AddSnapshot(entry, 0, result);
        return result;
    }

    static void AddSnapshot(OutlineEntry entry, int depth, List<object> result)
    {
        result.Add(new { entry.ElementName, entry.Name, entry.Line, Depth = depth });
        foreach (var child in entry.Children)
            AddSnapshot(child, depth + 1, result);
    }

    sealed record OutlineEntry(
        string ElementName,
        string? Name,
        int Line,
        IReadOnlyList<OutlineEntry> Children)
    {
        public override string ToString()
            => string.IsNullOrWhiteSpace(Name) ? ElementName : $"{ElementName} ({Name})";
    }
}
