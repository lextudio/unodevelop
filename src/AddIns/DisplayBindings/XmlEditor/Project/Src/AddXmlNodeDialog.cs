using System;
using System.Collections.Generic;
using ICSharpCode.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ICSharpCode.XmlEditor
{
	public class AddXmlNodeDialog : IAddXmlNodeDialog
	{
		ListBox namesListBox;
		TextBox customNameTextBox;

		public AddXmlNodeDialog(string[] names)
		{
			InitStrings();
		}

		public string Title { get; set; } = "";
		public string CustomNameLabelText { get; set; } = "";

		public string[] GetNames() => new string[0];

		public AddXmlNodeDialogResult ShowDialog() => AddXmlNodeDialogResult.Cancel;

		public void Dispose() { }

		void InitStrings() { }
	}
}
