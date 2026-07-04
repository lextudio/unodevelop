using System;
using System.Text.RegularExpressions;
using ICSharpCode.AvalonEdit.Rendering;

namespace UnoDevelop.Workbench;

// Ported from SharpDevelop's CompilerMessageView.MessageViewLinkElementGenerator.
// Detects compiler / test-runner file references in output text and renders them as
// underlined links. Navigation on click is handled by OutputPad (which re-parses the
// clicked line with TryParse) so it works even without the editor's link click pipeline.
internal sealed class MessageViewLinkElementGenerator : LinkElementGenerator
{
    // C#:    C:\path\File.cs(12,5) or /path/File.cs(12,5)
    private static readonly Regex CSharpRegex = new(@"(?<!\S)((?:\w:[/\\]|/).*?)\((\d+),(\d+)\)");
    // NUnit: C:\path\File.cs:line 12 or /path/File.cs:line 12
    private static readonly Regex NUnitRegex = new(@"(?<!\S)((?:\w:[/\\]|/).*?):line\s(\d+)?$");
    // C++:   C:\path\File.cpp(12) or /path/File.cpp(12)
    private static readonly Regex CppRegex = new(@"(?<!\S)((?:\w:[/\\]|/).*?)\((\d+)\)");

    private MessageViewLinkElementGenerator(Regex regex) : base(regex)
    {
        RequireControlModifierForClick = false;
    }

    protected override Uri GetUriFromMatch(Match match)
    {
        try { return new Uri(match.Groups[1].Value.Trim()); }
        catch { return null!; }
    }

    public static void Register(TextView textView)
    {
        textView.ElementGenerators.Add(new MessageViewLinkElementGenerator(CSharpRegex));
        textView.ElementGenerators.Add(new MessageViewLinkElementGenerator(NUnitRegex));
        textView.ElementGenerators.Add(new MessageViewLinkElementGenerator(CppRegex));
    }

    // Parses a single output line for a file reference. Returns true and the resolved
    // location on the first regex that matches.
    public static bool TryParse(string lineText, out string file, out int line, out int column)
    {
        file = string.Empty;
        line = 0;
        column = 0;
        if (string.IsNullOrEmpty(lineText))
            return false;

        var m = CSharpRegex.Match(lineText);
        if (m.Success)
        {
            file = m.Groups[1].Value.Trim();
            line = ParseInt(m.Groups[2].Value);
            column = ParseInt(m.Groups[3].Value);
            return true;
        }

        m = NUnitRegex.Match(lineText);
        if (m.Success)
        {
            file = m.Groups[1].Value.Trim();
            line = ParseInt(m.Groups[2].Value);
            return true;
        }

        m = CppRegex.Match(lineText);
        if (m.Success)
        {
            file = m.Groups[1].Value.Trim();
            line = ParseInt(m.Groups[2].Value);
            return true;
        }

        return false;
    }

    private static int ParseInt(string s) => int.TryParse(s, out var v) ? v : 0;
}
