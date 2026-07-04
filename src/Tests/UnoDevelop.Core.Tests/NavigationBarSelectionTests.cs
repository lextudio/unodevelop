using System.Collections.Generic;
using ICSharpCode.SharpDevelop.LanguageServices;
using NUnit.Framework;
using UnoDevelop;

namespace UnoDevelop.Core.Tests;

// Unit-tests the editor nav bar's caret -> outline-item matching (MainPage.FindContainingOutlineItem
// / IsWithinExtent) in isolation from the WinUI control tree - these are pure functions over
// DocumentOutlineNode spans, so no live editor/window is needed to catch regressions in "which
// item does the caret sitting at (line, column) belong to".
[TestFixture]
public sealed class NavigationBarSelectionTests
{
    private static MainPage.EditorViewContent.OutlineComboItem Item(string name, int startLine, int startColumn, int endLine, int endColumn)
    {
        var node = new DocumentOutlineNode(
            name,
            "Method",
            new TextSpan(new TextPosition(startLine, startColumn), new TextPosition(startLine, startColumn)),
            System.Array.Empty<DocumentOutlineNode>(),
            new TextSpan(new TextPosition(startLine, startColumn), new TextPosition(endLine, endColumn)));
        return MainPage.EditorViewContent.OutlineComboItem.Create(node);
    }

    [Test]
    public void IsWithinExtent_CaretInsideSpan_ReturnsTrue()
    {
        var extent = new TextSpan(new TextPosition(2, 1), new TextPosition(5, 1));
        Assert.That(MainPage.IsWithinExtent(extent, 3, 10), Is.True);
    }

    [Test]
    public void IsWithinExtent_CaretBeforeSpan_ReturnsFalse()
    {
        var extent = new TextSpan(new TextPosition(2, 1), new TextPosition(5, 1));
        Assert.That(MainPage.IsWithinExtent(extent, 1, 99), Is.False);
    }

    [Test]
    public void IsWithinExtent_CaretAfterSpan_ReturnsFalse()
    {
        var extent = new TextSpan(new TextPosition(2, 1), new TextPosition(5, 1));
        Assert.That(MainPage.IsWithinExtent(extent, 6, 1), Is.False);
    }

    [Test]
    public void IsWithinExtent_CaretExactlyAtStartOrEnd_ReturnsTrue()
    {
        var extent = new TextSpan(new TextPosition(2, 1), new TextPosition(5, 10));
        Assert.That(MainPage.IsWithinExtent(extent, 2, 1), Is.True);
        Assert.That(MainPage.IsWithinExtent(extent, 5, 10), Is.True);
    }

    [Test]
    public void FindContainingOutlineItem_PicksTheOneContainingCaret()
    {
        var items = new[]
        {
            Item("First", 1, 1, 5, 1),
            Item("Second", 6, 1, 10, 1),
        };

        var result = MainPage.FindContainingOutlineItem(items, 7, 3);

        Assert.That(result?.Name, Is.EqualTo("Second"));
    }

    [Test]
    public void FindContainingOutlineItem_CaretOutsideAllItems_ReturnsNull()
    {
        var items = new[]
        {
            Item("First", 1, 1, 5, 1),
            Item("Second", 10, 1, 15, 1),
        };

        var result = MainPage.FindContainingOutlineItem(items, 7, 1);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void FindContainingOutlineItem_NestedExtents_PicksInnermost()
    {
        // A local function's extent nested inside its containing method's - regression case for
        // the old "OrderBy start ascending, take last" tie-break, which had no principled way to
        // prefer the smaller/inner span over the larger/outer one that also contains the caret.
        var items = new[]
        {
            Item("ContainingMethod", 1, 1, 20, 1),
            Item("LocalFunction", 5, 1, 8, 1),
        };

        var result = MainPage.FindContainingOutlineItem(items, 6, 3);

        Assert.That(result?.Name, Is.EqualTo("LocalFunction"));
    }

    [Test]
    public void FindContainingOutlineItem_CaretAtSharedBoundary_PicksOwningSibling()
    {
        // "First" ends exactly where "Second" begins (line 5, column 1) - both extents contain
        // that point, and the caret should resolve to whichever sibling actually owns that
        // boundary rather than an order-dependent pick.
        var items = new[]
        {
            Item("First", 1, 1, 5, 1),
            Item("Second", 5, 1, 10, 1),
        };

        var result = MainPage.FindContainingOutlineItem(items, 5, 1);

        Assert.That(result, Is.Not.Null);
        Assert.That(new[] { "First", "Second" }, Does.Contain(result!.Name));
    }

    [Test]
    public void FindContainingOutlineItem_EmptyList_ReturnsNull()
    {
        var result = MainPage.FindContainingOutlineItem(System.Array.Empty<MainPage.EditorViewContent.OutlineComboItem>(), 1, 1);

        Assert.That(result, Is.Null);
    }
}
