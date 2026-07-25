using System.IO;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop.Workbench;

namespace UnoDevelop.Workbench;

public sealed class AvalonEditDisplayBinding : IDisplayBinding
{
    public bool IsPreferredBindingForFile(FileName fileName) => false;
    public bool CanCreateContentForFile(FileName fileName) => false;
    public double AutoDetectFileContent(FileName fileName, Stream fileContent, string detectedMimeType) => 0.5;
    public IViewContent? CreateContentForFile(OpenedFile file) => null;
}
