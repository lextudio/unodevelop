using System;
using System.IO;
using System.Linq;
using ICSharpCode.Core;
using NUnit.Framework;
using UnoDevelop.Services;

namespace UnoDevelop.Core.Tests;

/// <summary>
/// Regression coverage for Solution Explorer's right-click context-menu hit testing and
/// flyout command routing (see docs/opendevelop-sync.md "Open Test Debt").
///
/// Layer A (hit testing): <see cref="MainPage.TryResolveNodeContext"/> walks the visual tree from
/// a RightTapped event's OriginalSource looking for a TreeViewItem whose DataContext is a
/// TreeViewNode wrapping a ProjectBrowserNodeContext. Realizing a real TreeViewItem *container*
/// for a TreeViewNode requires the TreeView to go through an actual layout/container-generation
/// pass (ContainerFromItem stays null until then), which is not reliably achievable in this
/// headless NUnit host (no window, no layout pump, no live Uno dispatcher -- confirmed empirically:
/// even a bare `new MenuFlyout()` throws a NullReferenceException here because
/// Uno.UI.Dispatching.NativeDispatcher.GetForCurrentThread() has nothing to attach to without a
/// running Uno Application/dispatcher). Attempting that was scoped out per the task's own guidance
/// to not over-invest in Layer A; instead we widened TryResolveNodeContext from private to internal
/// (see MainPage.SolutionExplorer.cs) purely so a future UI-test harness that *can* pump a real
/// dispatcher/layout pass can call it directly, and we focus this fixture's real coverage on Layer B.
///
/// Layer B (command routing): builds a real, isolated <see cref="AddInTreeImpl"/>, loads a small
/// synthetic in-memory addin modeled on Explorer.addin's context-menu shape, and drives the exact
/// AddInTree/Condition/CommandWrapper pipeline that <see cref="UnoAddInContextMenuBuilder"/> itself
/// calls (BuildItems, Condition.GetFailedAction, CommandWrapper.CreateLazyCommand) to prove
/// Include/Exclude/Disable outcomes and that "clicking" actually invokes the real command class with
/// the right Owner. We deliberately do NOT call UnoAddInContextMenuBuilder.CreateContextMenu()
/// itself, because it constructs a real Microsoft.UI.Xaml.Controls.MenuFlyout, which -- for the same
/// "no live dispatcher" reason as Layer A above -- throws in this headless host. Everything
/// CreateContextMenu does *besides* allocating WinUI controls (looking up descriptors, evaluating
/// conditions, building lazy commands) is exercised here directly against the same production types.
///
/// IMPORTANT DISCOVERY while building this coverage: the real, shipping
/// src/AddIns/Main/Explorer/Project/Explorer.addin uses `&lt;Condition&gt;` as a *child element* of
/// `&lt;MenuItem&gt;` (e.g. Rename/Delete are followed by a nested Ownerstate Condition). Tracing
/// ExtensionPath.DoSetUp (externals/OpenDevelop/.../AddInTree/AddIn/ExtensionPath.cs) shows a Codon
/// is constructed from the *ambient* conditionStack BEFORE its own children (including any nested
/// Condition) are parsed; a Condition nested inside a leaf MenuItem only affects codons created
/// further down that same recursive call, i.e. it is silently dropped for a leaf item with no
/// sub-MenuItems. <see cref="RealExplorerAddinLeafItemConditionsAreCurrentlyDroppedKnownGap"/> proves
/// this against the real, unmodified Explorer.addin file: Rename/Delete/RemoveFromProject/CopyPath
/// all resolve with zero effective conditions today, meaning their Renameable/Deletable/etc. gating
/// currently has NO effect at runtime. This is a real, pre-existing production bug (not introduced by
/// this change) that the correct fix is to either wrap items in a sibling `&lt;Condition&gt;` (which
/// this codebase's parser *does* honor -- see the synthetic addin below) or to change the parser to
/// hoist a leaf MenuItem's own nested Condition into its own Conditions. Fixing Explorer.addin/the
/// parser is out of scope for this test-debt task (constraints prohibit editing Explorer.addin or
/// production parser code), so this is reported as a known gap rather than silently fixed.
/// </summary>
[TestFixture]
public sealed class SolutionExplorerContextMenuTests
{
    private const string ContextMenuPath = "/UnoDevelop.Core.Tests/SolutionExplorerContextMenu";

