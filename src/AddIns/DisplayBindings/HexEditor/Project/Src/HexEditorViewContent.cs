using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Workbench;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace UnoDevelop.AddIns.DisplayBindings.HexEditor;

public sealed class HexEditorViewContent : IViewContent
{
    private const int BytesPerLine = 16;

    private readonly FileName _fileName;
    private readonly List<OpenedFile> _files = new();
    private readonly Grid _control;
    private readonly TextBox _hexText;
    private readonly TextBlock _status;
    private byte[] _bytes = Array.Empty<byte>();
    private bool _isDirty;

    public HexEditorViewContent(FileName fileName)
    {
        _fileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
        _files.Add(new HexOpenedFile(_fileName));

        TabPageText = Path.GetFileName(_fileName.ToString());
        TitleName = TabPageText;
        InfoTip = _fileName.ToString();

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(8, 6, 8, 4)
        };

        var saveButton = new Button { Content = "Save", MinWidth = 72 };
        saveButton.Click += (_, _) => SaveToDisk();
        var reloadButton = new Button { Content = "Reload", MinWidth = 72 };
        reloadButton.Click += (_, _) => LoadFromDisk();
        var copyButton = new Button { Content = "Copy", MinWidth = 72 };
        copyButton.Click += (_, _) => CopySelection();
        _status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        toolbar.Children.Add(saveButton);
        toolbar.Children.Add(reloadButton);
        toolbar.Children.Add(copyButton);
        toolbar.Children.Add(_status);

        _hexText = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            FontSize = 13,
            Margin = new Thickness(8, 0, 8, 8)
        };
        _hexText.TextChanged += HexTextChanged;

        _control = new Grid();
        _control.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _control.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(toolbar, 0);
        Grid.SetRow(_hexText, 1);
        _control.Children.Add(toolbar);
        _control.Children.Add(_hexText);

        LoadFromDisk();
    }

    public object? Control => _control;
    public object? InitiallyFocusedControl => _hexText;
    public IWorkbenchWindow? WorkbenchWindow { get; set; }
    public event EventHandler? TabPageTextChanged;
    public string TabPageText { get; }
    public string TitleName { get; }
    public event EventHandler? TitleNameChanged;
    public string InfoTip { get; }
    public event EventHandler? InfoTipChanged;
    public IList<OpenedFile> Files => _files;
    public OpenedFile? PrimaryFile => _files[0];
    public FileName? PrimaryFileName => _fileName;
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
        var bytes = ParseHexDump(_hexText.Text);
        stream.SetLength(0);
        stream.Write(bytes, 0, bytes.Length);
        _bytes = bytes;
        MarkClean();
        UpdateStatus();
    }

    public void Load(OpenedFile file, Stream stream)
    {
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        _bytes = memory.ToArray();
        SetHexText(FormatHexDump(_bytes));
        MarkClean();
        UpdateStatus();
    }

    public INavigationPoint BuildNavPoint() => new HexNavigationPoint(_fileName.ToString());
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
        Disposed?.Invoke(this, EventArgs.Empty);
    }

    private void LoadFromDisk()
    {
        _bytes = File.ReadAllBytes(_fileName.ToString());
        SetHexText(FormatHexDump(_bytes));
        MarkClean();
        UpdateStatus();
    }

    private void SaveToDisk()
    {
        var bytes = ParseHexDump(_hexText.Text);
        File.WriteAllBytes(_fileName.ToString(), bytes);
        _bytes = bytes;
        MarkClean();
        UpdateStatus();
    }

    private void SetHexText(string text)
    {
        _hexText.TextChanged -= HexTextChanged;
        _hexText.Text = text;
        _hexText.TextChanged += HexTextChanged;
    }

    private void HexTextChanged(object sender, TextChangedEventArgs e) => MarkDirty();

    private void MarkDirty()
    {
        if (_isDirty)
        {
            return;
        }

        _isDirty = true;
        IsDirtyChanged?.Invoke(this, EventArgs.Empty);
        UpdateStatus();
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

    private void UpdateStatus()
    {
        _status.Text = $"{_bytes.Length:N0} byte(s)" + (_isDirty ? " - modified" : string.Empty);
    }

    private void CopySelection()
    {
        var text = string.IsNullOrEmpty(_hexText.SelectedText) ? _hexText.Text : _hexText.SelectedText;
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    private static string FormatHexDump(byte[] bytes)
    {
        var builder = new StringBuilder();
        for (var offset = 0; offset < bytes.Length; offset += BytesPerLine)
        {
            var count = Math.Min(BytesPerLine, bytes.Length - offset);
            builder.Append(offset.ToString("X8", CultureInfo.InvariantCulture));
            builder.Append("  ");

            for (var i = 0; i < BytesPerLine; i++)
            {
                if (i < count)
                {
                    builder.Append(bytes[offset + i].ToString("X2", CultureInfo.InvariantCulture));
                }
                else
                {
                    builder.Append("  ");
                }

                builder.Append(i == 7 ? "  " : " ");
            }

            builder.Append(" |");
            for (var i = 0; i < count; i++)
            {
                var b = bytes[offset + i];
                builder.Append(b >= 32 && b <= 126 ? (char)b : '.');
            }

            builder.AppendLine("|");
        }

        return builder.ToString();
    }

    private static byte[] ParseHexDump(string text)
    {
        var bytes = new List<byte>();
        foreach (var rawLine in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            var line = rawLine;
            var bar = line.IndexOf('|');
            if (bar >= 0)
            {
                line = line[..bar];
            }

            var parts = line.Split(new[] { ' ', '\t', ':' }, StringSplitOptions.RemoveEmptyEntries);
            var start = parts.Length > 0 && parts[0].Length == 8 && IsHex(parts[0]) ? 1 : 0;
            for (var i = start; i < parts.Length; i++)
            {
                var token = parts[i];
                if (token.Length != 2 || !IsHex(token))
                {
                    continue;
                }

                bytes.Add(byte.Parse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            }
        }

        return bytes.ToArray();
    }

    private static bool IsHex(string value)
    {
        return value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');
    }

    private sealed class HexOpenedFile : OpenedFile
    {
        public HexOpenedFile(FileName fileName)
        {
            FileName = fileName;
        }

        public override event EventHandler? FileClosed;
    }

    private sealed class HexNavigationPoint : INavigationPoint
    {
        public HexNavigationPoint(string fileName)
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
        public int CompareTo(object? obj) => obj is HexNavigationPoint ? 0 : -1;
    }
}
