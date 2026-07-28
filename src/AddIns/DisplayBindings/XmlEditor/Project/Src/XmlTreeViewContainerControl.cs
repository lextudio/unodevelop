using System;
using System.Linq;
using System.Xml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Windows.UI.Text;
using Windows.System;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Gui;
using ICSharpCode.SharpDevelop.WinForms;

namespace ICSharpCode.XmlEditor;

public class XmlTreeViewContainerControl : UserControl, IXmlTreeView, IOwnerState, IClipboardHandler
{
    readonly XmlTreeEditor _editor;

    readonly XmlTreeViewControl _tree;
    readonly ListView _attributesList;
    readonly TextBox _errorMessage;
    readonly TextBox _textBox;
    readonly Border _attributesBorder;
    readonly StackPanel _rightPanel;

    bool _dirty;
    bool _errorVisible;
    bool _attributesVisible = true;

    [Flags]
    enum XmlTreeViewContainerControlStates
    {
        None = 0,
        ElementSelected = 1,
        RootElementSelected = 2,
        AttributeSelected = 4,
        TextNodeSelected = 8,
        CommentSelected = 16,
    }

    public event EventHandler DirtyChanged;

    public XmlTreeViewContainerControl()
        : this(new XmlSchemaCompletionCollection(), null)
    {
    }

    public XmlTreeViewContainerControl(XmlSchemaCompletionCollection schemas, XmlSchemaCompletion defaultSchema)
    {
        _editor = new XmlTreeEditor(this, schemas, defaultSchema);

        _tree = new XmlTreeViewControl();
        _tree.TreeViewKeyPressed += OnTreeViewKeyPressed;

        _attributesList = new ListView
        {
            Header = "Attributes",
            SelectionMode = ListViewSelectionMode.Single,
            ItemTemplate = CreateAttributeTemplate(),
            Visibility = Visibility.Visible,
        };

        _errorMessage = new TextBox
        {
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            Visibility = Visibility.Collapsed,
        };

        _textBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };
        _textBox.TextChanged += OnTextBoxTextChanged;

