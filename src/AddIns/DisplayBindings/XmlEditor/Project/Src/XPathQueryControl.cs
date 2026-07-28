using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using System.Xml;
using System.Xml.XPath;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Editor;
using ICSharpCode.SharpDevelop.Gui;

namespace ICSharpCode.XmlEditor
{
	public class XPathQueryNamespaceRow
	{
		public string Prefix { get; set; } = string.Empty;
		public string Uri { get; set; } = string.Empty;
	}

	public class XPathQueryResultRow
	{
		public string Match { get; set; } = string.Empty;
		public string Line { get; set; } = string.Empty;
		public object Tag { get; set; }
	}

	public class XPathQueryControl : UserControl, IMementoCapable
	{
		const string NamespacesProperty = "Namespaces";
		const string XPathComboBoxTextProperty = "XPathQuery.LastQuery";
		const string XPathComboBoxItemsProperty = "XPathQuery.History";

		string fileName = string.Empty;
		const int xpathQueryHistoryLimit = 20;
		bool ignoreXPathTextChanges;

		enum MoveCaret
		{
			ByJumping = 1,
			ByScrolling = 2
		}

		readonly ObservableCollection<XPathQueryNamespaceRow> namespaceRows = new ObservableCollection<XPathQueryNamespaceRow>();
		readonly ObservableCollection<XPathQueryResultRow> resultRows = new ObservableCollection<XPathQueryResultRow>();
		readonly ObservableCollection<string> xpathHistory = new ObservableCollection<string>();

		TextBlock xPathLabel;
		ComboBox xpathComboBox;
		Button queryButton;
		TabView tabControl;
		TabViewItem xPathResultsTabPage;
		TabViewItem namespacesTabPage;
		ListView xPathResultsListView;
		ListView namespacesListView;

		public XPathQueryControl()
		{
			BuildUI();
			InitStrings();
		}

		public void AddNamespace(string prefix, string uri)
		{
			namespaceRows.Add(new XPathQueryNamespaceRow { Prefix = prefix, Uri = uri });
		}

		public XmlNamespaceCollection GetNamespaces()
		{
			var namespaces = new XmlNamespaceCollection();
			foreach (var row in namespaceRows)
			{
				string prefix = row.Prefix ?? string.Empty;
				string uri = row.Uri ?? string.Empty;
				if (prefix.Length == 0 && uri.Length == 0)
				{
				}
				else
				{
					namespaces.Add(new XmlNamespace(prefix, uri));
				}
			}
			return namespaces;
		}

		public Properties CreateMemento()
		{
			var properties = new Properties();
			SaveNamespaces(properties);
			SaveXPathQueryHistory(properties);
			return properties;
		}

		void SaveNamespaces(Properties properties)
		{
			properties.SetList(NamespacesProperty, GetNamespaceStringArray());
		}

		void SaveXPathQueryHistory(Properties properties)
		{
			properties.Set(XPathComboBoxTextProperty, xpathComboBox.Text);
			properties.SetList(XPathComboBoxItemsProperty, GetXPathHistory());
		}

		public void SetMemento(Properties properties)
		{
			ignoreXPathTextChanges = true;
			try
			{
				LoadNamespaces(properties);
				LoadXPathQueryHistory(properties);
			}
			finally
			{
				ignoreXPathTextChanges = false;
			}
		}

		void LoadNamespaces(Properties properties)
		{
			var namespaces = properties.GetList<string>(NamespacesProperty);
			foreach (string ns in namespaces)
			{
				XmlNamespace xmlNamespace = XmlNamespace.FromString(ns);
				AddNamespace(xmlNamespace.Prefix, xmlNamespace.Name);
			}
		}

		void LoadXPathQueryHistory(Properties properties)
		{
			xpathComboBox.Text = properties.Get(XPathComboBoxTextProperty, string.Empty);
			var xpaths = properties.GetList<string>(XPathComboBoxItemsProperty);
			foreach (string xpath in xpaths)
				xpathHistory.Add(xpath);
		}

		public void ActiveWindowChanged()
		{
			UpdateQueryButtonState();
		}

