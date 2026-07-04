using System;
using System.ComponentModel;
using ICSharpCode.AvalonEdit;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop;
using Microsoft.UI.Xaml.Media;
using SD = ICSharpCode.SharpDevelop.Editor;

namespace UnoDevelop.OptionPanels;

internal sealed class UnoCodeEditorOptions : TextEditorOptions, SD.ITextEditorOptions
{
    private static readonly Lazy<UnoCodeEditorOptions> _instance = new(() =>
        PropertyService.Get("UnoCodeEditorOptions", new UnoCodeEditorOptions()));

    public static UnoCodeEditorOptions Instance => _instance.Value;

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        PropertyService.Set("UnoCodeEditorOptions", this);
    }

    string _fontFamily = "Consolas";

    public string FontFamily
    {
        get => _fontFamily;
        set
        {
            if (_fontFamily != value)
            {
                _fontFamily = value;
                OnPropertyChanged(nameof(FontFamily));
            }
        }
    }

    double _fontSize = 13.0;

    [DefaultValue(13.0)]
    public double FontSize
    {
        get => _fontSize;
        set
        {
            if (_fontSize != value)
            {
                _fontSize = value;
                OnPropertyChanged(nameof(FontSize));
            }
        }
    }

    bool _showLineNumbers = true;

    [DefaultValue(true)]
    public bool ShowLineNumbers
    {
        get => _showLineNumbers;
        set
        {
            if (_showLineNumbers != value)
            {
                _showLineNumbers = value;
                OnPropertyChanged(nameof(ShowLineNumbers));
            }
        }
    }

    bool _wordWrap;

    [DefaultValue(false)]
    public bool WordWrap
    {
        get => _wordWrap;
        set
        {
            if (_wordWrap != value)
            {
                _wordWrap = value;
                OnPropertyChanged(nameof(WordWrap));
            }
        }
    }

    bool _enableFolding = true;

    [DefaultValue(true)]
    public bool EnableFolding
    {
        get => _enableFolding;
        set
        {
            if (_enableFolding != value)
            {
                _enableFolding = value;
                OnPropertyChanged(nameof(EnableFolding));
            }
        }
    }

    bool _enableChangeMarkerMargin = true;

    [DefaultValue(true)]
    public bool EnableChangeMarkerMargin
    {
        get => _enableChangeMarkerMargin;
        set
        {
            if (_enableChangeMarkerMargin != value)
            {
                _enableChangeMarkerMargin = value;
                OnPropertyChanged(nameof(EnableChangeMarkerMargin));
            }
        }
    }

    bool _showHiddenDefinitions;

    [DefaultValue(false)]
    public bool ShowHiddenDefinitions
    {
        get => _showHiddenDefinitions;
        set
        {
            if (_showHiddenDefinitions != value)
            {
                _showHiddenDefinitions = value;
                OnPropertyChanged(nameof(ShowHiddenDefinitions));
            }
        }
    }

    bool _useSmartIndentation = true;

    [DefaultValue(true)]
    public bool UseSmartIndentation
    {
        get => _useSmartIndentation;
        set
        {
            if (_useSmartIndentation != value)
            {
                _useSmartIndentation = value;
                OnPropertyChanged(nameof(UseSmartIndentation));
            }
        }
    }

    bool _mouseWheelZoom = true;

    [DefaultValue(true)]
    public bool MouseWheelZoom
    {
        get => _mouseWheelZoom;
        set
        {
            if (_mouseWheelZoom != value)
            {
                _mouseWheelZoom = value;
                OnPropertyChanged(nameof(MouseWheelZoom));
            }
        }
    }

    bool _hideCursorWhileTyping;

    [DefaultValue(false)]
    public new bool HideCursorWhileTyping
    {
        get => _hideCursorWhileTyping;
        set
        {
            if (_hideCursorWhileTyping != value)
            {
                _hideCursorWhileTyping = value;
                OnPropertyChanged(nameof(HideCursorWhileTyping));
            }
        }
    }

    bool _ctrlClickGoToDefinition = true;

    [DefaultValue(true)]
    public bool CtrlClickGoToDefinition
    {
        get => _ctrlClickGoToDefinition;
        set
        {
            if (_ctrlClickGoToDefinition != value)
            {
                _ctrlClickGoToDefinition = value;
                OnPropertyChanged(nameof(CtrlClickGoToDefinition));
            }
        }
    }

    bool _autoInsertBlockEnd = true;

    [DefaultValue(true)]
    public bool AutoInsertBlockEnd
    {
        get => _autoInsertBlockEnd;
        set
        {
            if (_autoInsertBlockEnd != value)
            {
                _autoInsertBlockEnd = value;
                OnPropertyChanged(nameof(AutoInsertBlockEnd));
            }
        }
    }

    bool _underlineErrors = true;

    [DefaultValue(true)]
    public bool UnderlineErrors
    {
        get => _underlineErrors;
        set
        {
            if (_underlineErrors != value)
            {
                _underlineErrors = value;
                OnPropertyChanged(nameof(UnderlineErrors));
            }
        }
    }

    bool _highlightBrackets = true;

    [DefaultValue(true)]
    public bool HighlightBrackets
    {
        get => _highlightBrackets;
        set
        {
            if (_highlightBrackets != value)
            {
                _highlightBrackets = value;
                OnPropertyChanged(nameof(HighlightBrackets));
            }
        }
    }

    bool _highlightSymbol = true;

    [DefaultValue(true)]
    public bool HighlightSymbol
    {
        get => _highlightSymbol;
        set
        {
            if (_highlightSymbol != value)
            {
                _highlightSymbol = value;
                OnPropertyChanged(nameof(HighlightSymbol));
            }
        }
    }

    bool _enableAnimations = true;

    [DefaultValue(true)]
    public bool EnableAnimations
    {
        get => _enableAnimations;
        set
        {
            if (_enableAnimations != value)
            {
                _enableAnimations = value;
                OnPropertyChanged(nameof(EnableAnimations));
            }
        }
    }

    public void ApplyTo(ICSharpCode.AvalonEdit.TextEditor editor)
    {
        editor.Options = this;
        editor.ShowLineNumbers = ShowLineNumbers;
        editor.WordWrap = WordWrap;
        editor.ShowFoldMargin = EnableFolding;
        editor.EditorFontFamily = new FontFamily(FontFamily);
        editor.EditorFontSize = FontSize;
    }

    int VerticalRulerColumn
    {
        get => ColumnRulerPosition;
        set => ColumnRulerPosition = value;
    }

    int SD.ITextEditorOptions.VerticalRulerColumn => VerticalRulerColumn;
    string SD.ITextEditorOptions.FontFamily => FontFamily;
    double SD.ITextEditorOptions.FontSize => FontSize;
    bool SD.ITextEditorOptions.UnderlineErrors => UnderlineErrors;
    bool SD.ITextEditorOptions.AutoInsertBlockEnd => AutoInsertBlockEnd;
    bool SD.ITextEditorOptions.ConvertTabsToSpaces => ConvertTabsToSpaces;
    int SD.ITextEditorOptions.IndentationSize => IndentationSize;
    string SD.ITextEditorOptions.IndentationString => IndentationString;
}
