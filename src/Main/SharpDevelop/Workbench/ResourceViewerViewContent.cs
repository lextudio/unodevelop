using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Workbench;
using LeXtudio.OpenDevelop.ResourceFiles;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace UnoDevelop.Workbench;

internal sealed class ResourceViewerViewContent : IViewContent
{
    private readonly List<OpenedFile> _files = new();
    private readonly ObservableCollection<ResourceEntry> _entries = new();
    private readonly ObservableCollection<ResourceEntry> _filteredEntries = new();
    private readonly Grid _control;
    private readonly TextBox _filterBox;
    private readonly TextBlock _header;
    private readonly bool _canEdit;
    private bool _isDirty;

    public ResourceViewerViewContent(string filePath)
    {
        FilePath = filePath;
        TabPageText = Path.GetFileName(filePath);
        TitleName = TabPageText;
        InfoTip = filePath;
        _canEdit = Path.GetExtension(filePath).Equals(".resx", StringComparison.OrdinalIgnoreCase);
        _files.Add(new ResourceOpenedFile(filePath));

        LoadEntries(ResourceFileReader.Read(filePath));

        _filterBox = new TextBox
        {
            PlaceholderText = "Filter resources",
            Margin = new Thickness(8, 8, 8, 4)
        };
        _filterBox.TextChanged += (_, _) => ApplyFilter();

        var toolbar = CreateToolbar();
        _header = new TextBlock
        {
            Text = CreateHeaderText(),
            Margin = new Thickness(8, 0, 8, 4),
            Style = Application.Current.Resources["BodyTextBlockStyle"] as Style
        };

        var list = new ListView
        {
            ItemsSource = _filteredEntries,
            SelectionMode = ListViewSelectionMode.Extended,
            ItemTemplate = _canEdit ? CreateEditableTemplate() : CreateReadOnlyTemplate()
        };
        list.DoubleTapped += (_, _) => CopySelectedEntries(list);
        list.KeyDown += (_, args) =>
        {
            if (args.Key == VirtualKey.Delete && _canEdit)
            {
                DeleteSelectedEntries(list);
                args.Handled = true;
            }
        };

        var menu = new MenuFlyout();
        var copy = new MenuFlyoutItem { Text = "Copy" };
        copy.Click += (_, _) => CopySelectedEntries(list);
        menu.Items.Add(copy);
        if (_canEdit)
        {
            var delete = new MenuFlyoutItem { Text = "Delete" };
            delete.Click += (_, _) => DeleteSelectedEntries(list);
            menu.Items.Add(delete);
        }
        list.ContextFlyout = menu;

        _control = new Grid();
        _control.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _control.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _control.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var top = new StackPanel { Orientation = Orientation.Vertical };
        top.Children.Add(toolbar);
        top.Children.Add(_filterBox);
        top.Children.Add(_header);
        Grid.SetRow(top, 0);
        Grid.SetRow(list, 2);
        _control.Children.Add(top);
        _control.Children.Add(list);
        _control.KeyDown += (_, args) =>
        {
            if (args.Key == VirtualKey.S && IsControlPressed())
            {
                SaveToFile(notifyOnSuccess: true);
                args.Handled = true;
            }
        };
    }

    public string FilePath { get; }

    /// <summary>Read-only snapshot of loaded entries - exposed for DevFlow/integration-test inspection.</summary>
    public IReadOnlyList<ResourceEntry> Entries => _entries;

    public object? Control => _control;

    public object? InitiallyFocusedControl => _filterBox;

    public IWorkbenchWindow? WorkbenchWindow { get; set; }

    public event EventHandler? TabPageTextChanged;

    public string TabPageText { get; }

    public string TitleName { get; }

    public event EventHandler? TitleNameChanged;

    public string InfoTip { get; }

    public event EventHandler? InfoTipChanged;

    public IList<OpenedFile> Files => _files;

    public OpenedFile? PrimaryFile => _files[0];

    public FileName? PrimaryFileName => PrimaryFile?.FileName;

    public bool IsDisposed { get; private set; }

    public event EventHandler? Disposed;

    public bool IsReadOnly => !_canEdit;

    public bool IsViewOnly => !_canEdit;

    public bool CloseWithSolution => true;

    public ICollection<IViewContent> SecondaryViewContents => Array.Empty<IViewContent>();

    public bool IsDirty => _isDirty;

    public event EventHandler? IsDirtyChanged;

    public void Save(OpenedFile file, Stream stream)
    {
        if (!_canEdit)
        {
            return;
        }

        // Deliberately ignore the passed-in stream and save in-place via FilePath directly:
        // ResourceFileReader.SaveResX(fileName, entries, stream) re-reads fileName from disk to
        // preserve existing .resx headers/whitespace, so if the caller's stream already truncated
        // that same file (e.g. File.Create(FilePath) before calling this), the reload sees an
        // empty file and the save corrupts it to zero bytes. The fileName-only overload owns its
        // own read-then-write ordering and is safe for this in-place case.
        ResourceFileReader.SaveResX(FilePath, _entries);
        MarkClean();
    }

