using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Workbench;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace UnoDevelop.AddIns.DisplayBindings.SettingsEditor;

public sealed class SettingsEditorViewContent : IViewContent
{
    private const string SettingsNamespace = "http://schemas.microsoft.com/VisualStudio/2004/01/settings";
    private readonly List<OpenedFile> _files = new();
    private readonly ObservableCollection<SettingEntry> _entries = new();
    private readonly Grid _control;
    private readonly TextBox _namespaceBox;
    private readonly TextBox _classNameBox;
    private readonly CheckBox _useMySettingsClassName;
    private readonly TextBlock _status;
    private bool _isDirty;

    public SettingsEditorViewContent(string filePath)
    {
        FilePath = filePath;
        TabPageText = Path.GetFileName(filePath);
        TitleName = TabPageText;
        InfoTip = filePath;
        _files.Add(new SettingsOpenedFile(filePath));

        var document = XDocument.Load(filePath);
        LoadDocument(document);

        _namespaceBox = new TextBox { Header = "Namespace", Text = GeneratedClassNamespace, Margin = new Thickness(8, 8, 4, 4) };
        _classNameBox = new TextBox { Header = "Class", Text = GeneratedClassName, Margin = new Thickness(4, 8, 4, 4) };
        _useMySettingsClassName = new CheckBox { Content = "Use My.Settings class name", IsChecked = UseMySettingsClassName, Margin = new Thickness(8, 34, 8, 4) };
        _namespaceBox.TextChanged += (_, _) => MarkDirty();
        _classNameBox.TextChanged += (_, _) => MarkDirty();
        _useMySettingsClassName.Checked += (_, _) => MarkDirty();
        _useMySettingsClassName.Unchecked += (_, _) => MarkDirty();

        var metadata = new Grid();
        metadata.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        metadata.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        metadata.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_namespaceBox, 0);
        Grid.SetColumn(_classNameBox, 1);
        Grid.SetColumn(_useMySettingsClassName, 2);
        metadata.Children.Add(_namespaceBox);
        metadata.Children.Add(_classNameBox);
        metadata.Children.Add(_useMySettingsClassName);

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(8, 4, 8, 4) };
        var addString = new Button { Content = "Add string" };
        addString.Click += (_, _) => AddEntry("System.String", "User");
        var addBool = new Button { Content = "Add bool" };
        addBool.Click += (_, _) => AddEntry("System.Boolean", "User");
        var addInt = new Button { Content = "Add int" };
        addInt.Click += (_, _) => AddEntry("System.Int32", "User");
        var save = new Button { Content = "Save" };
        save.Click += (_, _) => SaveToFile(notifyOnSuccess: true);
        toolbar.Children.Add(addString);
        toolbar.Children.Add(addBool);
        toolbar.Children.Add(addInt);
        toolbar.Children.Add(save);

        var list = new ListView
        {
            ItemsSource = _entries,
            SelectionMode = ListViewSelectionMode.Extended,
            ItemTemplate = CreateEntryTemplate(),
            Margin = new Thickness(8, 0, 8, 4)
        };
        list.KeyDown += (_, args) =>
        {
            if (args.Key == VirtualKey.Delete)
            {
                DeleteSelectedEntries(list);
                args.Handled = true;
            }
        };
        list.DoubleTapped += (_, _) => CopySelectedEntries(list);

        var menu = new MenuFlyout();
        var copy = new MenuFlyoutItem { Text = "Copy" };
        copy.Click += (_, _) => CopySelectedEntries(list);
        menu.Items.Add(copy);
        var delete = new MenuFlyoutItem { Text = "Delete" };
        delete.Click += (_, _) => DeleteSelectedEntries(list);
        menu.Items.Add(delete);
        list.ContextFlyout = menu;

        _status = new TextBlock { Text = CreateStatusText(), Margin = new Thickness(8, 0, 8, 8) };

        _control = new Grid();
        _control.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _control.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _control.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _control.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(metadata, 0);
        Grid.SetRow(toolbar, 1);
        Grid.SetRow(list, 2);
        Grid.SetRow(_status, 3);
        _control.Children.Add(metadata);
        _control.Children.Add(toolbar);
        _control.Children.Add(list);
        _control.Children.Add(_status);
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
    private string GeneratedClassNamespace { get; set; } = string.Empty;
    private string GeneratedClassName { get; set; } = string.Empty;
    private bool UseMySettingsClassName { get; set; }

    public object? Control => _control;
    public object? InitiallyFocusedControl => _control;
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
    public bool IsReadOnly => false;
    public bool IsViewOnly => false;
    public bool CloseWithSolution => true;
    public ICollection<IViewContent> SecondaryViewContents => Array.Empty<IViewContent>();
    public bool IsDirty => _isDirty;
    public event EventHandler? IsDirtyChanged;

    public void Save(OpenedFile file, Stream stream)
    {
        var document = CreateDocument();
        document.Save(stream);
        MarkClean();
    }

    public void Load(OpenedFile file, Stream stream)
    {
    }

    public INavigationPoint BuildNavPoint() => new SettingsNavigationPoint(FilePath);
    public bool SupportsSwitchFromThisWithoutSaveLoad(OpenedFile file, IViewContent newView) => false;
    public bool SupportsSwitchToThisWithoutSaveLoad(OpenedFile file, IViewContent oldView) => false;
    public void SwitchFromThisWithoutSaveLoad(OpenedFile file, IViewContent newView) { }
    public void SwitchToThisWithoutSaveLoad(OpenedFile file, IViewContent oldView) { }
    public object? GetService(Type serviceType) => null;

    public void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }

        IsDisposed = true;
        if (PrimaryFile is SettingsOpenedFile openedFile)
        {
            openedFile.NotifyClosed();
        }

        Disposed?.Invoke(this, EventArgs.Empty);
    }

    private void LoadDocument(XDocument document)
    {
        var root = document.Root ?? throw new FormatException("Not a settings file.");
        GeneratedClassNamespace = (string?)root.Attribute("GeneratedClassNamespace") ?? string.Empty;
        GeneratedClassName = (string?)root.Attribute("GeneratedClassName") ?? string.Empty;
        UseMySettingsClassName = string.Equals((string?)root.Attribute("UseMySettingsClassName"), "true", StringComparison.OrdinalIgnoreCase);

        var settings = root.Elements().FirstOrDefault(element => element.Name.LocalName == "Settings");
        if (settings is null)
        {
            throw new FormatException("Not a settings file.");
        }

        foreach (var setting in settings.Elements().Where(element => element.Name.LocalName == "Setting"))
        {
            var value = setting.Elements().FirstOrDefault(element => element.Name.LocalName == "Value");
            var entry = new SettingEntry
            {
                Name = (string?)setting.Attribute("Name") ?? string.Empty,
                Type = (string?)setting.Attribute("Type") ?? "System.String",
                Scope = (string?)setting.Attribute("Scope") ?? "User",
                Value = value?.Value ?? string.Empty,
                Description = (string?)setting.Attribute("Description") ?? string.Empty
            };
            TrackEntry(entry);
            _entries.Add(entry);
        }
    }

    private XDocument CreateDocument()
    {
        XNamespace ns = SettingsNamespace;
        GeneratedClassNamespace = _namespaceBox.Text ?? string.Empty;
        GeneratedClassName = _classNameBox.Text ?? string.Empty;
        UseMySettingsClassName = _useMySettingsClassName.IsChecked == true;

        var root = new XElement(ns + "SettingsFile",
            new XAttribute("CurrentProfile", "(Default)"),
            new XAttribute("GeneratedClassNamespace", GeneratedClassNamespace),
            new XAttribute("GeneratedClassName", GeneratedClassName));
        if (UseMySettingsClassName)
        {
            root.Add(new XAttribute("UseMySettingsClassName", "true"));
        }

        root.Add(new XElement(ns + "Profiles", new XElement(ns + "Profile", new XAttribute("Name", "(Default)"))));
        var settings = new XElement(ns + "Settings");
        foreach (var entry in _entries.Where(entry => !string.IsNullOrWhiteSpace(entry.Name)))
        {
            var setting = new XElement(ns + "Setting",
                new XAttribute("Name", entry.Name.Trim()),
                new XAttribute("Type", string.IsNullOrWhiteSpace(entry.Type) ? "System.String" : entry.Type.Trim()),
                new XAttribute("Scope", NormalizeScope(entry.Scope)));
            if (!string.IsNullOrWhiteSpace(entry.Description))
            {
                setting.Add(new XAttribute("Description", entry.Description));
            }

            setting.Add(new XElement(ns + "Value", new XAttribute("Profile", "(Default)"), entry.Value ?? string.Empty));
            settings.Add(setting);
        }

        root.Add(settings);
        return new XDocument(new XDeclaration("1.0", "utf-8", null), root);
    }

    private static string NormalizeScope(string? scope)
        => string.Equals(scope, "Application", StringComparison.OrdinalIgnoreCase) ? "Application" : "User";

    private void AddEntry(string type, string scope)
    {
        var index = 1;
        var name = "Setting" + index;
        while (_entries.Any(entry => entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            index++;
            name = "Setting" + index;
        }

        var entry = new SettingEntry { Name = name, Type = type, Scope = scope, Value = string.Empty };
        TrackEntry(entry);
        _entries.Add(entry);
        MarkDirty();
        UpdateStatus();
    }

    private void TrackEntry(SettingEntry entry)
    {
        entry.PropertyChanged += (_, _) =>
        {
            MarkDirty();
            UpdateStatus();
        };
    }

    private void DeleteSelectedEntries(ListView list)
    {
        var selected = list.SelectedItems.OfType<SettingEntry>().ToArray();
        if (selected.Length == 0 && list.SelectedItem is SettingEntry single)
        {
            selected = new[] { single };
        }

        foreach (var entry in selected)
        {
            _entries.Remove(entry);
        }

        if (selected.Length > 0)
        {
            MarkDirty();
            UpdateStatus();
        }
    }

    private void SaveToFile(bool notifyOnSuccess)
    {
        try
        {
            using var stream = File.Create(FilePath);
            Save(PrimaryFile!, stream);
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

    private string CreateStatusText()
        => $"{_entries.Count} setting(s). Scope values are User or Application.";

    private void UpdateStatus()
    {
        _status.Text = CreateStatusText();
    }

    private static DataTemplate CreateEntryTemplate()
    {
        return new DataTemplate(() =>
        {
            var root = new Grid { Padding = new Thickness(8, 4, 8, 4), ColumnSpacing = 8 };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });

            root.Children.Add(CreateEditor("Name", 0));
            root.Children.Add(CreateEditor("Type", 1));
            root.Children.Add(CreateEditor("Scope", 2));
            root.Children.Add(CreateEditor("Value", 3));
            root.Children.Add(CreateEditor("Description", 4));
            return root;
        });
    }

    private static FrameworkElement CreateEditor(string path, int column)
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
        Grid.SetColumn(box, column);
        return box;
    }

    private static void CopySelectedEntries(ListView list)
    {
        var entries = list.SelectedItems.OfType<SettingEntry>().ToArray();
        if (entries.Length == 0 && list.SelectedItem is SettingEntry single)
        {
            entries = new[] { single };
        }

        if (entries.Length == 0)
        {
            return;
        }

        var package = new DataPackage();
        package.SetText(string.Join(Environment.NewLine, entries.Select(entry => $"{entry.Name}\t{entry.Type}\t{entry.Scope}\t{entry.Value}")));
        Clipboard.SetContent(package);
    }

    private static bool IsControlPressed()
    {
        var state = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
        return (state & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
    }

    private sealed class SettingEntry : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private string _type = "System.String";
        private string _scope = "User";
        private string _value = string.Empty;
        private string _description = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Name { get => _name; set => Set(ref _name, value); }
        public string Type { get => _type; set => Set(ref _type, value); }
        public string Scope { get => _scope; set => Set(ref _scope, value); }
        public string Value { get => _value; set => Set(ref _value, value); }
        public string Description { get => _description; set => Set(ref _description, value); }

        private void Set(ref string field, string? value)
        {
            value ??= string.Empty;
            if (field == value)
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }
    }

    private sealed class SettingsOpenedFile : OpenedFile
    {
        public SettingsOpenedFile(string filePath)
        {
            FileName = ICSharpCode.Core.FileName.Create(filePath);
        }

        public override event EventHandler? FileClosed;

        public void NotifyClosed() => FileClosed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class SettingsNavigationPoint : INavigationPoint
    {
        public SettingsNavigationPoint(string fileName)
        {
            FileName = fileName;
        }

        public string FileName { get; private set; }
        public string Description => FileName;
        public string FullDescription => FileName;
        public string ToolTip => FileName;
        public object NavigationData => FileName;
        public int Index => 0;
        public void JumpTo() { }
        public void FileNameChanged(string newName) => FileName = newName;
        public void ContentChanging(object sender, EventArgs e) { }
        public int CompareTo(object? obj)
        {
            return obj is SettingsNavigationPoint other
                ? string.Compare(FileName, other.FileName, StringComparison.OrdinalIgnoreCase)
                : 0;
        }
    }
}
