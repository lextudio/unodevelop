using System;
using System.IO;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop.Workbench;

namespace UnoDevelop.AddIns.DisplayBindings.SettingsEditor;

public sealed class SettingsEditorDisplayBinding : IDisplayBinding
{
    public bool CanCreateContentForFile(FileName fileName)
    {
        return Path.GetExtension(fileName).Equals(".settings", StringComparison.OrdinalIgnoreCase);
    }

    public IViewContent CreateContentForFile(OpenedFile file)
    {
        return new SettingsEditorViewContent(file.FileName);
    }

    public bool IsPreferredBindingForFile(FileName fileName)
    {
        return CanCreateContentForFile(fileName);
    }

    public double AutoDetectFileContent(FileName fileName, Stream fileContent, string detectedMimeType)
    {
        return CanCreateContentForFile(fileName) ? 1 : 0;
    }
}
