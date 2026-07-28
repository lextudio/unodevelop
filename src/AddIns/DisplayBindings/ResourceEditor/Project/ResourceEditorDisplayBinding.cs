using System;
using System.IO;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop.Workbench;
using LeXtudio.OpenDevelop.ResourceFiles;

namespace UnoDevelop.AddIns.DisplayBindings.ResourceEditor;

public sealed class ResourceEditorDisplayBinding : IDisplayBinding
{
    public bool CanCreateContentForFile(FileName fileName)
    {
        return ResourceFileReader.CanRead(fileName);
    }

    public IViewContent CreateContentForFile(OpenedFile file)
    {
        if (ResourceFileReader.CanRead(file.FileName))
        {
            return new ResourceViewerViewContent(file.FileName);
        }

        throw new NotSupportedException("The selected file is not a supported resource file.");
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
