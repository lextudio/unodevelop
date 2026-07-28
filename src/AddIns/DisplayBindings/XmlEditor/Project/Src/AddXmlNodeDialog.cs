using System;
using System.Collections.Generic;
using System.Xml;
using ICSharpCode.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ICSharpCode.XmlEditor
{
	public class AddXmlNodeDialog : IAddXmlNodeDialog
	{
		ListBox namesListBox;
		TextBlock customNameTextBoxLabel;
		TextBox customNameTextBox;
		TextBlock errorTextBlock;
		ContentDialog dialog;
		bool result;

		public AddXmlNodeDialog(string[] names)
		{
			CreateDialog();
			InitStrings();
			if (names.Length > 0)
				AddNames(names);
			else
				RemoveNamesListBox();
		}

		public string CustomNameLabelText
		{
			get { return customNameTextBoxLabel.Text; }
			set { customNameTextBoxLabel.Text = value; }
		}

		public string[] GetNames()
		{
			var names = new List<string>();
			foreach (string name in namesListBox.SelectedItems)
				names.Add(name);
			string customName = customNameTextBox.Text.Trim();
			if (customName.Length > 0)
				names.Add(customName);
			return names.ToArray();
		}

		public string Title
		{
			get { return dialog.Title as string ?? ""; }
			set { dialog.Title = value; }
		}

		public AddXmlNodeDialogResult ShowDialog()
		{
			var r = dialog.ShowAsync().GetAwaiter().GetResult();
			return result ? AddXmlNodeDialogResult.OK : AddXmlNodeDialogResult.Cancel;
		}

		public void Dispose() { }

		void InitStrings()
		{
			dialog.PrimaryButtonText = StringParser.Parse("${res:Global.OKButtonText}");
			dialog.SecondaryButtonText = StringParser.Parse("${res:Global.CancelButtonText}");
		}

		void CreateDialog()
		{
			namesListBox = new ListBox { SelectionMode = SelectionMode.Extended };
			namesListBox.SelectionChanged += NamesListBoxSelectionChanged;

			customNameTextBoxLabel = new TextBlock
			{
				Text = "Custom:",
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(6, 0, 6, 0)
			};
			customNameTextBox = new TextBox { Margin = new Thickness(0, 0, 6, 0) };
			customNameTextBox.TextChanged += CustomNameTextBoxTextChanged;

			errorTextBlock = new TextBlock
			{
				Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red),
				Margin = new Thickness(6, 0, 6, 6)
			};

			var customNamePanel = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Margin = new Thickness(6)
			};
			customNamePanel.Children.Add(customNameTextBoxLabel);
			customNamePanel.Children.Add(customNameTextBox);

			var content = new StackPanel
			{
				Width = 289,
				Height = 244
			};
			content.Children.Add(namesListBox);
			content.Children.Add(customNamePanel);
			content.Children.Add(errorTextBlock);

			dialog = new ContentDialog
			{
				Title = "AddXmlNodeDialog",
				Content = content,
				DefaultButton = ContentDialogButton.Primary,
				IsPrimaryButtonEnabled = false,
			};
			dialog.PrimaryButtonClick += (s, e) =>
			{
				if (!IsOkButtonEnabled)
					e.Cancel = true;
				else
					result = true;
			};
		}

		void NamesListBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			UpdateOkButtonState();
		}

		void CustomNameTextBoxTextChanged(object sender, TextChangedEventArgs e)
		{
			UpdateOkButtonState();
		}

		void UpdateOkButtonState()
		{
			dialog.IsPrimaryButtonEnabled = IsOkButtonEnabled;
		}

		bool IsItemSelected
		{
			get { return namesListBox.SelectedIndex >= 0; }
		}

		bool IsOkButtonEnabled
		{
			get { return IsItemSelected || ValidateCustomName(); }
		}

		bool ValidateCustomName()
		{
			string name = customNameTextBox.Text.Trim();
			if (name.Length > 0)
			{
				try
				{
					VerifyName(name);
					errorTextBlock.Text = string.Empty;
					return true;
				}
				catch (XmlException ex)
				{
					errorTextBlock.Text = ex.Message;
				}
			}
			return false;
		}

		static void VerifyName(string name)
		{
			string[] parts = name.Split(new char[] { ':' }, 2);
			if (parts.Length == 1)
			{
				XmlConvert.VerifyName(name);
				return;
			}
			string firstPart = parts[0].Trim();
			string secondPart = parts[1].Trim();
			if (firstPart.Length > 0 && secondPart.Length > 0)
			{
				XmlConvert.VerifyNCName(firstPart);
				XmlConvert.VerifyNCName(secondPart);
			}
			else
			{
				XmlConvert.VerifyNCName(name);
			}
		}

		void AddNames(string[] names)
		{
			var sorted = new List<string>(names);
			sorted.Sort(StringComparer.Ordinal);
			foreach (string name in sorted)
				namesListBox.Items.Add(name);
		}

		void RemoveNamesListBox()
		{
			namesListBox.Visibility = Visibility.Collapsed;
		}
	}
}