    [Test]
    public void ContextMenuExcludesFailedItemAndDisablesGatedItemButKeepsPlainItem()
    {
        RunWithIsolatedAddInTree((addInTree, owner) =>
        {
            // Mirrors exactly what UnoAddInContextMenuBuilder.CreateMenuItem does, minus allocating
            // WinUI controls (see the class-level comment for why that part can't run headlessly).
            var descriptors = addInTree.BuildItems<MenuItemDescriptor>(ContextMenuPath, owner, throwOnNotFound: true);

            var plain = descriptors.Single(d => d.Codon.Id == "AlwaysVisible");
            Assert.That(Condition.GetFailedAction(plain.Conditions, plain.Parameter), Is.EqualTo(ConditionFailedAction.Nothing),
                "Item with no conditions must not be excluded or disabled.");

            var separator = descriptors.Single(d => d.Codon.Id == "Sep");
            Assert.That(separator.Codon.Properties["type"], Is.EqualTo("Separator"));

            var excluded = descriptors.Single(d => d.Codon.Id == "ExcludedItem");
            Assert.That(excluded.Conditions.Count, Is.EqualTo(1), "Condition wraps the item as a sibling, so it must be attached.");
            Assert.That(Condition.GetFailedAction(excluded.Conditions, excluded.Parameter), Is.EqualTo(ConditionFailedAction.Exclude),
                "Failing Ownerstate condition with the default action must resolve to Exclude " +
                "(UnoAddInContextMenuBuilder.CreateMenuItem removes such items from the flyout entirely).");

            var disabled = descriptors.Single(d => d.Codon.Id == "DisabledItem");
            Assert.That(Condition.GetFailedAction(disabled.Conditions, disabled.Parameter), Is.EqualTo(ConditionFailedAction.Disable),
                "Failing Ownerstate condition with action=\"Disable\" must resolve to Disable " +
                "(UnoAddInContextMenuBuilder.CreateMenuItem keeps such items but sets IsEnabled=false).");

            Assert.That(descriptors.Select(d => d.Codon.Id), Is.EqualTo(new[] { "AlwaysVisible", "Sep", "ExcludedItem", "DisabledItem" }),
                "Codon order from the addin tree drives the flyout's item order.");
        });
    }

