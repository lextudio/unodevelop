using System;
using ICSharpCode.Core;
using ICSharpCode.SharpDevelop.Workbench;

namespace ICSharpCode.SharpDevelop.Gui;

[Flags]
public enum DefaultPadPositions
{
    None = 0,
    Right = 1,
    Left = 2,
    Bottom = 4,
    Top = 8,
    Hidden = 16,
}

public sealed class PadDescriptor : IDisposable
{
    private readonly AddIn? _addIn;
    private readonly Type? _padType;
    private readonly Func<object, IPadContent>? _contentAdapter;
    private IPadContent? _padContent;
    private bool _created;
    private string? _serviceInterfaceName;
    private Type? _serviceInterface;

    public PadDescriptor(string className, string title, string icon = "",
        DefaultPadPositions defaultPosition = DefaultPadPositions.Bottom,
        Func<IPadContent>? factory = null)
    {
        ClassName = className;
        Title = title;
        Icon = icon;
        DefaultPosition = defaultPosition;
        Factory = factory;
    }

    public PadDescriptor(Codon codon, Func<object, IPadContent>? contentAdapter = null)
    {
        if (codon is null)
            throw new ArgumentNullException(nameof(codon));

        _addIn = codon.AddIn;
        _contentAdapter = contentAdapter;
        Shortcut = codon.Properties["shortcut"];
        Category = codon.Properties["category"];
        Icon = codon.Properties["icon"];
        Title = codon.Properties["title"];
        ClassName = codon.Properties["class"];
        _serviceInterfaceName = codon.Properties["serviceInterface"];
        DefaultPosition = ParseDefaultPosition(codon.Properties["defaultPosition"]);
    }

    public PadDescriptor(Type padType, string title, string icon = "")
    {
        _padType = padType ?? throw new ArgumentNullException(nameof(padType));
        ClassName = padType.FullName ?? padType.Name;
        Title = title ?? throw new ArgumentNullException(nameof(title));
        Icon = icon ?? throw new ArgumentNullException(nameof(icon));
        Category = "none";
        Shortcut = string.Empty;
    }

    /// Backwards-compat alias used by UnoWorkbenchService.GetPad().
    public string ClassName { get; }

    /// Upstream-compatible alias used by SharpDevelop addin code.
    public string Class => ClassName;

    /// Display title shown in the tool-pane header.
    public string Title { get; }

    /// Optional icon resource name.
    public string Icon { get; }

    public string Category { get; set; } = "none";

    public string Shortcut { get; set; } = string.Empty;

    public Type? ServiceInterface
    {
        get
        {
            if (_serviceInterface is null && _addIn is not null && !string.IsNullOrEmpty(_serviceInterfaceName))
                _serviceInterface = _addIn.FindType(_serviceInterfaceName);

            return _serviceInterface;
        }
    }

    /// Where the pad should dock by default.
    public DefaultPadPositions DefaultPosition { get; set; }

    /// Optional factory; if null, PadContent must be set directly.
    public Func<IPadContent>? Factory { get; init; }

    public IPadContent? PadContent
    {
        get
        {
            CreatePad();
            return _padContent;
        }
        set => _padContent = value;
    }

    public void CreatePad()
    {
        if (_created) return;
        _created = true;
        if (Factory is not null)
        {
            _padContent = Factory();
            return;
        }

        object? instance = null;
        if (_addIn is not null)
            instance = _addIn.CreateObject(ClassName);
        else if (_padType is not null)
            instance = Activator.CreateInstance(_padType);

        if (instance is null)
            return;

        _padContent = instance as IPadContent
            ?? _contentAdapter?.Invoke(instance)
            ?? throw new InvalidOperationException($"{ClassName} does not implement {nameof(IPadContent)}.");
    }

    public void Dispose() => _padContent?.Dispose();

    public override string ToString() => $"[PadDescriptor {ClassName}]";

    private static DefaultPadPositions ParseDefaultPosition(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return DefaultPadPositions.Bottom;

        return Enum.TryParse(value, ignoreCase: true, out DefaultPadPositions result)
            ? result
            : DefaultPadPositions.Bottom;
    }
}
