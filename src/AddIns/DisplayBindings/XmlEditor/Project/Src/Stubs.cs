using System;
using ICSharpCode.SharpDevelop.Workbench;

namespace ICSharpCode.XmlEditor
{
	public class XmlTreeView
	{
		public XmlTreeView() { }
		public XmlTreeView(IViewContent viewContent, XmlSchemaCompletionCollection schemas, XmlSchemaCompletion defaultSchema) { }
	}

	public class XmlTreeViewContainerControl
	{
		public void AddAttribute() { }
		public void RemoveAttribute() { }
		public void AddChildElement() { }
		public void AppendChildComment() { }
		public void AppendChildTextNode() { }
		public void InsertElementBefore() { }
		public void InsertElementAfter() { }
		public void InsertCommentBefore() { }
		public void InsertCommentAfter() { }
		public void InsertTextNodeBefore() { }
		public void InsertTextNodeAfter() { }
		public void ExpandAll() { }
		public void CollapseAll() { }
	}

	public class SelectXmlSchemaWindow
	{
		public SelectXmlSchemaWindow(string[] namespaces) { }
		public string SelectedNamespaceUri { get; set; }
	}

	public static class XPathNodeTextMarker
	{
		public static void RemoveMarkers(object textEditor) { }
	}
}
