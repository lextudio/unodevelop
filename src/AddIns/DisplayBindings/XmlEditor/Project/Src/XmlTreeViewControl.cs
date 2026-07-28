using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Xml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using ICSharpCode.Core;

namespace ICSharpCode.XmlEditor;

public class XmlTreeViewKeyPressedEventArgs : EventArgs
{
    public XmlTreeViewKeyPressedEventArgs(Windows.System.VirtualKey keyData) => KeyData = keyData;
    public Windows.System.VirtualKey KeyData { get; }
}

public class XmlTreeViewControl : UserControl
{
    readonly TreeView _treeView;
    readonly TreeViewNode _rootNode;
    readonly Dictionary<XmlTreeNode, TreeViewNode> _nodeMap = new();

    XmlDocument _document;
    bool _syncing;

    enum InsertionMode { Before, After }

    public event EventHandler<XmlTreeViewKeyPressedEventArgs> TreeViewKeyPressed;

    public XmlTreeViewControl()
    {
        _rootNode = new TreeViewNode();
        _treeView = new TreeView
        {
            SelectionMode = TreeViewSelectionMode.Single,
            ItemTemplate = CreateNodeTemplate(),
        };
        _treeView.ItemInvoked += (s, e) => OnSelectedItemChanged();
        _treeView.KeyDown += OnKeyDown;
        Content = _treeView;
    }

    static DataTemplate CreateNodeTemplate()
    {
        return new DataTemplate(() =>
        {
            var textBlock = new TextBlock();
            textBlock.SetBinding(TextBlock.TextProperty, new Binding
            {
                Path = new PropertyPath("Content.Text"),
                Mode = BindingMode.OneWay,
            });
            return textBlock;
        });
    }

    public ObservableCollection<XmlTreeNode> Nodes => _rootNode.Content is null
        ? new ObservableCollection<XmlTreeNode>()
        : throw new NotSupportedException("Use Document to set nodes");

    public XmlDocument Document
    {
        get => _document;
        set
        {
            _document = value;
            ShowDocument();
        }
    }

    public XmlTreeNode SelectedNode
    {
        get
        {
            var selectedTreeViewNode = _treeView.SelectedNode;
            return selectedTreeViewNode?.Content as XmlTreeNode;
        }
    }

    public XmlElement SelectedElement
    {
        get
        {
            var node = SelectedNode as XmlElementTreeNode;
            return node?.XmlElement;
        }
    }

    public bool IsElementSelected => SelectedElement != null;

    public XmlText SelectedTextNode
    {
        get
        {
            var node = SelectedNode as XmlTextTreeNode;
            return node?.XmlText;
        }
    }

    public XmlComment SelectedComment
    {
        get
        {
            var node = SelectedNode as XmlCommentTreeNode;
            return node?.XmlComment;
        }
    }

    public bool IsTextNodeSelected => SelectedTextNode != null;

    public void SaveViewState(Properties properties) { }
    public void RestoreViewState(Properties properties) { }

    public void AppendChildElement(XmlElement element)
    {
        var selectedNode = SelectedNode as XmlElementTreeNode;
        if (selectedNode != null)
        {
            var newNode = new XmlElementTreeNode(element);
            newNode.AddTo(selectedNode);
            selectedNode.Expand();
        }
    }

    public void AppendChildTextNode(XmlText textNode)
    {
        var selectedNode = SelectedNode as XmlElementTreeNode;
        if (selectedNode != null)
        {
            var newNode = new XmlTextTreeNode(textNode);
            newNode.AddTo(selectedNode);
            selectedNode.Expand();
        }
    }

    public void InsertElementBefore(XmlElement element)
        => InsertElement(element, InsertionMode.Before);

    public void InsertElementAfter(XmlElement element)
        => InsertElement(element, InsertionMode.After);

    public void RemoveElement(XmlElement element)
    {
        var node = FindElement(element);
        node?.Remove();
    }

    public void RemoveTextNode(XmlText textNode)
    {
        var node = FindTextNode(textNode);
        node?.Remove();
    }

    public void InsertTextNodeBefore(XmlText textNode)
        => InsertTextNode(textNode, InsertionMode.Before);

    public void InsertTextNodeAfter(XmlText textNode)
        => InsertTextNode(textNode, InsertionMode.After);

    public void UpdateTextNode(XmlText textNode)
    {
        var node = FindTextNode(textNode);
        node?.Update();
    }

    public void UpdateComment(XmlComment comment)
    {
        var node = FindComment(comment);
        node?.Update();
    }

    public void AppendChildComment(XmlComment comment)
    {
        var selectedNode = SelectedNode as XmlElementTreeNode;
        if (selectedNode != null)
        {
            var newNode = new XmlCommentTreeNode(comment);
            newNode.AddTo(selectedNode);
            selectedNode.Expand();
        }
    }

    public void RemoveComment(XmlComment comment)
    {
        var node = FindComment(comment);
        node?.Remove();
    }

    public void InsertCommentBefore(XmlComment comment)
        => InsertComment(comment, InsertionMode.Before);

    public void InsertCommentAfter(XmlComment comment)
        => InsertComment(comment, InsertionMode.After);

    public void ShowCut(XmlNode node) => ShowCut(node, true);
    public void HideCut(XmlNode node) => ShowCut(node, false);

