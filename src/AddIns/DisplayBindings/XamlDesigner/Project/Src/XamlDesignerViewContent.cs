using System;
using System.Collections.Generic;
using System.IO;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Workbench;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace ICSharpCode.XamlDesigner
{
    public sealed class XamlDesignerViewContent : IViewContent
    {
        readonly IViewContent _primary;
        readonly Grid _root;
        readonly Grid _previewHost;
        readonly TextBlock _status;

        public XamlDesignerViewContent(IViewContent primary)
        {
            _primary = primary;

            _previewHost = new Grid
            {
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
                Margin = new Thickness(4)
            };

            _status = new TextBlock
            {
                Margin = new Thickness(8, 4, 8, 4),
                Text = "Design"
            };

            _root = new Grid();
            _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(_previewHost, 0);
            Grid.SetRow(_status, 1);
            _root.Children.Add(_previewHost);
            _root.Children.Add(_status);
            Control = _root;

            RefreshPreview();
        }

        void RefreshPreview()
        {
            var fileName = PrimaryFileName?.ToString();
            if (string.IsNullOrEmpty(fileName))
                return;

            try
            {
                var xaml = File.ReadAllText(fileName);
                var element = XamlReader.Load(xaml) as UIElement;
                _previewHost.Children.Clear();
                if (element is not null)
                {
                    _previewHost.Children.Add(element);
                    _status.Text = "Design";
                }
            }
            catch (Exception ex)
            {
                _status.Text = $"Error: {ex.Message}";
            }
        }

        public object Control { get; }
        public object InitiallyFocusedControl => Control;

        public IWorkbenchWindow? WorkbenchWindow { get; set; }
        public string TabPageText => "Design";
        public string TitleName => "Design";
        public string InfoTip => "XAML Design Surface";

        public event EventHandler? TabPageTextChanged;
        public event EventHandler? TitleNameChanged;
        public event EventHandler? InfoTipChanged;
        public event EventHandler? Disposed;

        public IList<OpenedFile> Files => Array.Empty<OpenedFile>();
        public OpenedFile? PrimaryFile => _primary?.PrimaryFile;
        public FileName? PrimaryFileName => _primary?.PrimaryFileName;

        public bool IsDisposed { get; private set; }
        public bool IsDirty => false;
        public bool IsReadOnly => true;
        public bool IsViewOnly => true;
        public bool CloseWithSolution => true;

        public event EventHandler? IsDirtyChanged { add { } remove { } }

        public ICollection<IViewContent> SecondaryViewContents => Array.Empty<IViewContent>();

        public INavigationPoint BuildNavPoint() => new DummyNavigationPoint();

        public void Save(OpenedFile file, Stream stream) { }
        public void Load(OpenedFile file, Stream stream) { }

        public bool SupportsSwitchFromThisWithoutSaveLoad(OpenedFile file, IViewContent newView) => true;
        public bool SupportsSwitchToThisWithoutSaveLoad(OpenedFile file, IViewContent oldView) => true;
        public void SwitchFromThisWithoutSaveLoad(OpenedFile file, IViewContent newView) { }
        public void SwitchToThisWithoutSaveLoad(OpenedFile file, IViewContent oldView) { }

        public object? GetService(Type serviceType) => null;

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            Disposed?.Invoke(this, EventArgs.Empty);
        }

        sealed class DummyNavigationPoint : INavigationPoint
        {
            public string Description => "XAML Designer";
            public string ShortDescription => "Design";
            public string FullDescription => "XAML Design Surface";
            public string ToolTip => "XAML Design Surface";
            public string FileName => string.Empty;
            public object? NavigationData => null;
            public int Ordinal => 0;
            public int Index => 0;
            public void JumpTo() { }
            public void FileNameChanged(string newName) { }
            public void ContentChanging(object? sender, EventArgs e) { }
            public int CompareTo(object? obj) => 0;
            public event EventHandler? DescriptionChanged { add { } remove { } }
        }
    }
}
