// The WindowsShims shim `System.Windows.Controls.Button` was consolidated away
// in favor of WinUI's Button (+ WinUIButtonExtensions for WPF-shaped members).
// Toolbar/menu code keeps using the `WpfButton` name via this global alias.
global using WpfButton = Microsoft.UI.Xaml.Controls.Button;
