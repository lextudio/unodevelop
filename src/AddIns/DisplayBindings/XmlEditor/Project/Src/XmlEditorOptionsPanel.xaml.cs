using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using UnoDevelop.OptionPanels;

namespace ICSharpCode.XmlEditor
{
	public class XmlEditorOptionsPanel : OptionPanel
	{
		CheckBox showAttributesWhenFoldedCheckBox;
		CheckBox showSchemaAnnotationCheckBox;

		public XmlEditorOptionsPanel()
		{
			showAttributesWhenFoldedCheckBox = new CheckBox
			{
				Content = "${res:ICSharpCode.XmlEditor.XmlEditorOptionsPanel.ShowAttributesWhenFoldedLabel}",
				IsChecked = XmlEditorService.ShowAttributesWhenFolded,
			};
			showAttributesWhenFoldedCheckBox.Checked += (s, e) => XmlEditorService.ShowAttributesWhenFolded = true;
			showAttributesWhenFoldedCheckBox.Unchecked += (s, e) => XmlEditorService.ShowAttributesWhenFolded = false;

			var foldingGroup = new Border
			{
				BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush,
				BorderThickness = new Thickness(1),
				CornerRadius = new CornerRadius(4),
				Padding = new Thickness(8),
				Margin = new Thickness(5),
				Child = new StackPanel
				{
					Children =
					{
						new TextBlock
						{
							Text = "${res:ICSharpCode.XmlEditor.XmlEditorOptionsPanel.FoldingGroupLabel}",
							FontWeight = Microsoft.UI.Text.FontWeights.Bold,
							Margin = new Thickness(0, 0, 0, 4),
						},
						showAttributesWhenFoldedCheckBox,
					}
				}
			};

			showSchemaAnnotationCheckBox = new CheckBox
			{
				Content = "${res:ICSharpCode.XmlEditor.XmlEditorOptionsPanel.ShowSchemaAnnotationLabel}",
				IsChecked = XmlEditorService.ShowSchemaAnnotation,
			};
			showSchemaAnnotationCheckBox.Checked += (s, e) => XmlEditorService.ShowSchemaAnnotation = true;
			showSchemaAnnotationCheckBox.Unchecked += (s, e) => XmlEditorService.ShowSchemaAnnotation = false;

			var completionGroup = new Border
			{
				BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush,
				BorderThickness = new Thickness(1),
				CornerRadius = new CornerRadius(4),
				Padding = new Thickness(8),
				Margin = new Thickness(5),
				Child = new StackPanel
				{
					Children =
					{
						new TextBlock
						{
							Text = "${res:ICSharpCode.XmlEditor.XmlEditorOptionsPanel.XmlCompletionGroupLabel}",
							FontWeight = Microsoft.UI.Text.FontWeights.Bold,
							Margin = new Thickness(0, 0, 0, 4),
						},
						showSchemaAnnotationCheckBox,
					}
				}
			};

			Content = new StackPanel
			{
				Children =
				{
					foldingGroup,
					completionGroup,
				}
			};
		}
	}
}
