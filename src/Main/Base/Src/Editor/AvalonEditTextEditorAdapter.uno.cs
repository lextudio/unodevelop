using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.Core;
using ICSharpCode.TypeSystem;
using ICSharpCode.SharpDevelop.Editor.CodeCompletion;
using ICSharpCode.SharpDevelop.Refactoring;
using TextEditor = ICSharpCode.AvalonEdit.TextEditor;

namespace ICSharpCode.SharpDevelop.Editor
{
    public class AvalonEditTextEditorAdapter : ITextEditor
    {
        private readonly TextEditor _editor;
        private readonly TextEditorCaret _caret;
        private readonly FileName _fileName;

        public ITextEditor PrimaryView => this;
        public TextEditor TextEditor => _editor;
        public IDocument Document => _editor.Document;
        public ITextEditorCaret Caret => _caret;
        public ITextEditorOptions Options { get; } = new DefaultOptions();
        public ILanguageBinding Language { get; } = new DefaultLanguageBinding();
        public int SelectionStart => _editor.SelectionStart;
        public int SelectionLength => _editor.SelectionLength;
        public string SelectedText { get => _editor.SelectedText; set => _editor.SelectedText = value; }
        public FileName FileName => _fileName;
        public ICompletionListWindow? ActiveCompletionWindow { get; private set; }
        public IInsightWindow? ActiveInsightWindow { get; private set; }
        public IList<IContextActionProvider> ContextActionProviders { get; } = new List<IContextActionProvider>();

        public event EventHandler? SelectionChanged;
        public event System.Windows.Input.KeyEventHandler? KeyPress { add { } remove { } }

        public AvalonEditTextEditorAdapter(object textEditorControl)
        {
            _editor = textEditorControl as TextEditor
                ?? throw new ArgumentException("Expected a UnoEdit TextEditor.", nameof(textEditorControl));
            _fileName = FileName.Create(_editor.Tag as string ?? string.Empty);
            _caret = new TextEditorCaret(_editor);
            _editor.TextArea.CaretOffsetChanged += (_, _) =>
            {
                _caret.RaiseLocationChanged();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            };
        }

        public static AvalonEditTextEditorAdapter CreateAvalonEditInstance()
        {
            SD.EditorControlService.CreateEditor(out object editor);
            return new AvalonEditTextEditorAdapter(editor);
        }

        public void Select(int selectionStart, int selectionLength) => _editor.Select(selectionStart, selectionLength);

        public void JumpTo(int line, int column)
        {
            var location = new ICSharpCode.AvalonEdit.Document.TextLocation(Math.Max(1, line), Math.Max(1, column));
            var offset = Document.GetOffset(location);
            _editor.Select(offset, 0);
            _editor.ScrollTo(line, column);
        }

        public ICompletionListWindow ShowCompletionWindow(ICompletionItemList data)
        {
            var window = new NoOpCompletionListWindow(data);
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(ActiveCompletionWindow, window))
                    ActiveCompletionWindow = null;
            };
            ActiveCompletionWindow = window;
            return window;
        }

        public IInsightWindow ShowInsightWindow(IEnumerable<IInsightItem> items)
        {
            var window = new NoOpInsightWindow(items);
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(ActiveInsightWindow, window))
                    ActiveInsightWindow = null;
            };
            ActiveInsightWindow = window;
            return window;
        }
        public IEnumerable<ISnippetCompletionItem> GetSnippets() => Array.Empty<ISnippetCompletionItem>();
        public object? GetService(Type serviceType)
        {
            if (serviceType.IsInstanceOfType(_editor))
            {
                return _editor;
            }

            return null;
        }

        sealed class TextEditorCaret : ITextEditorCaret
        {
            private readonly TextEditor _editor;

            public TextEditorCaret(TextEditor editor)
            {
                _editor = editor;
            }

            public int Offset
            {
                get => _editor.CaretOffset;
                set => _editor.CaretOffset = Math.Clamp(value, 0, _editor.Document.TextLength);
            }

            public int Line
            {
                get => Location.Line;
                set => Location = new ICSharpCode.AvalonEdit.Document.TextLocation(value, Column);
            }

            public int Column
            {
                get => Location.Column;
                set => Location = new ICSharpCode.AvalonEdit.Document.TextLocation(Line, value);
            }

            public ICSharpCode.AvalonEdit.Document.TextLocation Location
            {
                get => _editor.Document.GetLocation(Offset);
                set => Offset = _editor.Document.GetOffset(value);
            }

            public event EventHandler? LocationChanged;

            public void RaiseLocationChanged() => LocationChanged?.Invoke(this, EventArgs.Empty);
        }

        sealed class DefaultOptions : ITextEditorOptions
        {
            public string IndentationString => "\t";
            public bool AutoInsertBlockEnd => true;
            public bool ConvertTabsToSpaces => false;
            public int IndentationSize => 4;
            public int VerticalRulerColumn => 120;
            public bool UnderlineErrors => true;
            public string FontFamily => "Consolas";
            public double FontSize => 10.0;
            public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
        }

        sealed class DefaultLanguageBinding : ILanguageBinding
        {
            public IFormattingStrategy FormattingStrategy => DefaultFormattingStrategy.DefaultInstance;
            public IBracketSearcher BracketSearcher => DefaultBracketSearcher.DefaultInstance;
            public CodeGenerator CodeGenerator => CodeGenerator.DummyCodeGenerator;
            public System.CodeDom.Compiler.CodeDomProvider? CodeDomProvider => null;
            public ICodeCompletionBinding CreateCompletionBinding(string expressionToComplete, ICodeContext context) => null!;
            public object? GetService(Type serviceType) => null;
        }

        abstract class NoOpCompletionWindow : ICompletionWindow
        {
            public event EventHandler? Closed;

            public double Width { get; set; } = double.NaN;
            public double Height { get; set; } = double.NaN;
            public bool CloseAutomatically { get; set; } = true;
            public int StartOffset { get; set; }
            public int EndOffset { get; set; }

            public void Close()
            {
                Closed?.Invoke(this, EventArgs.Empty);
            }
        }

        sealed class NoOpCompletionListWindow : NoOpCompletionWindow, ICompletionListWindow
        {
            public NoOpCompletionListWindow(ICompletionItemList data)
            {
                if (data is null)
                    throw new ArgumentNullException(nameof(data));

                SelectedItem = data.SuggestedItem ?? data.Items.FirstOrDefault() ?? new DefaultCompletionItem(string.Empty);
            }

            public ICompletionItem SelectedItem { get; set; }
        }

        sealed class NoOpInsightWindow : NoOpCompletionWindow, IInsightWindow
        {
            public NoOpInsightWindow(IEnumerable<IInsightItem> items)
            {
                Items = items?.ToList() ?? throw new ArgumentNullException(nameof(items));
                SelectedItem = Items.FirstOrDefault() ?? new EmptyInsightItem();
            }

            public IList<IInsightItem> Items { get; }
            public IInsightItem SelectedItem { get; set; }

            public event EventHandler<TextChangeEventArgs>? DocumentChanged { add { } remove { } }
            public event EventHandler? SelectedItemChanged { add { } remove { } }
            public event EventHandler? CaretPositionChanged { add { } remove { } }

            sealed class EmptyInsightItem : IInsightItem
            {
                public object Header => string.Empty;
                public object Content => string.Empty;
                public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
            }
        }
    }
}