        _attributesBorder = new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush,
            Padding = new Thickness(4),
            Child = _attributesList,
        };

        _rightPanel = new StackPanel { Spacing = 4 };
        _rightPanel.Children.Add(_attributesBorder);
        _rightPanel.Children.Add(_errorMessage);
        _rightPanel.Children.Add(_textBox);

        var separator = new Border
        {
            Width = 3,
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });

        Grid.SetColumn(_tree, 0);
        Grid.SetColumn(separator, 1);
        Grid.SetColumn(_rightPanel, 2);

        grid.Children.Add(_tree);
        grid.Children.Add(separator);
        grid.Children.Add(_rightPanel);

        Content = grid;
        IsAttributesGridVisible = true;
    }

    static DataTemplate CreateAttributeTemplate()
    {
        return new DataTemplate(() =>
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var nameBlock = new TextBlock { FontWeight = new FontWeight(600), Margin = new Thickness(0, 0, 8, 0) };
            nameBlock.SetBinding(TextBlock.TextProperty, new Binding
            {
                Path = new PropertyPath("Name"),
                Mode = BindingMode.OneWay,
            });
            Grid.SetColumn(nameBlock, 0);
            grid.Children.Add(nameBlock);

            var valueBlock = new TextBlock { TextWrapping = TextWrapping.Wrap };
            valueBlock.SetBinding(TextBlock.TextProperty, new Binding
            {
                Path = new PropertyPath("Value"),
                Mode = BindingMode.OneWay,
            });
            Grid.SetColumn(valueBlock, 1);
            grid.Children.Add(valueBlock);

            return grid;
        });
    }

    public Enum InternalState
    {
        get
        {
            var state = XmlTreeViewContainerControlStates.None;
            if (SelectedElement != null)
            {
                state |= XmlTreeViewContainerControlStates.ElementSelected;
                if (SelectedElement == Document?.DocumentElement)
                    state |= XmlTreeViewContainerControlStates.RootElementSelected;
            }
            if (SelectedAttribute != null)
                state |= XmlTreeViewContainerControlStates.AttributeSelected;
            if (SelectedTextNode != null)
                state = XmlTreeViewContainerControlStates.TextNodeSelected;
            if (SelectedComment != null)
                state = XmlTreeViewContainerControlStates.CommentSelected;
            return state;
        }
    }

    public ListView AttributesGrid => _attributesList;

    public bool IsDirty
    {
        get => _dirty;
        set
        {
            var prev = _dirty;
            _dirty = value;
            OnXmlChanged(prev);
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage.Text;
        set => _errorMessage.Text = value;
    }

    public bool IsErrorMessageTextBoxVisible
    {
        get => _errorVisible;
        set
        {
            _errorVisible = value;
            _errorMessage.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            if (value)
            {
                IsAttributesGridVisible = false;
                IsTextBoxVisible = false;
            }
        }
    }

    public XmlTreeViewControl TreeView => _tree;

    public void ShowXmlIsNotWellFormedMessage(XmlException ex)
        => ShowErrorMessage(ex.Message);

    public void ShowErrorMessage(string message)
    {
        _tree.Nodes.Clear();
        ErrorMessage = message;
        IsErrorMessageTextBoxVisible = true;
    }

    public void LoadXml(string xml)
    {
        _textBox.Text = "";
        IsAttributesGridVisible = true;
        ClearAttributes();
        _editor.LoadXml(xml);
        ExpandRootDocumentElementNode();
    }

    void ExpandRootDocumentElementNode()
    {
        if (_tree.Nodes.Count > 0)
            _tree.Nodes[0].Expand();
    }

    public void ExpandAll() => ExpandAll(_tree.SelectedNode);
    public void CollapseAll() => CollapseAll(_tree.SelectedNode);

    static void ExpandAll(XmlTreeNode node)
    {
        if (node == null) return;
        node.IsExpanded = true;
        foreach (var child in node.Nodes)
            ExpandAll(child);
    }

    static void CollapseAll(XmlTreeNode node)
    {
        if (node == null) return;
        node.IsExpanded = false;
    }

    public XmlDocument Document
    {
        get => _editor.Document;
        set => _tree.Document = value;
    }

    public void ShowAttributes(XmlAttributeCollection attributes)
    {
        IsAttributesGridVisible = true;
        if (attributes != null)
        {
            var items = attributes.Cast<XmlAttribute>()
                .Select(a => new AttributeItem(a.Name, a.Value))
                .ToList();
            _attributesList.ItemsSource = items;
        }
        else
        {
            _attributesList.ItemsSource = null;
        }
    }

    public void ClearAttributes()
    {
        _attributesList.ItemsSource = null;
    }

    public void ShowTextContent(string text)
    {
        IsTextBoxVisible = true;
        _textBox.Text = text;
    }

    public string TextContent
    {
        get => _textBox.Text.Replace("\n", "\r\n");
        set => _textBox.Text = value;
    }

    public XmlNode SelectedNode
    {
        get
        {
            var elem = SelectedElement;
            if (elem != null) return elem;
            var text = SelectedTextNode;
            if (text != null) return text;
            return SelectedComment;
        }
    }

    public XmlElement SelectedElement => _tree.SelectedElement;
    public XmlText SelectedTextNode => _tree.SelectedTextNode;
    public XmlComment SelectedComment => _tree.SelectedComment;

    public string SelectedAttribute
    {
        get
        {
            if (IsAttributesGridVisible && _attributesList.SelectedItem is AttributeItem item)
                return item.Name;
            return null;
        }
    }

    public void AddAttribute() => _editor.AddAttribute();

    public string[] SelectNewAttributes(string[] attributes)
    {
        using var dialog = CreateAddAttributeDialog(attributes);
        if (dialog.ShowDialog() == AddXmlNodeDialogResult.OK)
            return dialog.GetNames();
        return [];
    }

    public void RemoveAttribute() => _editor.RemoveAttribute();

    public string[] SelectNewElements(string[] elements)
    {
        using var dialog = CreateAddElementDialog(elements);
        if (dialog.ShowDialog() == AddXmlNodeDialogResult.OK)
            return dialog.GetNames();
        return [];
    }

    public void AppendChildElement(XmlElement element) => _tree.AppendChildElement(element);

    public void AddChildElement() => _editor.AppendChildElement();

    public void InsertElementBefore() => _editor.InsertElementBefore();
    public void InsertElementBefore(XmlElement element) => _tree.InsertElementBefore(element);

    public void InsertElementAfter() => _editor.InsertElementAfter();
    public void InsertElementAfter(XmlElement element) => _tree.InsertElementAfter(element);

    public void RemoveElement(XmlElement element) => _tree.RemoveElement(element);

    public void AppendChildTextNode(XmlText textNode) => _tree.AppendChildTextNode(textNode);
    public void AppendChildTextNode() => _editor.AppendChildTextNode();

    public void InsertTextNodeBefore() => _editor.InsertTextNodeBefore();
    public void InsertTextNodeBefore(XmlText textNode) => _tree.InsertTextNodeBefore(textNode);

    public void InsertTextNodeAfter() => _editor.InsertTextNodeAfter();
    public void InsertTextNodeAfter(XmlText textNode) => _tree.InsertTextNodeAfter(textNode);

    public void RemoveTextNode(XmlText textNode) => _tree.RemoveTextNode(textNode);

    public void UpdateTextNode(XmlText textNode) => _tree.UpdateTextNode(textNode);
    public void UpdateComment(XmlComment comment) => _tree.UpdateComment(comment);

    public void AppendChildComment(XmlComment comment) => _tree.AppendChildComment(comment);
    public void AppendChildComment() => _editor.AppendChildComment();

    public void RemoveComment(XmlComment comment) => _tree.RemoveComment(comment);

    public void InsertCommentBefore(XmlComment comment) => _tree.InsertCommentBefore(comment);
    public void InsertCommentBefore() => _editor.InsertCommentBefore();

    public void InsertCommentAfter(XmlComment comment) => _tree.InsertCommentAfter(comment);
    public void InsertCommentAfter() => _editor.InsertCommentAfter();

    public void ShowCut(XmlNode node) => _tree.ShowCut(node);
    public void HideCut(XmlNode node) => _tree.HideCut(node);

    public bool EnableCut => _editor.IsCutEnabled;
    public bool EnableCopy => _editor.IsCopyEnabled;
    public bool EnablePaste => _editor.IsPasteEnabled;
    public bool EnableDelete => _editor.IsDeleteEnabled;
    public bool EnableSelectAll => false;

    public void Cut() => _editor.Cut();
    public void Copy() => _editor.Copy();
    public void Paste() => _editor.Paste();
    public void Delete() => _editor.Delete();
    public void SelectAll() { }

    protected virtual IAddXmlNodeDialog CreateAddElementDialog(string[] elementNames)
    {
        var dialog = new AddXmlNodeDialog(elementNames)
        {
            Title = StringParser.Parse("${res:ICSharpCode.XmlEditor.AddElementDialog.Title}"),
            CustomNameLabelText = StringParser.Parse("${res:ICSharpCode.XmlEditor.AddElementDialog.CustomElementLabel}"),
        };
        return dialog;
    }

    protected virtual IAddXmlNodeDialog CreateAddAttributeDialog(string[] attributeNames)
    {
        var dialog = new AddXmlNodeDialog(attributeNames)
        {
            Title = StringParser.Parse("${res:ICSharpCode.XmlEditor.AddAttributeDialog.Title}"),
            CustomNameLabelText = StringParser.Parse("${res:ICSharpCode.XmlEditor.AddAttributeDialog.CustomAttributeLabel}"),
        };
        return dialog;
    }

    protected void OnTreeViewKeyPressed(object source, XmlTreeViewKeyPressedEventArgs e)
    {
        if (e.KeyData == VirtualKey.Delete)
            Delete();
        else if (e.KeyData == VirtualKey.C && IsCtrlDown())
            Copy();
        else if (e.KeyData == VirtualKey.X && IsCtrlDown())
            Cut();
        else if (e.KeyData == VirtualKey.V && IsCtrlDown())
            Paste();
        else if (e.KeyData == VirtualKey.A && IsCtrlDown())
            SelectAll();
    }

    static bool IsCtrlDown() => Microsoft.UI.Xaml.Window.Current is not null;

    protected void OnTextBoxTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_editor != null)
        {
            var prev = _dirty;
            _editor.TextContentChanged();
            OnXmlChanged(prev);
        }
    }

    protected void OnAttributesGridSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
    }

    void OnXmlChanged(bool previousIsDirty)
    {
        if (previousIsDirty != _dirty)
            DirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    bool IsAttributesGridVisible
    {
        get => _attributesVisible;
        set
        {
            _attributesVisible = value;
            _attributesBorder.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            if (value)
            {
                IsTextBoxVisible = false;
                IsErrorMessageTextBoxVisible = false;
            }
        }
    }

    bool IsTextBoxVisible
    {
        set
        {
            _textBox.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            if (value)
            {
                IsAttributesGridVisible = false;
                IsErrorMessageTextBoxVisible = false;
            }
        }
    }

    sealed class AttributeItem(string name, string value)
    {
        public string Name { get; } = name;
        public string Value { get; } = value;
    }
}
