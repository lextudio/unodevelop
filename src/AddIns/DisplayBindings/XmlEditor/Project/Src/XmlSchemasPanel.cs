using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop;
using UnoDevelop.OptionPanels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;

namespace ICSharpCode.XmlEditor
{
	public class XmlSchemasPanel : UserControl, IOptionPanel, IXmlSchemasPanel
	{
		RegisteredXmlSchemasEditor editor;

		public XmlSchemasPanel()
			: this(XmlEditorService.RegisteredXmlSchemas,
				new DefaultXmlFileExtensions(),
				XmlEditorService.XmlSchemaFileAssociations,
				XmlEditorService.RegisteredXmlSchemas)
		{
		}

		public XmlSchemasPanel(RegisteredXmlSchemas registeredXmlSchemas, 
			ICollection<string> xmlFileExtensions, 
			XmlSchemaFileAssociations fileAssociations,
			IXmlSchemaCompletionDataFactory factory)
		{
			BuildUI();
			editor = new RegisteredXmlSchemasEditor(registeredXmlSchemas, xmlFileExtensions, fileAssociations, this, factory);
		}

		public object Owner { get; set; }

		public object Control
		{
			get { return this; }
		}

		public void LoadOptions()
		{
			editor.LoadOptions();
		}

		public bool SaveOptions()
		{
			return editor.SaveOptions();
		}

		public int XmlSchemaListItemCount
		{
			get { return schemaListBox.Items.Count; }
		}

		public int SelectedXmlSchemaListItemIndex
		{
			get { return schemaListBox.SelectedIndex; }
			set { schemaListBox.SelectedIndex = value; }
		}

		public int SelectedXmlSchemaFileAssociationListItemIndex
		{
			get { return fileExtensionComboBox.SelectedIndex; }
			set { fileExtensionComboBox.SelectedIndex = value; }
		}

		public int XmlSchemaFileAssociationListItemCount
		{
			get { return fileExtensionComboBox.Items.Count; }
		}

		public bool RemoveSchemaButtonEnabled
		{
			get { return removeSchemaButton.IsEnabled; }
			set { removeSchemaButton.IsEnabled = value; }
		}

		public XmlSchemaListItem GetXmlSchemaListItem(int index)
		{
			return (XmlSchemaListItem)schemaListBox.Items[index];
		}

		public int AddXmlSchemaListItem(XmlSchemaListItem schemaListItem)
		{
			schemaListBox.Items.Add(schemaListItem);
			return schemaListBox.Items.Count - 1;
		}

		public void AddXmlSchemaListSortDescription(string propertyName, ListSortDirection sortDirection)
		{
		}

		public XmlSchemaListItem GetSelectedXmlSchemaListItem()
		{
			return (XmlSchemaListItem)schemaListBox.SelectedItem;
		}

		public void RemoveXmlSchemaListItem(XmlSchemaListItem schemaListItem)
		{
			schemaListBox.Items.Remove(schemaListItem);
		}

		public void RefreshXmlSchemaListItems()
		{
		}

		public XmlSchemaFileAssociationListItem GetXmlSchemaFileAssociationListItem(int index)
		{
			return (XmlSchemaFileAssociationListItem)fileExtensionComboBox.Items[index];
		}

		public XmlSchemaFileAssociationListItem GetSelectedXmlSchemaFileAssociationListItem()
		{
			return (XmlSchemaFileAssociationListItem)fileExtensionComboBox.SelectedItem;
		}

		public int AddXmlSchemaFileAssociationListItem(XmlSchemaFileAssociationListItem schemaFileAssociationListItem)
		{
			fileExtensionComboBox.Items.Add(schemaFileAssociationListItem);
			return fileExtensionComboBox.Items.Count - 1;
		}

		public void AddXmlSchemaFileAssociationListSortDescription(string propertyName, ListSortDirection sortDirection)
		{
		}

		public string GetSelectedSchemaNamespace()
		{
			return schemaNamespaceTextBox.Text;
		}

		public void SetSelectedSchemaNamespace(string schemaNamespace)
		{
			schemaNamespaceTextBox.Text = schemaNamespace;
		}

		public string GetSelectedSchemaNamespacePrefix()
		{
			return namespacePrefixTextBox.Text;
		}

		public void SetSelectedSchemaNamespacePrefix(string namespacePrefix)
		{
			namespacePrefixTextBox.Text = namespacePrefix;
		}