		void BuildUI()
		{
			xPathLabel = new TextBlock
			{
				Text = "XPath:",
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(3)
			};
			xpathComboBox = new ComboBox { IsEditable = true, ItemsSource = xpathHistory, Margin = new Thickness(3) };
			xpathComboBox.TextSubmitted += (s, e) => XPathComboBoxTextChanged(s, null);
			xpathComboBox.KeyDown += XPathComboBoxKeyDown;

			queryButton = new Button { Content = "Query", IsEnabled = false, Width = 70, Margin = new Thickness(3) };
			queryButton.Click += QueryButtonClick;

			var topPanel = new Grid();
			topPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			topPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			topPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			Grid.SetColumn(xPathLabel, 0);
			Grid.SetColumn(xpathComboBox, 1);
			Grid.SetColumn(queryButton, 2);
			topPanel.Children.Add(xPathLabel);
			topPanel.Children.Add(xpathComboBox);
			topPanel.Children.Add(queryButton);

			xPathResultsListView = new ListView { ItemsSource = resultRows };
			xPathResultsListView.ItemTemplate = CreateResultsDataTemplate();
			xPathResultsListView.DoubleTapped += (s, e) => JumpToResultLocation();
			xPathResultsListView.SelectionChanged += (s, e) => ScrollToResultLocation();

			xPathResultsTabPage = new TabViewItem { Header = "Results" };
			xPathResultsTabPage.Content = xPathResultsListView;

			namespacesListView = new ListView { ItemsSource = namespaceRows };
			namespacesListView.ItemTemplate = CreateNamespaceDataTemplate();

			namespacesTabPage = new TabViewItem { Header = "Namespaces" };
			namespacesTabPage.Content = namespacesListView;

			tabControl = new TabView
			{
				TabWidthMode = TabViewWidthMode.SizeToContent,
				IsAddTabButtonVisible = false,
			};
			tabControl.TabItems.Add(xPathResultsTabPage);
			tabControl.TabItems.Add(namespacesTabPage);

			var root = new Grid();
			root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
			root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
			Grid.SetRow(topPanel, 0);
			Grid.SetRow(tabControl, 1);
			root.Children.Add(topPanel);
			root.Children.Add(tabControl);

			Content = root;
		}

		DataTemplate CreateResultsDataTemplate()
		{
			return new DataTemplate(() =>
			{
				var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
				var matchText = new TextBlock();
				matchText.SetBinding(TextBlock.TextProperty, new Binding { Path = new PropertyPath("Match") });
				var lineText = new TextBlock();
				lineText.SetBinding(TextBlock.TextProperty, new Binding { Path = new PropertyPath("Line") });
				stack.Children.Add(matchText);
				stack.Children.Add(lineText);
				return stack;
			});
		}

		DataTemplate CreateNamespaceDataTemplate()
		{
			return new DataTemplate(() =>
			{
				var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
				var prefixText = new TextBlock();
				prefixText.SetBinding(TextBlock.TextProperty, new Binding { Path = new PropertyPath("Prefix") });
				var uriText = new TextBlock();
				uriText.SetBinding(TextBlock.TextProperty, new Binding { Path = new PropertyPath("Uri") });
				stack.Children.Add(prefixText);
				stack.Children.Add(uriText);
				return stack;
			});
		}

		void XPathComboBoxTextChanged(object sender, object e)
		{
			if (!ignoreXPathTextChanges)
				UpdateQueryButtonState();
		}

		void UpdateQueryButtonState()
		{
			queryButton.IsEnabled = IsXPathQueryEntered && XmlDisplayBinding.XmlViewContentActive;
		}

		bool IsXPathQueryEntered
		{
			get { return xpathComboBox.Text.Length > 0; }
		}

		void QueryButtonClick(object sender, RoutedEventArgs e)
		{
			RunXPathQuery();
		}

		void RunXPathQuery()
		{
			XmlView xmlView = XmlView.ActiveXmlView;
			if (xmlView == null)
				return;

			try
			{
				fileName = xmlView.File.FileName;

				ClearResults();
				XPathNodeTextMarker.RemoveMarkers(xmlView.TextEditor.Document);

				XPathQuery query = new XPathQuery(xmlView.TextEditor, GetNamespaces());
				XPathNodeMatch[] nodes = query.FindNodes(xpathComboBox.Text);
				if (nodes.Length > 0)
				{
					AddXPathResults(nodes);
					XPathNodeTextMarker marker = new XPathNodeTextMarker(xmlView.TextEditor.Document);
					marker.AddMarkers(nodes);
				}
				else
				{
					AddNoXPathResult();
				}
				AddXPathToHistory();
			}
			catch (XPathException xpathEx)
			{
				AddErrorResult(xpathEx);
			}
			catch (XmlException xmlEx)
			{
				AddErrorResult(xmlEx);
			}
			finally
			{
				BringResultsTabToFront();
			}
		}

		void ClearResults()
		{
			resultRows.Clear();
		}

		void BringResultsTabToFront()
		{
			tabControl.SelectedIndex = 0;
		}

		void AddXPathResults(XPathNodeMatch[] nodes)
		{
			foreach (XPathNodeMatch node in nodes)
			{
				string line = node.HasLineInfo() ? (node.LineNumber + 1).ToString(CultureInfo.InvariantCulture) : string.Empty;
				resultRows.Add(new XPathQueryResultRow { Match = node.DisplayValue, Line = line, Tag = node });
			}
		}

