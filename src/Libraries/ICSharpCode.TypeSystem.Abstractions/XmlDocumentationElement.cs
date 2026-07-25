using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace ICSharpCode.TypeSystem
{
	public sealed class XmlDocumentationElement
	{
		readonly XElement element;
		readonly string textContent;

		public static XmlDocumentationElement CreateText(string text)
		{
			return new XmlDocumentationElement(text);
		}

		public static IList<XmlDocumentationElement> Parse(XElement root)
		{
			return root.Nodes().Select(FromNode).Where(e => e != null).ToList();
		}

		static XmlDocumentationElement FromNode(XNode node)
		{
			var text = node as XText;
			if (text != null)
				return string.IsNullOrEmpty(text.Value) ? null : new XmlDocumentationElement(text.Value);
			var element = node as XElement;
			return element != null ? new XmlDocumentationElement(element) : null;
		}

		XmlDocumentationElement(string text)
		{
			this.textContent = text;
		}

		XmlDocumentationElement(XElement element)
		{
			this.element = element;
		}

		public bool IsTextNode {
			get { return element == null; }
		}

		public string TextContent {
			get { return textContent; }
		}

		public string Name {
			get { return element != null ? element.Name.LocalName : null; }
		}

		public IList<XmlDocumentationElement> Children {
			get { return element != null ? Parse(element) : EmptyChildren; }
		}

		static readonly IList<XmlDocumentationElement> EmptyChildren = new List<XmlDocumentationElement>();

		public string GetAttribute(string name)
		{
			if (element == null)
				return null;
			var attribute = element.Attribute(name);
			return attribute != null ? attribute.Value : null;
		}

		public IEntity ReferencedEntity {
			get { return null; }
		}
	}
}
