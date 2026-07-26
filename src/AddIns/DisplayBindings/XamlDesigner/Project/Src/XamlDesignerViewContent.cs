using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Gui;
using ICSharpCode.SharpDevelop.Workbench;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace ICSharpCode.XamlDesigner
{
    public sealed class XamlDesignerViewContent : IViewContent
    {
        readonly IViewContent _primary;
        readonly Grid _root;
        readonly Grid _previewHost;
        readonly Canvas _adornerLayer;
        readonly Border _selectionBorder;
        readonly TextBlock _status;
        readonly XamlToolboxProvider _toolboxProvider = new();
        readonly XamlOutlineContentHost _outlineContentHost;
        FrameworkElement? _selectedElement;

        public XamlDesignerViewContent(IViewContent primary)
        {
            _primary = primary;
            _outlineContentHost = new XamlOutlineContentHost(primary);

            _previewHost = new Grid
            {
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
                Margin = new Thickness(4)
            };
            _previewHost.AllowDrop = true;
            _previewHost.DragOver += OnPreviewDragOver;
            _previewHost.Drop += OnPreviewDrop;

            _adornerLayer = new Canvas { IsHitTestVisible = true };
            _selectionBorder = new Border
            {
                BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.DodgerBlue),
                BorderThickness = new Thickness(2),
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };
            _adornerLayer.Children.Add(_selectionBorder);
            AddResizeThumb(HorizontalAlignment.Left, VerticalAlignment.Top, -1, -1);
            AddResizeThumb(HorizontalAlignment.Right, VerticalAlignment.Top, 1, -1);
            AddResizeThumb(HorizontalAlignment.Left, VerticalAlignment.Bottom, -1, 1);
            AddResizeThumb(HorizontalAlignment.Right, VerticalAlignment.Bottom, 1, 1);

            _status = new TextBlock
            {
                Margin = new Thickness(8, 4, 8, 4),
                Text = "Design",
                // WinUI's TextBlock defaults to no wrapping and no text selection - a long
                // exception message (the common case here, e.g. a XAML parse error naming an
                // unresolvable type) would otherwise overflow off-screen with no way to read or
                // copy it out.
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true
            };
            // Scrollable rather than a hard clip: a wrapped multi-line error can still exceed a
            // reasonable status-bar height, and clipping would just trade "unreadable off to the
            // side" for "unreadable off the bottom".
            var statusScroller = new ScrollViewer
            {
                Content = _status,
                MaxHeight = 160,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            _root = new Grid();
            _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(_previewHost, 0);
            Grid.SetRow(_adornerLayer, 0);
            Grid.SetRow(statusScroller, 1);
            _root.Children.Add(_previewHost);
            _root.Children.Add(_adornerLayer);
            _root.Children.Add(statusScroller);
            Control = _root;

            RefreshPreview();
        }

        /// <summary>
        /// Current status text of the preview surface ("Design" on success, "Error: ..." on
        /// failure) — exposed for DevFlow/integration-test inspection (ide-xaml-preview-status).
        /// </summary>
        public string StatusText => _status.Text;

        /// <summary>Whether the last <see cref="RefreshPreview"/> call rendered successfully.</summary>
        public bool HasRenderedPreview => _previewHost.Children.Count > 0;

        public void RefreshPreview()
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
                    HookSelectableElements(element);
                    _status.Text = "Design";
                }
                else
                {
                    _status.Text = "Error: XAML did not produce a UIElement";
                }
            }
            catch (Exception ex)
            {
                _previewHost.Children.Clear();
                SelectElement(null);
                _status.Text = $"Error: {ex.Message}";
            }
        }

        public string? SelectedElementType => _selectedElement?.GetType().Name;
        public bool HasSelectionAdorner => _selectionBorder.Visibility == Visibility.Visible;

        public bool SelectElementByType(string typeName, int index = 0)
        {
            var match = EnumerateElements(_previewHost.Children.FirstOrDefault())
                .Where(element => string.Equals(element.GetType().Name, typeName, StringComparison.OrdinalIgnoreCase))
                .Skip(Math.Max(0, index))
                .FirstOrDefault();
            SelectElement(match as FrameworkElement);
            return match is FrameworkElement;
        }

        public bool AddToolboxItem(string xaml)
        {
            try
            {
                if (XamlReader.Load(PrepareStandaloneXaml(xaml)) is not UIElement element)
                    return false;
                var target = _selectedElement as Panel
                    ?? EnumerateElements(_previewHost.Children.FirstOrDefault()).OfType<Panel>().FirstOrDefault();
                if (target is null)
                    return false;
                target.Children.Add(element);
                HookSelectableElements(element);
                SelectElement(element as FrameworkElement);
                _status.Text = "Design";
                return true;
            }
            catch (Exception ex)
            {
                _status.Text = "Error: " + ex.Message;
                return false;
            }
        }

        static string PrepareStandaloneXaml(string xaml)
        {
            var document = XDocument.Parse(xaml);
            if (document.Root is not null && document.Root.Name.Namespace == XNamespace.None)
                document.Root.Name = XName.Get(document.Root.Name.LocalName,
                    "http://schemas.microsoft.com/winfx/2006/xaml/presentation");
            return document.ToString(SaveOptions.DisableFormatting);
        }

        public bool ResizeSelection(double widthDelta, double heightDelta)
        {
            if (_selectedElement is null)
                return false;
            var width = double.IsNaN(_selectedElement.Width) ? _selectedElement.ActualWidth : _selectedElement.Width;
            var height = double.IsNaN(_selectedElement.Height) ? _selectedElement.ActualHeight : _selectedElement.Height;
            _selectedElement.Width = Math.Max(8, width + widthDelta);
            _selectedElement.Height = Math.Max(8, height + heightDelta);
            UpdateSelectionAdorner();
            PublishSelection();
            return true;
        }

        void OnPreviewDragOver(object sender, DragEventArgs args)
        {
            if (args.DataView.Contains(StandardDataFormats.Text))
                args.AcceptedOperation = DataPackageOperation.Copy;
        }

        async void OnPreviewDrop(object sender, DragEventArgs args)
        {
            if (!args.DataView.Contains(StandardDataFormats.Text))
                return;
            var payload = await args.DataView.GetTextAsync();
            if (payload.StartsWith(XamlToolboxProvider.PayloadPrefix, StringComparison.Ordinal))
                AddToolboxItem(payload.Substring(XamlToolboxProvider.PayloadPrefix.Length));
        }

        void HookSelectableElements(UIElement? root)
        {
            foreach (var element in EnumerateElements(root).OfType<FrameworkElement>())
            {
                element.Tapped -= OnElementTapped;
                element.Tapped += OnElementTapped;
            }
        }

        void OnElementTapped(object sender, TappedRoutedEventArgs args)
        {
            if (sender is FrameworkElement element)
            {
                SelectElement(element);
                args.Handled = true;
            }
        }

        void SelectElement(FrameworkElement? element)
        {
            _selectedElement = element;
            UpdateSelectionAdorner();
            PublishSelection();
        }

        void PublishSelection()
        {
            var pad = SD.Workbench.PadContentCollection.FirstOrDefault(candidate =>
                candidate.ClassName.EndsWith(".PropertiesPad", StringComparison.Ordinal));
            if (pad is null)
                return;
            SD.Workbench.ActivatePad(pad);
            pad.CreatePad();
            var control = pad.PadContent?.Control;
            control?.GetType().GetMethod("SetSelectedObject")?.Invoke(control, new object?[] { _selectedElement });
        }

        void UpdateSelectionAdorner()
        {
            if (_selectedElement is null)
            {
                _selectionBorder.Visibility = Visibility.Collapsed;
                SetThumbVisibility(Visibility.Collapsed);
                return;
            }
            var width = _selectedElement.ActualWidth > 0
                ? _selectedElement.ActualWidth
                : double.IsFinite(_selectedElement.Width) ? _selectedElement.Width : Math.Max(40, _selectedElement.MinWidth);
            var height = _selectedElement.ActualHeight > 0
                ? _selectedElement.ActualHeight
                : double.IsFinite(_selectedElement.Height) ? _selectedElement.Height : Math.Max(24, _selectedElement.MinHeight);
            var point = _selectedElement.TransformToVisual(_adornerLayer).TransformPoint(new Windows.Foundation.Point());
            Canvas.SetLeft(_selectionBorder, point.X);
            Canvas.SetTop(_selectionBorder, point.Y);
            _selectionBorder.Width = width;
            _selectionBorder.Height = height;
            _selectionBorder.Visibility = Visibility.Visible;
            PositionThumbs(point.X, point.Y, width, height);
        }

        void AddResizeThumb(HorizontalAlignment horizontal, VerticalAlignment vertical, int horizontalDirection, int verticalDirection)
        {
            var thumb = new Thumb
            {
                Width = 10,
                Height = 10,
                Background = new SolidColorBrush(Microsoft.UI.Colors.DodgerBlue),
                Visibility = Visibility.Collapsed,
                Tag = (horizontalDirection, verticalDirection)
            };
            thumb.DragDelta += (_, args) =>
                ResizeSelection(args.HorizontalChange * horizontalDirection, args.VerticalChange * verticalDirection);
            _adornerLayer.Children.Add(thumb);
        }

        void SetThumbVisibility(Visibility visibility)
        {
            foreach (var thumb in _adornerLayer.Children.OfType<Thumb>())
                thumb.Visibility = visibility;
        }

        void PositionThumbs(double left, double top, double width, double height)
        {
            foreach (var thumb in _adornerLayer.Children.OfType<Thumb>())
            {
                var direction = ((int Horizontal, int Vertical))thumb.Tag;
                Canvas.SetLeft(thumb, direction.Horizontal < 0 ? left - 5 : left + width - 5);
                Canvas.SetTop(thumb, direction.Vertical < 0 ? top - 5 : top + height - 5);
                thumb.Visibility = Visibility.Visible;
            }
        }

        static IEnumerable<UIElement> EnumerateElements(UIElement? root)
        {
            if (root is null)
                yield break;
            yield return root;
            if (root is Panel panel)
            {
                foreach (var child in panel.Children)
                    foreach (var descendant in EnumerateElements(child))
                        yield return descendant;
            }
            else if (root is ContentControl contentControl && contentControl.Content is UIElement child)
            {
                foreach (var descendant in EnumerateElements(child))
                    yield return descendant;
            }
            else if (root is Border border && border.Child is UIElement borderChild)
            {
                foreach (var descendant in EnumerateElements(borderChild))
                    yield return descendant;
            }
        }

        public IReadOnlyList<object> GetSnapshot()
        {
            if (_previewHost.Children.Count == 0)
                return Array.Empty<object>();

            var result = new List<object>();
            AddSnapshotItems(_previewHost.Children[0], result, 0);
            return result;
        }

        static void AddSnapshotItems(UIElement element, List<object> result, int depth)
        {
            var frameworkElement = element as FrameworkElement;
            result.Add(new
            {
                Type = element.GetType().Name,
                Name = frameworkElement?.Name ?? string.Empty,
                Text = element is TextBlock textBlock ? textBlock.Text : null,
                Content = element is ContentControl contentHost && contentHost.Content is string text ? text : null,
                Depth = depth
            });

            if (element is Panel panel)
            {
                foreach (var child in panel.Children)
                    AddSnapshotItems(child, result, depth + 1);
            }
            else if (element is ContentControl contentControl && contentControl.Content is UIElement child)
            {
                AddSnapshotItems(child, result, depth + 1);
            }
            else if (element is Border border && border.Child is UIElement borderChild)
            {
                AddSnapshotItems(borderChild, result, depth + 1);
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

        public object? GetService(Type serviceType)
            => serviceType == typeof(IToolboxProvider) ? _toolboxProvider
                : serviceType == typeof(IOutlineContentHost) ? _outlineContentHost
                : null;

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