    void OnKeyDown(object sender, KeyRoutedEventArgs e)
        => TreeViewKeyPressed?.Invoke(this, new XmlTreeViewKeyPressedEventArgs(e.Key));

    void OnSelectedItemChanged() { }

    void ShowDocument()
    {
        _rootNode.Children.Clear();
        _nodeMap.Clear();
        if (_document != null)
        {
            foreach (XmlNode node in _document.ChildNodes)
            {
                switch (node.NodeType)
                {
                    case XmlNodeType.Element:
                        var elementNode = new XmlElementTreeNode((XmlElement)node);
                        _rootNode.Children.Add(BuildTreeViewNode(elementNode));
                        break;
                    case XmlNodeType.Comment:
                        var commentNode = new XmlCommentTreeNode((XmlComment)node);
                        _rootNode.Children.Add(BuildTreeViewNode(commentNode));
                        break;
                }
            }
        }
        _treeView.RootNodes.Clear();
        foreach (var child in _rootNode.Children)
            _treeView.RootNodes.Add(child);
    }

    TreeViewNode BuildTreeViewNode(XmlTreeNode modelNode)
    {
        var tvn = new TreeViewNode { Content = modelNode };
        _nodeMap[modelNode] = tvn;
        foreach (var child in modelNode.Nodes)
            tvn.Children.Add(BuildTreeViewNode(child));
        modelNode.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(XmlTreeNode.IsExpanded))
                tvn.IsExpanded = modelNode.IsExpanded;
        };
        return tvn;
    }

    XmlTreeNode SelectedModelNode => _treeView.SelectedNode?.Content as XmlTreeNode;

    XmlElementTreeNode SelectedElementNode => SelectedModelNode as XmlElementTreeNode;

    void InsertElement(XmlElement element, InsertionMode mode)
    {
        var selected = SelectedModelNode;
        if (selected?.Parent is XmlElementTreeNode parent)
        {
            var newNode = new XmlElementTreeNode(element);
            var index = parent.Nodes.IndexOf(selected);
            if (mode == InsertionMode.After) index++;
            newNode.Insert(index, parent);
        }
    }

    void InsertTextNode(XmlText textNode, InsertionMode mode)
    {
        var selected = SelectedModelNode;
        if (selected?.Parent is XmlElementTreeNode parent)
        {
            var newNode = new XmlTextTreeNode(textNode);
            var index = parent.Nodes.IndexOf(selected);
            if (mode == InsertionMode.After) index++;
            newNode.Insert(index, parent);
        }
    }

    void InsertComment(XmlComment comment, InsertionMode mode)
    {
        var selected = SelectedModelNode;
        if (selected != null)
        {
            var newNode = new XmlCommentTreeNode(comment);
            int index;
            if (selected.Parent != null)
                index = selected.Parent.Nodes.IndexOf(selected);
            else
                index = _rootNode.Children.Count;
            if (mode == InsertionMode.After) index++;
            if (selected.Parent != null)
                newNode.Insert(index, selected.Parent);
            else
                _rootNode.Children.Insert(index, BuildTreeViewNode(newNode));
        }
    }

    XmlElementTreeNode FindElement(XmlElement element)
    {
        var selected = SelectedElementNode;
        if (selected?.XmlElement == element) return selected;
        return FindElement(element, _rootNode.Children.Select(c => c.Content as XmlTreeNode).Where(x => x != null));
    }

    static XmlElementTreeNode FindElement(XmlElement element, IEnumerable<XmlTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node is XmlElementTreeNode etn)
            {
                if (etn.XmlElement == element) return etn;
                var found = FindElement(element, etn.Nodes);
                if (found != null) return found;
            }
        }
        return null;
    }

    XmlTextTreeNode FindTextNode(XmlText textNode)
    {
        var selected = SelectedNode as XmlTextTreeNode;
        if (selected?.XmlText == textNode) return selected;
        return FindTextNode(textNode, _rootNode.Children.Select(c => c.Content as XmlTreeNode).Where(x => x != null));
    }

    static XmlTextTreeNode FindTextNode(XmlText textNode, IEnumerable<XmlTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node is XmlTextTreeNode ttn)
            {
                if (ttn.XmlText == textNode) return ttn;
            }
            else
            {
                var found = FindTextNode(textNode, node.Nodes);
                if (found != null) return found;
            }
        }
        return null;
    }

    XmlCommentTreeNode FindComment(XmlComment comment)
    {
        var selected = SelectedNode as XmlCommentTreeNode;
        if (selected?.XmlComment == comment) return selected;
        return FindComment(comment, _rootNode.Children.Select(c => c.Content as XmlTreeNode).Where(x => x != null));
    }

    static XmlCommentTreeNode FindComment(XmlComment comment, IEnumerable<XmlTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node is XmlCommentTreeNode ctn)
            {
                if (ctn.XmlComment == comment) return ctn;
            }
            else
            {
                var found = FindComment(comment, node.Nodes);
                if (found != null) return found;
            }
        }
        return null;
    }

    void ShowCut(XmlNode node, bool showGhost)
    {
        if (node is XmlElement elem)
            FindElement(elem)!.ShowGhostImage = showGhost;
        else if (node is XmlText text)
            FindTextNode(text)!.ShowGhostImage = showGhost;
        else if (node is XmlComment comment)
            FindComment(comment)!.ShowGhostImage = showGhost;
    }
}
