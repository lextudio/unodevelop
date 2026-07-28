using ICSharpCode.SharpDevelop.Workbench;

namespace ICSharpCode.XmlEditor
{
	public class XPathQueryPad : AbstractPadContent
	{
		XPathQueryControl control = new XPathQueryControl();

		public override object Control => control;
	}
}
