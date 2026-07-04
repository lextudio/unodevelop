using System;
using System.Windows;
using ICSharpCode.SharpDevelop;
using WinUIClipboard = Windows.ApplicationModel.DataTransfer.Clipboard;
using WinUIDataPackage = Windows.ApplicationModel.DataTransfer.DataPackage;

namespace UnoDevelop.Services;

internal sealed class UnoClipboardService : IClipboard
{
    public void Clear() => WinUIClipboard.Clear();

    public IDataObject GetDataObject()
        => System.Windows.Clipboard.GetDataObject();

    public void SetDataObject(object data)
    {
        if (data is IDataObject dataObject)
            System.Windows.Clipboard.SetDataObject(dataObject, false);
        else if (data is string text)
            SetText(text);
    }

    public void SetDataObject(object data, bool copy)
    {
        if (data is IDataObject dataObject)
            System.Windows.Clipboard.SetDataObject(dataObject, copy);
        else if (data is string text)
            SetText(text);
    }

    public bool ContainsText()
    {
        var content = WinUIClipboard.GetContent();
        return content?.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text) == true;
    }

    public string GetText()
    {
        var content = WinUIClipboard.GetContent();
        if (content?.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text) == true)
        {
            return content.GetTextAsync().GetAwaiter().GetResult() ?? string.Empty;
        }
        return string.Empty;
    }

    public void SetText(string text)
    {
        var package = new WinUIDataPackage();
        package.SetText(text);
        WinUIClipboard.SetContent(package);
    }
}