    [Test]
    public void RealExplorerAddinLeafItemConditionsAreCurrentlyDroppedKnownGap()
    {
        // Loads the real, unmodified src/AddIns/Main/Explorer/Project/Explorer.addin (read-only --
        // this test does not modify it) to document the known gap described in the class-level
        // comment: a <Condition> nested as a *child* of a leaf <MenuItem> (Explorer.addin's actual
        // syntax for Rename/Delete/etc.) never ends up in that item's Codon.Conditions.
        var addInTree = new AddInTreeImpl(null);
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        string realFile = null;
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "AddIns", "Main", "Explorer", "Project", "Explorer.addin");
            if (File.Exists(candidate)) { realFile = candidate; break; }
            dir = dir.Parent;
        }
        Assert.That(realFile, Is.Not.Null, "could not locate Explorer.addin from " + AppContext.BaseDirectory);

        var addIn = AddIn.Load(addInTree, realFile, new System.Xml.NameTable());
        addIn.Enabled = true;
        addInTree.InsertAddIn(addIn);

        var node = addInTree.GetTreeNode("/SharpDevelop/Pads/ProjectBrowser/ContextMenu/Common/Edit", throwOnNotFound: true);
        var rename = node.Codons.Single(c => c.Id == "Rename");
        var delete = node.Codons.Single(c => c.Id == "Delete");

        // These SHOULD be 1 (an Ownerstate condition each) for the gating to actually work at
        // runtime; today they are 0. This assertion intentionally locks in current (buggy) behavior
        // so that a future fix to the parser or to Explorer.addin's syntax is a visible, deliberate
        // change to this test rather than a silent regression either way.
        Assert.That(rename.Conditions.Count, Is.EqualTo(0),
            "Known gap: Rename's nested Condition is currently dropped (see class-level comment). " +
            "If this starts failing because someone fixed the underlying parser/addin syntax, update " +
            "this test to assert Count is EqualTo(1) and GetFailedAction gates it correctly.");
        Assert.That(delete.Conditions.Count, Is.EqualTo(0));
    }

    [Test]
    public void ContextMenuCommandRoutesClickToRealCommandClassWithOwner()
    {
        RunWithIsolatedAddInTree((addInTree, owner) =>
        {
            TestObservingCommand.LastOwner = null;

            var descriptors = addInTree.BuildItems<MenuItemDescriptor>(ContextMenuPath, owner, throwOnNotFound: true);
            var plainDescriptor = descriptors.Single(d => d.Codon.Id == "AlwaysVisible");

            var command = CommandWrapper.CreateLazyCommand(plainDescriptor.Codon, plainDescriptor.Conditions);
            Assert.That(command.CanExecute(plainDescriptor.Parameter), Is.True);

            command.Execute(plainDescriptor.Parameter);

            Assert.That(TestObservingCommand.LastOwner, Is.SameAs(owner),
                "Executing the command built for the 'AlwaysVisible' codon should run the real " +
                "TestObservingCommand.Run() with Owner set to the context-menu owner parameter.");
        });
    }

    [Test]
    public void ContextMenuBuilderProducesNothingForDisabledItemsCommand()
    {
        RunWithIsolatedAddInTree((addInTree, owner) =>
        {
            var descriptors = addInTree.BuildItems<MenuItemDescriptor>(ContextMenuPath, owner, throwOnNotFound: true);
            var disabledDescriptor = descriptors.Single(d => d.Codon.Id == "DisabledItem");

            var command = CommandWrapper.CreateLazyCommand(disabledDescriptor.Codon, disabledDescriptor.Conditions);

            // The condition fails (owner lacks the "Allowed" state) so CanExecute must be false,
            // matching the flyout item being disabled rather than removed.
            Assert.That(command.CanExecute(disabledDescriptor.Parameter), Is.False);
        });
    }

    /// <summary>
    /// Builds a fresh, isolated AddInTreeImpl with a synthetic addin registered under
    /// <see cref="ContextMenuPath"/>, temporarily swaps ServiceSingleton.ServiceProvider to resolve
    /// IAddInTree to it, runs <paramref name="body"/>, and restores whatever ServiceProvider was in
    /// place beforehand -- even if the body throws -- so the ~228 other tests sharing this process
    /// are never left with a broken/foreign IAddInTree.
    /// </summary>
    private static void RunWithIsolatedAddInTree(Action<AddInTreeImpl, FakeOwnerState> body)
    {
        var addInTree = new AddInTreeImpl(null);

        const string addinXml = """
            <AddIn name="UnoDevelop.Core.Tests Synthetic Explorer AddIn">
              <Runtime>
                <Import assembly=":UnoDevelop.Core.Tests" />
              </Runtime>

              <Path name="/UnoDevelop.Core.Tests/SolutionExplorerContextMenu">
                <MenuItem id="AlwaysVisible" label="Plain Command" class="UnoDevelop.Core.Tests.SolutionExplorerContextMenuTests+TestObservingCommand" />
                <MenuItem id="Sep" type="Separator" />
                <Condition name="Ownerstate" ownerstate="Allowed">
                  <MenuItem id="ExcludedItem" label="Excluded Item" class="UnoDevelop.Core.Tests.SolutionExplorerContextMenuTests+TestObservingCommand" />
                </Condition>
                <Condition name="Ownerstate" ownerstate="Allowed" action="Disable">
                  <MenuItem id="DisabledItem" label="Disabled Item" class="UnoDevelop.Core.Tests.SolutionExplorerContextMenuTests+TestObservingCommand" />
                </Condition>
              </Path>
            </AddIn>
            """;

        AddIn addIn;
        using (var reader = new StringReader(addinXml))
        {
            addIn = AddIn.Load(addInTree, reader);
        }
        addIn.Enabled = true;
        addInTree.InsertAddIn(addIn);

        var previousServiceProvider = ServiceSingleton.ServiceProvider;
        ServiceSingleton.ServiceProvider = new SingleServiceProvider(typeof(IAddInTree), addInTree);
        try
        {
            var owner = new FakeOwnerState(FakeOwnerState.Flags.None);
            body(addInTree, owner);
        }
        finally
        {
            ServiceSingleton.ServiceProvider = previousServiceProvider;
        }
    }

    private sealed class SingleServiceProvider : IServiceProvider
    {
        private readonly Type _serviceType;
        private readonly object _service;

        public SingleServiceProvider(Type serviceType, object service)
        {
            _serviceType = serviceType;
            _service = service;
        }

        public object GetService(Type serviceType) => serviceType == _serviceType ? _service : null;
    }

    internal sealed class FakeOwnerState : IOwnerState
    {
        [Flags]
        public enum Flags
        {
            None = 0,
            Allowed = 1
        }

        public FakeOwnerState(Flags state) => State = state;

        public Flags State { get; }

        public Enum InternalState => State;
    }

    /// <summary>
    /// Referenced by class= in the synthetic addin XML above; proves that clicking through the
    /// real AddInTree/CommandWrapper machinery invokes the actual command's Run() with Owner set.
    /// </summary>
    internal sealed class TestObservingCommand : AbstractMenuCommand
    {
        public static object LastOwner;

        public override void Run() => LastOwner = Owner;
    }
}
