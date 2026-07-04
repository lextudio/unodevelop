using System.IO;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop.Workbench;

namespace ICSharpCode.SharpDevelop.BrowserDisplayBinding
{
    public class BrowserDisplayBinding : IDisplayBinding
    {
        public bool CanCreateContentForFile(FileName fileName)
        {
            string s = fileName;
            return s.StartsWith("http:", System.StringComparison.OrdinalIgnoreCase)
                || s.StartsWith("https:", System.StringComparison.OrdinalIgnoreCase)
                || s.StartsWith("ftp:", System.StringComparison.OrdinalIgnoreCase)
                || s.StartsWith("browser:", System.StringComparison.OrdinalIgnoreCase);
        }

        public IViewContent CreateContentForFile(OpenedFile file) => null!;

        public bool IsPreferredBindingForFile(FileName fileName) => CanCreateContentForFile(fileName);

        public double AutoDetectFileContent(FileName fileName, Stream fileContent, string detectedMimeType) => 1;
    }
}
