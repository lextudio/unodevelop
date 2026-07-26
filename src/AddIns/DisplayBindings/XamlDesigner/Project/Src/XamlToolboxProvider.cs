using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace ICSharpCode.XamlDesigner
{
    public sealed class XamlToolboxProvider : UserControl, ICSharpCode.SharpDevelop.Gui.IToolboxProvider
    {
        public const string PayloadPrefix = "UnoDevelop.XamlToolbox:";
        readonly Dictionary<string, Expander> _groups = new();

        static readonly IReadOnlyList<ToolboxItem> Items = new[]
        {
            new ToolboxItem("Layout", "Grid", "<Grid />"),
            new ToolboxItem("Layout", "StackPanel", "<StackPanel />"),
            new ToolboxItem("Layout", "Canvas", "<Canvas />"),
            new ToolboxItem("Layout", "Border", "<Border Padding=\"8\" />"),
            new ToolboxItem("Layout", "ScrollViewer", "<ScrollViewer />"),
            new ToolboxItem("Layout", "RelativePanel", "<RelativePanel />"),
            new ToolboxItem("Layout", "Viewbox", "<Viewbox />"),

            new ToolboxItem("Controls", "Button", "<Button Content=\"Button\" />"),
            new ToolboxItem("Controls", "TextBlock", "<TextBlock Text=\"Text\" />"),
            new ToolboxItem("Controls", "HyperlinkButton", "<HyperlinkButton Content=\"Link\" />"),
            new ToolboxItem("Controls", "Image", "<Image Width=\"120\" Height=\"80\" />"),
            new ToolboxItem("Controls", "ProgressBar", "<ProgressBar Width=\"160\" Value=\"50\" />"),
            new ToolboxItem("Controls", "ProgressRing", "<ProgressRing IsActive=\"True\" />"),
            new ToolboxItem("Controls", "InfoBar", "<InfoBar Title=\"Information\" IsOpen=\"True\" />"),

            new ToolboxItem("Input", "TextBox", "<TextBox Text=\"Text\" />"),
            new ToolboxItem("Input", "PasswordBox", "<PasswordBox />"),
            new ToolboxItem("Input", "CheckBox", "<CheckBox Content=\"Check box\" />"),
            new ToolboxItem("Input", "RadioButton", "<RadioButton Content=\"Option\" />"),
            new ToolboxItem("Input", "ToggleButton", "<ToggleButton Content=\"Toggle\" />"),
            new ToolboxItem("Input", "ToggleSwitch", "<ToggleSwitch Header=\"Setting\" />"),
            new ToolboxItem("Input", "Slider", "<Slider Width=\"160\" Value=\"50\" />"),
            new ToolboxItem("Input", "NumberBox", "<NumberBox Width=\"120\" Value=\"0\" />"),
            new ToolboxItem("Input", "ComboBox", "<ComboBox Width=\"160\" />"),
            new ToolboxItem("Input", "DatePicker", "<DatePicker />"),
            new ToolboxItem("Input", "TimePicker", "<TimePicker />"),
            new ToolboxItem("Input", "CalendarDatePicker", "<CalendarDatePicker />"),

            new ToolboxItem("Collections", "ListView", "<ListView />"),
            new ToolboxItem("Collections", "GridView", "<GridView />"),
            new ToolboxItem("Collections", "TreeView", "<TreeView />"),
            new ToolboxItem("Collections", "ItemsRepeater", "<ItemsRepeater />"),
            new ToolboxItem("Collections", "FlipView", "<FlipView />"),

            new ToolboxItem("Navigation", "NavigationView", "<NavigationView />"),
            new ToolboxItem("Navigation", "TabView", "<TabView />"),
            new ToolboxItem("Navigation", "BreadcrumbBar", "<BreadcrumbBar />"),
            new ToolboxItem("Navigation", "CommandBar", "<CommandBar />"),
            new ToolboxItem("Navigation", "MenuBar", "<MenuBar />"),

            new ToolboxItem("Media", "MediaPlayerElement", "<MediaPlayerElement Width=\"320\" Height=\"180\" />"),
            new ToolboxItem("Media", "WebView2", "<WebView2 Width=\"320\" Height=\"240\" />"),
            new ToolboxItem("Media", "PersonPicture", "<PersonPicture Width=\"48\" Height=\"48\" />"),

            new ToolboxItem("Shapes", "Rectangle", "<Rectangle Width=\"100\" Height=\"60\" Fill=\"Gray\" />"),
            new ToolboxItem("Shapes", "Ellipse", "<Ellipse Width=\"80\" Height=\"80\" Fill=\"Gray\" />"),
            new ToolboxItem("Shapes", "Line", "<Line X1=\"0\" Y1=\"0\" X2=\"100\" Y2=\"0\" Stroke=\"Black\" />"),
            new ToolboxItem("Shapes", "Path", "<Path Data=\"M 0,0 L 80,0 40,60 Z\" Fill=\"Gray\" />")
        };

        public XamlToolboxProvider()
        {
            var groups = new StackPanel
            {
                Spacing = 10,
                Padding = new Thickness(8),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            foreach (var group in Items.GroupBy(item => item.Category))
            {
                var expander = new Expander
                {
                    Header = group.Key,
                    IsExpanded = true,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Content = CreateItemList(group.ToArray())
                };
                _groups[group.Key] = expander;
                groups.Children.Add(expander);
            }
            Content = new ScrollViewer
            {
                Content = groups,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollMode = ScrollMode.Disabled
            };
        }

        public object ToolboxContent => this;

        static ListView CreateItemList(IReadOnlyList<ToolboxItem> items)
        {
            var list = new ListView
            {
                ItemsSource = items,
                CanDragItems = true,
                SelectionMode = ListViewSelectionMode.Single,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            list.DragItemsStarting += (_, args) =>
            {
                if (args.Items.Count == 0 || args.Items[0] is not ToolboxItem item)
                    return;
                args.Data.SetText(PayloadPrefix + item.Xaml);
                args.Data.RequestedOperation = DataPackageOperation.Copy;
            };
            list.ItemTemplate = new DataTemplate(() =>
            {
                var name = new TextBlock { Padding = new Thickness(8, 5, 8, 5) };
                name.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding
                {
                    Path = new PropertyPath(nameof(ToolboxItem.Name))
                });
                return name;
            });
            return list;
        }

        public IReadOnlyList<object> GetSnapshot()
        {
            var result = new List<object>();
            foreach (var item in Items)
                result.Add(new { item.Category, item.Name, item.Xaml });
            return result;
        }

        public IReadOnlyList<object> GetGroupSnapshot()
        {
            var result = new List<object>();
            foreach (var group in Items.GroupBy(item => item.Category))
                result.Add(new
                {
                    Name = group.Key,
                    IsCollapsible = true,
                    IsExpanded = _groups[group.Key].IsExpanded,
                    Items = group.Select(item => item.Name).ToArray()
                });
            return result;
        }

        public bool SetGroupExpanded(string groupName, bool expanded)
        {
            if (!_groups.TryGetValue(groupName, out var group))
                return false;
            group.IsExpanded = expanded;
            return true;
        }

        public sealed record ToolboxItem(string Category, string Name, string Xaml);
    }
}
