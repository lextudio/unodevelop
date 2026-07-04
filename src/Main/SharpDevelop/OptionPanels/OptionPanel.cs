using System.ComponentModel;
using Microsoft.UI.Xaml.Controls;

namespace UnoDevelop.OptionPanels;

/// <summary>
/// Base class for IDE option panels. Subclass this, override LoadOptions/SaveOptions,
/// and build the UI in the constructor or XAML.
/// </summary>
public class OptionPanel : UserControl, IOptionPanel, INotifyPropertyChanged
{
    public virtual object? Owner { get; set; }

    public virtual object Control => this;

    public virtual void LoadOptions()
    {
    }

    public virtual bool SaveOptions() => true;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void RaisePropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
