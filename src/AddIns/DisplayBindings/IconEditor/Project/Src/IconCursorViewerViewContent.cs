using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Workbench;
using LeXtudio.OpenDevelop.ResourceFiles;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;

namespace ICSharpCode.IconEditor;

internal sealed class IconCursorViewerViewContent : IViewContent
{
    private readonly List<OpenedFile> _files = new();
    private readonly ObservableCollection<IconCursorFrame> _frames;
    private readonly Grid _control;
    private readonly TextBlock _previewStatus;
    private readonly Image _previewImage;

    public IconCursorViewerViewContent(string filePath)
    {
        FilePath = filePath;
        TabPageText = Path.GetFileName(filePath);
        TitleName = TabPageText;
        InfoTip = filePath;
        _files.Add(new IconCursorOpenedFile(filePath));

        var iconFile = IconCursorFileReader.Read(filePath);
        _frames = new ObservableCollection<IconCursorFrame>(iconFile.Frames);

        var header = new TextBlock
        {
            Text = $"{iconFile.Kind}: {iconFile.Frames.Count} frame(s)",
            Margin = new Thickness(8, 8, 8, 4),
            Style = Application.Current.Resources["BodyTextBlockStyle"] as Style
        };

        var list = new ListView
        {
            ItemsSource = _frames,
            SelectionMode = ListViewSelectionMode.Single,
            ItemTemplate = CreateFrameTemplate(),
            Margin = new Thickness(8, 0, 4, 8)
        };
        list.SelectionChanged += (_, _) => ShowFrame(list.SelectedItem as IconCursorFrame);
        list.DoubleTapped += (_, _) => CopySelectedFrame(list.SelectedItem as IconCursorFrame);

        var copy = new MenuFlyoutItem { Text = "Copy metadata" };
        copy.Click += (_, _) => CopySelectedFrame(list.SelectedItem as IconCursorFrame);
        list.ContextFlyout = new MenuFlyout { Items = { copy } };

        _previewStatus = new TextBlock
        {
            Text = "Select a frame.",
            Margin = new Thickness(8),
            TextWrapping = TextWrapping.Wrap
        };
        _previewImage = new Image
        {
            Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 512,
            MaxHeight = 512
        };

        var previewPanel = new Grid { Margin = new Thickness(4, 0, 8, 8) };
        previewPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        previewPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_previewStatus, 0);
        Grid.SetRow(_previewImage, 1);
        previewPanel.Children.Add(_previewStatus);
        previewPanel.Children.Add(_previewImage);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(list, 0);
        Grid.SetColumn(previewPanel, 1);
        body.Children.Add(list);
        body.Children.Add(previewPanel);

        _control = new Grid();
        _control.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _control.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(header, 0);
        Grid.SetRow(body, 1);
        _control.Children.Add(header);
        _control.Children.Add(body);

        if (_frames.Count > 0)
        {
            list.SelectedIndex = 0;
        }
    }

    public string FilePath { get; }
    public object? Control => _control;
    public object? InitiallyFocusedControl => _control;
    public IWorkbenchWindow? WorkbenchWindow { get; set; }
    public event EventHandler? TabPageTextChanged;
    public string TabPageText { get; }
    public string TitleName { get; }
    public event EventHandler? TitleNameChanged;
    public string InfoTip { get; }
    public event EventHandler? InfoTipChanged;
    public void Save(OpenedFile file, Stream stream) { }
    public void Load(OpenedFile file, Stream stream) { }
    public IList<OpenedFile> Files => _files;
    public OpenedFile? PrimaryFile => _files[0];
    public FileName? PrimaryFileName => PrimaryFile?.FileName;
    public INavigationPoint BuildNavPoint() => new IconCursorNavigationPoint(FilePath);
    public bool IsDisposed { get; private set; }
    public event EventHandler? Disposed;
    public bool IsReadOnly => true;
    public bool IsViewOnly => true;
    public bool CloseWithSolution => true;
    public ICollection<IViewContent> SecondaryViewContents => Array.Empty<IViewContent>();
    public bool IsDirty => false;
    public event EventHandler? IsDirtyChanged;
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
        if (PrimaryFile is IconCursorOpenedFile openedFile)
        {
            openedFile.NotifyClosed();
        }

        Disposed?.Invoke(this, EventArgs.Empty);
    }

    private void ShowFrame(IconCursorFrame? frame)
    {
        _previewImage.Source = null;
        if (frame is null)
        {
            _previewStatus.Text = "Select a frame.";
            return;
        }

        if (!frame.IsPng)
        {
            _previewStatus.Text = frame.Description + Environment.NewLine + "DIB/BMP icon frames are parsed, but preview rendering is not implemented yet.";
            return;
        }

        try
        {
            var bitmap = new BitmapImage();
            using var stream = CreateRandomAccessStream(frame.Data);
            bitmap.SetSource(stream);
            _previewImage.Source = bitmap;
            _previewStatus.Text = frame.Description;
        }
        catch (Exception ex)
        {
            _previewStatus.Text = frame.Description + Environment.NewLine + "Preview failed: " + ex.Message;
        }
    }

    private static InMemoryRandomAccessStream CreateRandomAccessStream(byte[] bytes)
    {
        var stream = new InMemoryRandomAccessStream();
        using var writer = new DataWriter(stream.GetOutputStreamAt(0));
        writer.WriteBytes(bytes);
        writer.StoreAsync().AsTask().GetAwaiter().GetResult();
        writer.FlushAsync().AsTask().GetAwaiter().GetResult();
        stream.Seek(0);
        return stream;
    }

    private static DataTemplate CreateFrameTemplate()
    {
        return new DataTemplate(() =>
        {
            var root = new Grid { Padding = new Thickness(8, 6, 8, 6), ColumnSpacing = 10 };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var index = CreateCell("Index", true);
            var size = CreateCell("SizeText", false);
            var format = CreateCell("Format", false);
            var hotspot = CreateCell("HotspotText", false);
            var description = CreateCell("Description", false);

            Grid.SetColumn(index, 0);
            Grid.SetColumn(size, 1);
            Grid.SetColumn(format, 2);
            Grid.SetColumn(hotspot, 3);
            Grid.SetColumn(description, 4);
            root.Children.Add(index);
            root.Children.Add(size);
            root.Children.Add(format);
            root.Children.Add(hotspot);
            root.Children.Add(description);
            return root;
        });
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

    private static void CopySelectedFrame(IconCursorFrame? frame)
    {
        if (frame is null)
        {
            return;
        }

        var package = new DataPackage();
        package.SetText(frame.Description);
        Clipboard.SetContent(package);
    }

    private sealed class IconCursorOpenedFile : SimpleOpenedFile
    {
        public IconCursorOpenedFile(string filePath)
            : base(filePath)
        {
        }
    }

    private sealed class IconCursorNavigationPoint : INavigationPoint
    {
        public IconCursorNavigationPoint(string fileName)
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
            return obj is IconCursorNavigationPoint other
                ? string.Compare(FileName, other.FileName, StringComparison.OrdinalIgnoreCase)
                : 0;
        }
    }
}
