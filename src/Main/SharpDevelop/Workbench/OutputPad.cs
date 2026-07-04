using System;
using System.Linq;
using ICSharpCode.AvalonEdit;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Gui;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using UnoDevelop.Services;
using UnoEdit.Skia.Desktop.Controls;
using WpfToolBar = System.Windows.Controls.ToolBar;
using WpfToggleButton = System.Windows.Controls.Primitives.ToggleButton;

namespace UnoDevelop.Workbench;

// Faithful port of SharpDevelop's CompilerMessageView (the 'Output' pad):
//   - a category selector (Build / Run / Debug / ...) in a toolbar
//   - only the selected category's text is shown; appending to a category selects it
//   - Clear button, Word-wrap toggle, Copy / Select All
//   - clickable file references that jump to source
public sealed class OutputPad : UserControl
{
    private const string WordWrapProperty = "OutputPad.WordWrap";

    private readonly UnoOutputPadService _service;
    private readonly ComboBox _categoryCombo;
    private readonly WpfToggleButton _wordWrapButton;
    private MessageViewCategory? _displayed;
    private bool _suppressComboEvent;
    private string? _selectionBeforeContextMenu;
    // Number of chars of _displayed.Text already mirrored into the editor. Acts as a
    // watermark so a full-text snapshot (category switch) and a queued incremental
    // append of the same delta cannot double up.
    private int _renderedLength;

    // Raised when the user clicks a recognized file reference in the output.
    public event Action<string, int, int>? LinkActivated;

    public OutputPad()
        : this(SD.GetService<ICSharpCode.SharpDevelop.Workbench.IOutputPad>() as UnoOutputPadService
            ?? throw new InvalidOperationException("UnoOutputPadService is not registered."))
    {
    }

    public OutputPad(UnoOutputPadService service)
    {
        _service = service;

        Editor = new TextEditor
        {
            IsReadOnly = true,
            EditorFontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            EditorFontSize = 13d,
            Theme = TextEditorTheme.Light,
            // Match SharpDevelop's Output pad: a bare editor with no left margins, so text
            // sits flush against the left edge (no breakpoint icon bar, folding, or line numbers).
            ShowLineNumbers = false,
            ShowBreakpointMargin = false,
            ShowFoldMargin = false,
            // UnoEdit's wrapped selection geometry is not fully aligned yet.
            // Keep Output on the unwrapped path so selection/caret overlays match text.
            WordWrap = false,
        };

        _categoryCombo = new ComboBox { MinWidth = 120, Margin = new Thickness(4, 4, 8, 4) };
        _categoryCombo.SelectionChanged += OnCategoryComboChanged;

        var clearButton = new Button { Content = ToolbarVisuals.CreateToolbarIcon("ClearWindowContent_16x"), Margin = new Thickness(0, 4, 4, 4) };
        ToolbarVisuals.ApplyFlatToolbarChrome(clearButton);
        ToolbarVisuals.WireDisabledWash(clearButton);
        ToolTipService.SetToolTip(clearButton, "Clear All");
        clearButton.Click += (_, _) => _displayed?.ClearText();

        _wordWrapButton = new WpfToggleButton
        {
            Content = ToolbarVisuals.CreateToolbarIcon("WordWrap_16x"),
            Margin = new Thickness(0, 4, 4, 4),
            IsChecked = Editor.WordWrap,
        };
        ToolbarVisuals.ApplyFlatToolbarChrome(_wordWrapButton);
        ToolbarVisuals.WireDisabledWash(_wordWrapButton);
        ToolTipService.SetToolTip(_wordWrapButton, "Toggle Word Wrap");
        _wordWrapButton.Checked += (_, _) => SetWordWrap(true);
        _wordWrapButton.Unchecked += (_, _) => SetWordWrap(false);

        var toolbar = new WpfToolBar();
        toolbar.Items.Add(_categoryCombo);
        toolbar.Items.Add(clearButton);
        toolbar.Items.Add(_wordWrapButton);

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(toolbar, 0);
        Grid.SetRow(Editor, 1);
        grid.Children.Add(toolbar);
        grid.Children.Add(Editor);
        Content = grid;

        BuildContextMenu();
        RegisterLinkSupport();
        RefreshCategories();
        SelectCategory(_service.BuildMessageViewCategory);

        _service.CategoryAdded += OnCategoryAdded;
        _service.TextAppended += OnTextAppended;
        _service.TextSet += OnTextSet;
        _service.CurrentCategoryChanged += OnCurrentCategoryChanged;
    }

    public TextEditor Editor { get; }

    // ── Category display (mirrors CompilerMessageView.SelectedCategoryIndex) ──────

    private void RefreshCategories()
    {
        _suppressComboEvent = true;
        var selected = _displayed;
        _categoryCombo.Items.Clear();
        foreach (var cat in _service.Categories)
            _categoryCombo.Items.Add(StringParser.Parse(cat.DisplayCategory));
        var index = selected is null ? 0 : Math.Max(0, _service.Categories.ToList().IndexOf(selected));
        if (_categoryCombo.Items.Count > 0)
            _categoryCombo.SelectedIndex = index;
        _suppressComboEvent = false;
    }