		public bool? ShowDialog(object dialog)
		{
			if (dialog is SelectXmlSchemaWindow schemaWindow)
				return schemaWindow.ShowDialog();
			return null;
		}

		public Task<bool?> ShowFileDialogAsync(OpenFileDialog openFileDialog)
		{
			return openFileDialog.ShowDialogAsync();
		}

		public void ShowErrorFormatted(string format, string parameter)
		{
			MessageService.ShowErrorFormatted(format, parameter);
		}

		public void ShowExceptionError(Exception ex, string message)
		{
			MessageService.ShowException(ex, message);
		}

		public void ShowError(string message)
		{
			MessageService.ShowError(message);
		}

		public void ScrollXmlSchemaListItemIntoView(XmlSchemaListItem schemaListItem)
		{
			schemaListBox.ScrollIntoView(schemaListItem);
		}

		ListBox schemaListBox;
		ComboBox fileExtensionComboBox;
		TextBox schemaNamespaceTextBox;
		TextBox namespacePrefixTextBox;
		Button removeSchemaButton;
		Button addSchemaButton;
		Button changeSchemaButton;

		void BuildUI()
		{
			schemaListBox = new ListBox
			{
				Height = 150,
				SelectionMode = SelectionMode.Single,
			};
			schemaListBox.SelectionChanged += SchemaListBoxSelectionChanged;

			fileExtensionComboBox = new ComboBox();
			fileExtensionComboBox.SelectionChanged += FileExtensionComboBoxSelectionChanged;

			addSchemaButton = new Button { Content = "Add Schema from File System" };
			addSchemaButton.Click += AddSchemaButtonClick;

			removeSchemaButton = new Button { Content = "Remove Schema", IsEnabled = false };
			removeSchemaButton.Click += RemoveSchemaButtonClick;

			schemaNamespaceTextBox = new TextBox();
			namespacePrefixTextBox = new TextBox();
			namespacePrefixTextBox.TextChanged += NamespacePrefixTextBoxTextChanged;

			changeSchemaButton = new Button { Content = "Change Schema Association" };
			changeSchemaButton.Click += ChangeSchemaButtonClick;

			var headerPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
			headerPanel.Children.Add(new TextBlock { Text = "File Extension:" });
			headerPanel.Children.Add(fileExtensionComboBox);

			var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
			buttonPanel.Children.Add(addSchemaButton);
			buttonPanel.Children.Add(removeSchemaButton);

			var schemaPanel = new StackPanel { Spacing = 4 };
			schemaPanel.Children.Add(headerPanel);
			schemaPanel.Children.Add(new TextBlock { Text = "Schemas:" });
			schemaPanel.Children.Add(schemaListBox);
			schemaPanel.Children.Add(buttonPanel);

			var namespacePanel = new StackPanel { Spacing = 4 };
			namespacePanel.Children.Add(new TextBlock { Text = "Namespace:" });
			namespacePanel.Children.Add(schemaNamespaceTextBox);
			namespacePanel.Children.Add(new TextBlock { Text = "Prefix:" });
			namespacePanel.Children.Add(namespacePrefixTextBox);
			namespacePanel.Children.Add(changeSchemaButton);

			var root = new StackPanel { Spacing = 8, Margin = new Thickness(8) };
			root.Children.Add(schemaPanel);
			root.Children.Add(namespacePanel);

			Content = root;
		}

		void FileExtensionComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			editor.XmlSchemaFileAssociationFileExtensionSelectionChanged();
		}

		void SchemaListBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			editor.SchemaListSelectionChanged();
		}

		void AddSchemaButtonClick(object sender, RoutedEventArgs e)
		{
			_ = AddSchemaAsync();
		}

		async Task AddSchemaAsync()
		{
			await editor.AddSchemaFromFileSystemAsync();
		}

		void RemoveSchemaButtonClick(object sender, RoutedEventArgs e)
		{
			editor.RemoveSelectedSchema();
		}

		void NamespacePrefixTextBoxTextChanged(object sender, TextChangedEventArgs e)
		{
			editor.SchemaNamespacePrefixChanged();
		}

		void ChangeSchemaButtonClick(object sender, RoutedEventArgs e)
		{
			editor.ChangeSchemaAssociation();
		}
	}
}
