using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ICSharpCode.XmlEditor
{
	public class SelectXmlSchemaWindow : ISelectXmlSchemaWindow
	{
		ListBox schemaListBox;
		ContentDialog dialog;
		XmlSchemaPicker schemaPicker;
		bool result;

		public SelectXmlSchemaWindow(string[] schemaNamespaces)
		{
			schemaListBox = new ListBox();
			schemaListBox.DoubleTapped += (s, e) => { result = true; dialog.Hide(); };

			dialog = new ContentDialog
			{
				Title = "Select Xml Schema",
				Content = schemaListBox,
				DefaultButton = ContentDialogButton.Primary,
			};
			dialog.PrimaryButtonClick += (s, e) => { result = true; };

			schemaPicker = new XmlSchemaPicker(schemaNamespaces, this);
		}

		public bool? ShowDialog()
		{
			dialog.ShowAsync().GetAwaiter().GetResult();
			return result;
		}

		public object SelectedItem
		{
			get { return schemaListBox.SelectedItem; }
		}

		public int SelectedIndex
		{
			get { return schemaListBox.SelectedIndex; }
			set { schemaListBox.SelectedIndex = value; }
		}

		public void AddSchemaNamespace(string namespaceUri)
		{
			schemaListBox.Items.Add(namespaceUri);
		}

		public int IndexOfItem(object item)
		{
			return schemaListBox.Items.IndexOf(item);
		}

		public string SelectedNamespaceUri
		{
			get { return schemaPicker.GetSelectedSchemaNamespace(); }
			set { schemaPicker.SelectSchemaNamespace(value); }
		}
	}
}