    private void SelectCategory(MessageViewCategory category)
    {
        if (ReferenceEquals(_displayed, category))
            return;
        _displayed = category;
        var snapshot = category.Text;
        Editor.Text = snapshot;
        _renderedLength = snapshot.Length;
        Editor.ScrollToEnd();

        var index = _service.Categories.ToList().IndexOf(category);
        if (index >= 0 && _categoryCombo.SelectedIndex != index)
        {
            _suppressComboEvent = true;
            _categoryCombo.SelectedIndex = index;
            _suppressComboEvent = false;
        }
    }

    private void OnCategoryComboChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressComboEvent)
            return;
        var index = _categoryCombo.SelectedIndex;
        var cats = _service.Categories;
        if (index >= 0 && index < cats.Count)
            SelectCategory(cats[index]);
    }

    // ── Service events (marshalled to the UI thread) ─────────────────────────────

    private void OnCategoryAdded(MessageViewCategory _)
        => Enqueue(RefreshCategories);

    private void OnCurrentCategoryChanged(MessageViewCategory category)
        => Enqueue(() => SelectCategory(category));

    private void OnTextAppended(MessageViewCategory category, string _)
        => Enqueue(() =>
        {
            // SharpDevelop switches the view to the category that just received text.
            if (!ReferenceEquals(_displayed, category))
            {
                SelectCategory(category);
                return;
            }

            // Render only the tail beyond what we've already shown. If a preceding
            // category-switch snapshot already captured this delta, the watermark
            // equals the buffer length and nothing is appended again.
            var full = category.Text;
            if (_renderedLength < full.Length)
            {
                Editor.AppendText(full.Substring(_renderedLength));
                _renderedLength = full.Length;
                Editor.ScrollToEnd();
            }
            else if (_renderedLength > full.Length)
            {
                // Buffer shrank (truncation cap) — resync fully.
                Editor.Text = full;
                _renderedLength = full.Length;
                Editor.ScrollToEnd();
            }
        });

    private void OnTextSet(MessageViewCategory category, string text)
        => Enqueue(() =>
        {
            if (!ReferenceEquals(_displayed, category))
            {
                SelectCategory(category);
                return;
            }
            Editor.Text = text;
            _renderedLength = text.Length;
            Editor.ScrollToEnd();
        });

    private void Enqueue(Action action)
        => DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () => action());

    // ── Word wrap (persisted, mirrors ToggleMessageViewWordWrap) ─────────────────

    private void SetWordWrap(bool value)
    {
        Editor.WordWrap = value;
        SD.PropertyService.Set(WordWrapProperty, value);
    }

    // ── Copy / Select All (mirrors CompilerMessageView IClipboardHandler) ────────

    private void BuildContextMenu()
    {
        var menu = new MenuFlyout();
        menu.Opening += (_, _) => CaptureSelectionForContextMenu();
        var copy = new MenuFlyoutItem { Text = "Copy" };
        copy.Click += (_, _) => CopyFromSelectionOrFallback();
        var selectAll = new MenuFlyoutItem { Text = "Select All" };
        selectAll.Click += (_, _) => Editor.SelectAll();
        menu.Items.Add(copy);
        menu.Items.Add(selectAll);
        Editor.RightTapped += (_, _) => CaptureSelectionForContextMenu();
        Editor.ContextFlyout = menu;
    }

    private void CaptureSelectionForContextMenu()
    {
        if (Editor.SelectionLength > 0)
            _selectionBeforeContextMenu = Editor.SelectedText;
    }

    private void CopyFromSelectionOrFallback()
    {
        if (Editor.SelectionLength > 0)
        {
            Editor.Copy();
            return;
        }

        if (string.IsNullOrEmpty(_selectionBeforeContextMenu))
            return;

        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(_selectionBeforeContextMenu);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
    }

    // ── Clickable file references ─────────────────────────────────────────────────

    private void RegisterLinkSupport()
    {
        // Underlined-link rendering via the AvalonEdit element-generator pipeline.
        try { MessageViewLinkElementGenerator.Register(Editor.TextArea.TextView); }
        catch { /* editor pipeline not ready — navigation still works via tap below */ }

        // Click handling: the Skia editor does not yet dispatch clicks to link
        // elements, so resolve the clicked line ourselves and navigate.
        Editor.Tapped += OnEditorTapped;
    }

    private void OnEditorTapped(object sender, TappedRoutedEventArgs e)
    {
        var pos = Editor.GetPositionFromPoint(e.GetPosition(Editor));
        if (pos is null || Editor.Document is null)
            return;

        var lineNumber = pos.Value.Line;
        if (lineNumber < 1 || lineNumber > Editor.Document.LineCount)
            return;

        var docLine = Editor.Document.GetLineByNumber(lineNumber);
        var lineText = Editor.Document.GetText(docLine.Offset, docLine.Length);
        if (MessageViewLinkElementGenerator.TryParse(lineText, out var file, out var line, out var column))
        {
            LinkActivated?.Invoke(file, line, column);
            e.Handled = true;
        }
    }

    // ── Legacy helpers retained for existing callers ─────────────────────────────

    public void Clear() => _displayed?.ClearText();
}
