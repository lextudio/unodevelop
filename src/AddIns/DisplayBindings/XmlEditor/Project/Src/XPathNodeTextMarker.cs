using System;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.SharpDevelop.Editor;

namespace ICSharpCode.XmlEditor
{
	public class XPathNodeTextMarker
	{
		IDocument document;
		ITextMarkerService markerService;

		public XPathNodeTextMarker(IDocument document)
		{
			this.document = document;
			markerService = document.GetService(typeof(ITextMarkerService)) as ITextMarkerService;
		}

		public void AddMarkers(XPathNodeMatch[] nodes)
		{
		}

		public static void RemoveMarkers(IServiceProvider serviceProvider)
		{
		}
	}
}
