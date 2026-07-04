using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using UnoEdit.Skia.Desktop.Controls;

namespace UnoDevelop.Controls;

/// <summary>
/// Left margin that shows red breakpoint dots and handles click-to-toggle.
/// Notifies DebugService via the BreakpointsChanged callback so it can
/// send a DAP setBreakpoints request.
/// </summary>
internal sealed class BreakpointMargin : Canvas
{
    private const double MarginWidth = 18;

    private TextEditor? _editor;
    private readonly HashSet<int> _breakpointLines = new();
    private int _currentLine; // 0 = none

    /// Called whenever the breakpoint set changes.
    /// Argument is the current file path + sorted line list.
    public Action<string, IReadOnlyList<int>>? BreakpointsChanged { get; set; }

    public BreakpointMargin()
    {
        Width = MarginWidth;
        Background = new SolidColorBrush(Color.FromArgb(255, 240, 240, 240));
        Tapped += OnTapped;
    }

    public void Attach(TextEditor editor)
    {
        _editor = editor;
        // Repaint when scroll position or document changes
        if (editor.TextArea?.TextView is { } tv)
        {
            tv.ScrollOffsetChanged += (_, _) => Redraw();
            tv.VisualLinesChanged += (_, _) => Redraw();
        }
        editor.Document.Changed += (_, _) => Redraw();
    }

    // ── Public breakpoint management ──────────────────────────────────────

    /// Replace the full set (e.g. after DebugService confirms verified lines).
    public void SetBreakpoints(IEnumerable<int> lines)
    {
        _breakpointLines.Clear();
        foreach (var l in lines) _breakpointLines.Add(l);
        Redraw();
    }

    /// Set the current execution arrow line (0 clears it).
    public void SetCurrentLine(int line)
    {
        _currentLine = line;
        Redraw();
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private void OnTapped(object sender, TappedRoutedEventArgs e)
    {
        if (_editor?.TextArea?.TextView is not { } tv) return;

        var y = e.GetPosition(this).Y + tv.ScrollOffset.Y;
        var line = tv.GetDocumentLineByVisualTop(y);
        if (line is null) return;

        var lineNumber = line.LineNumber;
        if (_breakpointLines.Contains(lineNumber))
            _breakpointLines.Remove(lineNumber);
        else
            _breakpointLines.Add(lineNumber);

        Redraw();
        NotifyChanged();
    }

    private void NotifyChanged()
    {
        var filePath = (_editor?.Tag as string) ?? string.Empty;
        BreakpointsChanged?.Invoke(filePath, _breakpointLines.OrderBy(l => l).ToList());
    }

    private void Redraw()
    {
        Children.Clear();
        if (_editor?.TextArea?.TextView is not { } tv) return;

        var lineHeight = tv.DefaultLineHeight;
        var scrollOffset = tv.ScrollOffset.Y;

        // Breakpoint dots
        foreach (var lineNumber in _breakpointLines)
        {
            double visualTop;
            try { visualTop = tv.GetVisualTopByDocumentLine(lineNumber) - scrollOffset; }
            catch { continue; }

            if (visualTop < -lineHeight || visualTop > ActualHeight + lineHeight) continue;

            var dot = new Ellipse
            {
                Width = 12,
                Height = 12,
                Fill = new SolidColorBrush(Color.FromArgb(255, 228, 20, 0)),
            };
            Canvas.SetLeft(dot, (MarginWidth - 12) / 2);
            Canvas.SetTop(dot, visualTop + (lineHeight - 12) / 2);
            Children.Add(dot);
        }

        // Current execution arrow (yellow)
        if (_currentLine > 0)
        {
            double visualTop;
            try { visualTop = tv.GetVisualTopByDocumentLine(_currentLine) - scrollOffset; }
            catch { return; }

            if (visualTop >= -lineHeight && visualTop <= ActualHeight + lineHeight)
            {
                var arrow = new Polygon
                {
                    Fill = new SolidColorBrush(Color.FromArgb(255, 255, 200, 0)),
                    Stroke = new SolidColorBrush(Color.FromArgb(255, 180, 140, 0)),
                    StrokeThickness = 1,
                    Points = new PointCollection
                    {
                        new Point(2, lineHeight * 0.5),
                        new Point(10, lineHeight * 0.2),
                        new Point(10, lineHeight * 0.8),
                    }
                };
                Canvas.SetLeft(arrow, 0);
                Canvas.SetTop(arrow, visualTop);
                Children.Add(arrow);
            }
        }
    }
}