    public void Load(OpenedFile file, Stream stream)
    {
    }

    public INavigationPoint BuildNavPoint() => new ResourceNavigationPoint(FilePath);

    public bool SupportsSwitchFromThisWithoutSaveLoad(OpenedFile file, IViewContent newView) => false;

    public bool SupportsSwitchToThisWithoutSaveLoad(OpenedFile file, IViewContent oldView) => false;

    public void SwitchFromThisWithoutSaveLoad(OpenedFile file, IViewContent newView)
    {
    }

    public void SwitchToThisWithoutSaveLoad(OpenedFile file, IViewContent oldView)
    {
    }

    public object? GetService(Type serviceType) => null;

    public void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }

        IsDisposed = true;
        if (PrimaryFile is ResourceOpenedFile openedFile)
        {
            openedFile.NotifyClosed();
        }

        Disposed?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyFilter()
    {
        var filter = _filterBox.Text;
        _filteredEntries.Clear();
        foreach (var entry in _entries)
        {
            if (string.IsNullOrWhiteSpace(filter)
                || entry.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || entry.Type.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || entry.Value.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || entry.Comment?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true)
            {
                _filteredEntries.Add(entry);
            }
        }
    }

    private void LoadEntries(IEnumerable<ResourceEntry> entries)
    {
        foreach (var entry in entries)
        {
            entry.PropertyChanged += (_, _) =>
            {
                if (_canEdit)
                {
                    MarkDirty();
                    ApplyFilter();
                    UpdateHeader();
                }
            };
            _entries.Add(entry);
            _filteredEntries.Add(entry);
        }
    }

    private FrameworkElement CreateToolbar()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(8, 8, 8, 0),
            Visibility = _canEdit ? Visibility.Visible : Visibility.Collapsed
        };

        var addString = new Button { Content = "Add string" };
        addString.Click += (_, _) => AddEntry("string", string.Empty);
        var addBoolean = new Button { Content = "Add bool" };
        addBoolean.Click += (_, _) => AddEntry("System.Boolean", "False");
        var save = new Button { Content = "Save" };
        save.Click += (_, _) => SaveToFile(notifyOnSuccess: true);

        panel.Children.Add(addString);
        panel.Children.Add(addBoolean);
        panel.Children.Add(save);
        return panel;
    }

    private void AddEntry(string type, string value)
    {
        var baseName = type == "string" ? "NewString" : "NewBoolean";
        var index = 1;
        var name = baseName + index;
        while (_entries.Any(entry => entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            index++;
            name = baseName + index;
        }

        var entry = new ResourceEntry(name, type, value, null, value.Length, isEditable: true);
        entry.PropertyChanged += (_, _) =>
        {
            MarkDirty();
            ApplyFilter();
            UpdateHeader();
        };
        _entries.Add(entry);
        ApplyFilter();
        MarkDirty();
        UpdateHeader();
    }

    private void DeleteSelectedEntries(ListView list)
    {
        var selected = list.SelectedItems.OfType<ResourceEntry>().ToArray();
        if (selected.Length == 0 && list.SelectedItem is ResourceEntry single)
        {
            selected = new[] { single };
        }

        foreach (var entry in selected)
        {
            _entries.Remove(entry);
        }

        ApplyFilter();
        MarkDirty();
        UpdateHeader();
    }

    private void SaveToFile(bool notifyOnSuccess)
    {
        try
        {
            // Save(OpenedFile, Stream) ignores its stream parameter and saves via FilePath
            // directly (see its own comment) - no stream to open/pass here.
            Save(PrimaryFile!, Stream.Null);
            if (notifyOnSuccess)
            {
                ServiceSingleton.GetRequiredService<IMessageService>().ShowMessage($"Saved: {FilePath}", "UnoDevelop");
            }
        }
        catch (Exception ex)
        {
            ServiceSingleton.GetRequiredService<IMessageService>().ShowException(ex, $"Failed to save {FilePath}");
        }
    }

    private void MarkDirty()
    {
        if (_isDirty)
        {
            return;
        }

        _isDirty = true;
        IsDirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    private void MarkClean()
    {
        if (!_isDirty)
        {
            return;
        }

        _isDirty = false;
        IsDirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    private string CreateHeaderText()
        => _canEdit
            ? $"{_entries.Count} resource(s), editable .resx"
            : $"{_entries.Count} resource(s), read-only";

    private void UpdateHeader()
    {
        _header.Text = CreateHeaderText();
    }

    private static DataTemplate CreateReadOnlyTemplate()
    {
        return new DataTemplate(() =>
        {
            var root = new Grid { Padding = new Thickness(8, 6, 8, 6), ColumnSpacing = 12 };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var name = CreateCell("Name", true);
            var type = CreateCell("Type", false);
            // DisplaySummary shows "Bitmap (N bytes)" etc. for image/binary resx entries instead
            // of a raw unreadable base64 blob; falls back to the plain Value for text entries.
            var value = CreateCell("DisplaySummary", false);

            Grid.SetColumn(name, 0);
            Grid.SetColumn(type, 1);
            Grid.SetColumn(value, 2);
            root.Children.Add(name);
            root.Children.Add(type);
            root.Children.Add(value);
            return root;
        });
    }

    private static DataTemplate CreateEditableTemplate()
    {
        return new DataTemplate(() =>
        {
            var root = new Grid { Padding = new Thickness(8, 4, 8, 4), ColumnSpacing = 8 };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });

            var name = CreateEditor("Name");
            var type = CreateEditor("Type");
            // Per-row: string/boolean/metadata entries (IsEditable) get a live TextBox on Value;
            // Bitmap/Icon/Cursor/Binary entries (not editable) get a read-only byte-count summary
            // instead - a giant base64 blob in an inline TextBox is neither readable nor safely
            // hand-editable.
            var valueEditor = CreateEditor("Value");
            valueEditor.SetBinding(FrameworkElement.VisibilityProperty, new Binding
            {
                Path = new PropertyPath("IsEditable"),
                Converter = BoolToVisibilityConverter
            });
            var valueSummary = CreateCell("DisplaySummary", false);
            valueSummary.SetBinding(FrameworkElement.VisibilityProperty, new Binding
            {
                Path = new PropertyPath("IsEditable"),
                Converter = BoolToVisibilityConverter,
                ConverterParameter = "invert"
            });
            var valueCell = new Grid();
            valueCell.Children.Add(valueEditor);
            valueCell.Children.Add(valueSummary);
            var comment = CreateEditor("Comment");

            Grid.SetColumn(name, 0);
            Grid.SetColumn(type, 1);
            Grid.SetColumn(valueCell, 2);
            Grid.SetColumn(comment, 3);
            root.Children.Add(name);
            root.Children.Add(type);
            root.Children.Add(valueCell);
            root.Children.Add(comment);
            return root;
        });
    }

    private static readonly IValueConverter BoolToVisibilityConverter = new EditableToVisibilityConverter();

    private sealed class EditableToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            var isEditable = value is bool b && b;
            var invert = string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase);
            var show = invert ? !isEditable : isEditable;
            return show ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotSupportedException();
    }

    private static FrameworkElement CreateEditor(string path)
    {
        var box = new TextBox
        {
            MinHeight = 30,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap
        };
        box.SetBinding(TextBox.TextProperty, new Binding
        {
            Path = new PropertyPath(path),
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });
        return box;
    }

    private static FrameworkElement CreateCell(string path, bool strong)
    {
        var text = new TextBlock
        {
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = strong ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal
        };
        text.SetBinding(TextBlock.TextProperty, new Binding { Path = new PropertyPath(path) });
        return text;
    }

    private static void CopySelectedEntries(ListView list)
    {
        var entries = list.SelectedItems.OfType<ResourceEntry>().ToArray();
        if (entries.Length == 0 && list.SelectedItem is ResourceEntry single)
        {
            entries = new[] { single };
        }

        if (entries.Length == 0)
        {
            return;
        }

        var text = string.Join(Environment.NewLine, entries.Select(entry =>
            $"{entry.Name}\t{entry.Type}\t{entry.Value}"));
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    private static bool IsControlPressed()
    {
        var state = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
        return (state & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
    }

    private sealed class ResourceOpenedFile : SimpleOpenedFile
    {
        public ResourceOpenedFile(string filePath)
            : base(filePath)
        {
        }
    }

    private sealed class ResourceNavigationPoint : INavigationPoint
    {
        public ResourceNavigationPoint(string fileName)
        {
            FileName = fileName;
        }

        public string FileName { get; private set; }

        public string Description => FileName;

        public string FullDescription => FileName;

        public string ToolTip => FileName;

        public object NavigationData => FileName;

        public int Index => 0;

        public void JumpTo()
        {
        }

        public void FileNameChanged(string newName)
        {
            FileName = newName;
        }

        public void ContentChanging(object sender, EventArgs e)
        {
        }

        public int CompareTo(object? obj)
        {
            return obj is ResourceNavigationPoint other
                ? string.Compare(FileName, other.FileName, StringComparison.OrdinalIgnoreCase)
                : 0;
        }
    }
}
