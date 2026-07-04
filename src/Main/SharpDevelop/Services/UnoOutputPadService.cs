using System;
using System.Collections.Generic;
using System.Linq;
using ICSharpCode.SharpDevelop.Gui;
using ICSharpCode.SharpDevelop.Workbench;

namespace UnoDevelop.Services;

// Faithful port of SharpDevelop's CompilerMessageView output-pad model.
// This service is the thread-safe category registry; the OutputPad control
// subscribes to its events and renders the currently selected category.
public sealed class UnoOutputPadService : IOutputPad
{
    private readonly object _sync = new();
    private readonly List<MessageViewCategory> _categories = new();
    private volatile MessageViewCategory _current;

    public MessageViewCategory BuildMessageViewCategory { get; }

    IOutputCategory IOutputPad.BuildCategory => BuildMessageViewCategory;

    public IReadOnlyList<MessageViewCategory> Categories
    {
        get { lock (_sync) return _categories.ToArray(); }
    }

    public IOutputCategory CurrentCategory
    {
        get => _current;
        set
        {
            var cat = (MessageViewCategory)(value ?? throw new ArgumentNullException(nameof(value)));
            _current = cat;
            CurrentCategoryChanged?.Invoke(cat);
        }
    }

    // Text events carry the originating category so the pad can decide whether the
    // change concerns the currently displayed category (mirrors ProcessAppendText).
    public event Action<MessageViewCategory, string>? TextAppended;
    public event Action<MessageViewCategory, string>? TextSet;
    public event Action<MessageViewCategory>? CategoryAdded;
    public event Action<MessageViewCategory>? CurrentCategoryChanged;
    public event Action? BringToFrontRequested;

    public UnoOutputPadService()
    {
        BuildMessageViewCategory = CreateMessageViewCategory("Build");
        _current = BuildMessageViewCategory;
        lock (_sync) _categories.Add(BuildMessageViewCategory);
    }

    private MessageViewCategory CreateMessageViewCategory(string name)
    {
        var cat = new MessageViewCategory(name, name);
        cat.TextAppended += (s, e) => TextAppended?.Invoke((MessageViewCategory)s, e.Text);
        cat.TextSet += (s, e) => TextSet?.Invoke((MessageViewCategory)s, e.Text);
        return cat;
    }

    public IOutputCategory CreateCategory(string displayName)
        => GetOrCreateCategory(displayName);

    public IOutputCategory GetOrCreateCategory(string displayName)
    {
        lock (_sync)
        {
            var existing = _categories.FirstOrDefault(c => c.DisplayCategory == displayName);
            if (existing is not null)
                return existing;

            var cat = CreateMessageViewCategory(displayName);
            _categories.Add(cat);
            CategoryAdded?.Invoke(cat);
            return cat;
        }
    }

    public MessageViewCategory GetOrCreateMessageViewCategory(string displayName)
        => (MessageViewCategory)GetOrCreateCategory(displayName);

    public void SelectCategory(MessageViewCategory category)
        => CurrentCategory = category;

    public void RemoveCategory(IOutputCategory category)
    {
        lock (_sync)
            _categories.Remove(category as MessageViewCategory);
    }

    public void BringToFront() => BringToFrontRequested?.Invoke();
}