		void AddNoXPathResult()
		{
			resultRows.Add(new XPathQueryResultRow { Match = StringParser.Parse("${res:ICSharpCode.XmlEditor.XPathQueryPad.NoXPathResultsMessage}") });
		}

		void AddErrorResult(XmlException ex)
		{
			resultRows.Add(new XPathQueryResultRow { Match = ex.Message, Line = ex.LineNumber.ToString(CultureInfo.InvariantCulture), Tag = ex });
		}

		void AddErrorResult(XPathException ex)
		{
			string message = string.Concat(StringParser.Parse("${res:ICSharpCode.XmlEditor.XPathQueryPad.XPathLabel}"), " ", ex.Message);
			resultRows.Add(new XPathQueryResultRow { Match = message, Tag = ex });
		}

		void InitStrings()
		{
			queryButton.Content = StringParser.Parse("${res:ICSharpCode.XmlEditor.XPathQueryPad.QueryButton}");
			xPathLabel.Text = StringParser.Parse("${res:ICSharpCode.XmlEditor.XPathQueryPad.XPathLabel}");
			xPathResultsTabPage.Header = StringParser.Parse("${res:ICSharpCode.XmlEditor.XPathQueryPad.ResultsTab}");
			namespacesTabPage.Header = StringParser.Parse("${res:ICSharpCode.XmlEditor.XPathQueryPad.NamespacesTab}");
		}

		void JumpToResultLocation()
		{
			MoveCaretToResultLocation(MoveCaret.ByJumping);
		}

		void ScrollToResultLocation()
		{
			MoveCaretToResultLocation(MoveCaret.ByScrolling);
		}

		void MoveCaretToResultLocation(MoveCaret moveCaret)
		{
			if (xPathResultsListView.SelectedItem is XPathQueryResultRow row)
			{
				XPathNodeMatch xPathNodeMatch = row.Tag as XPathNodeMatch;
				XPathException xpathException = row.Tag as XPathException;
				XmlException xmlException = row.Tag as XmlException;
				if (xPathNodeMatch != null)
					MoveCaretToXPathNodeMatch(moveCaret, xPathNodeMatch);
				else if (xmlException != null)
					MoveCaretToXmlException(moveCaret, xmlException);
				else if (xpathException != null && moveCaret == MoveCaret.ByJumping)
					xpathComboBox.Focus(FocusState.Programmatic);
			}
		}

		void MoveCaretToXPathNodeMatch(MoveCaret moveCaret, XPathNodeMatch node)
		{
			if (moveCaret == MoveCaret.ByJumping)
				JumpTo(fileName, node.LineNumber, node.LinePosition);
			else
				ScrollTo(fileName, node.LineNumber, node.LinePosition, node.Value.Length);
		}

		void MoveCaretToXmlException(MoveCaret moveCaret, XmlException ex)
		{
			int line = ex.LineNumber - 1;
			int column = ex.LinePosition - 1;
			if (moveCaret == MoveCaret.ByJumping)
				JumpTo(fileName, line, column);
			else
				ScrollTo(fileName, line, column);
		}

		static void JumpTo(string fileName, int line, int column)
		{
			FileService.JumpToFilePosition(fileName, line + 1, column + 1);
		}

		static void ScrollTo(string filename, int line, int column, int length)
		{
			XmlView view = XmlView.ForFileName(filename);
			if (view != null)
			{
				ITextEditor editor = view.TextEditor;
				if (editor == null) return;
				int corLine = Math.Min(line + 1, editor.Document.LineCount - 1);
				editor.JumpTo(corLine, column + 1);
				if (length > 0 && line < editor.Document.LineCount)
				{
					int offset = editor.Document.GetOffset(line + 1, column + 1);
					editor.Select(offset, length);
				}
			}
		}

		static void ScrollTo(string fileName, int line, int column)
		{
			ScrollTo(fileName, line, column, 0);
		}

		string[] GetNamespaceStringArray()
		{
			var namespaces = new List<string>();
			foreach (XmlNamespace ns in GetNamespaces())
				namespaces.Add(ns.ToString());
			return namespaces.ToArray();
		}

		string[] GetXPathHistory()
		{
			return new List<string>(xpathHistory).ToArray();
		}

		void AddXPathToHistory()
		{
			string newXPath = xpathComboBox.Text;
			if (!xpathHistory.Contains(newXPath))
			{
				xpathHistory.Insert(0, newXPath);
				if (xpathHistory.Count > xpathQueryHistoryLimit)
					xpathHistory.RemoveAt(xpathHistory.Count - 1);
			}
		}

		void XPathComboBoxKeyDown(object sender, KeyRoutedEventArgs e)
		{
			if (e.Key == VirtualKey.Enter)
				RunXPathQuery();
		}
	}
}
