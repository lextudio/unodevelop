using System;
using System.IO;
using ICSharpCode.SharpDevelop.Workbench;

namespace ICSharpCode.XamlDesigner
{
    public class XamlDesignerDisplayBinding : ISecondaryDisplayBinding
    {
        public bool ReattachWhenParserServiceIsReady => false;

        public bool CanAttachTo(IViewContent content)
        {
            return content?.PrimaryFileName?.ToString().EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) == true;
        }

        public IViewContent[] CreateSecondaryViewContent(IViewContent viewContent)
        {
            return new IViewContent[] { new XamlDesignerViewContent(viewContent) };
        }
    }
}
