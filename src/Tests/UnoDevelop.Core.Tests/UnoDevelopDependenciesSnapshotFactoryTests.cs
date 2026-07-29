using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.ProjectSystem.Tree.Dependencies;
using NUnit.Framework;

namespace UnoDevelop.Core.Tests;

/// <summary>
/// Exercises the real linked CPS dependency dataflow pipeline end-to-end (MSBuildDependencySubscriber
/// + DependenciesSnapshotProvider + the manual composition/active-configuration wiring added in
/// slice 43 — see externals/OpenDevelop/doc/technotes/project-system.md), rather than the imperative
/// DependencyTreeBridgeBuilder path. Exercised through the current public API
/// (<see cref="SharpDevelopDependenciesSnapshotFactory.BuildTreeAsync"/>/<see cref="SharpDevelopDependenciesSnapshotFactory.PruneSessionsExceptAsync"/>/
/// <see cref="SharpDevelopDependenciesSnapshotFactory.ClearAllAsync"/>) rather than the retired,
/// internal <c>BuildSnapshotAsync</c> — <see cref="ProjectSystemTreeProviderTests"/> already covers
/// the resulting tree shape end-to-end through <c>UnoDevelopProjectTreeProvider</c>; these tests
/// instead focus on behavior that lives specifically in this factory: session reuse across repeated
/// calls, rebuilding when a project's target framework set changes, and pruning/disposing sessions.
/// </summary>
[TestFixture]
public sealed class SharpDevelopDependenciesSnapshotFactoryTests
{
    [Test]
    public async Task SingleTargetProject_ProducesExpectedDependencyGroups()
    {
        var items = new List<DependencyBridgeItem>
        {
            new(DependencyBridgeItemKind.Package, "Newtonsoft.Json", null,
                ImmutableDictionary<string, string>.Empty.Add("Version", "13.0.3")),
            new(DependencyBridgeItemKind.Assembly, "System.Xml.dll", "/fake/System.Xml.dll",
                ImmutableDictionary<string, string>.Empty),
            new(DependencyBridgeItemKind.Project, "Lib/Lib.csproj", "/fake/Lib/Lib.csproj",
                ImmutableDictionary<string, string>.Empty),
        };

        var itemsByTfm = new Dictionary<string, IReadOnlyList<DependencyBridgeItem>> { [""] = items };

        var tree = await SharpDevelopDependenciesSnapshotFactory.BuildTreeAsync("/fake/App.csproj", itemsByTfm);

        Assert.That(tree, Is.Not.Null);
        var groupNames = tree!.Children.Select(child => child.Caption).ToImmutableHashSet();

        Assert.That(groupNames, Does.Contain("Packages"));
        Assert.That(groupNames, Does.Contain("Assemblies"));
        Assert.That(groupNames, Does.Contain("Projects"));
        // No Analyzer/SDK/Framework/COM items were supplied, so those groups must be absent —
        // proves MSBuildDependencyCollection.TryUpdate correctly skips empty rule pairs instead
        // of throwing on the always-present-but-often-empty ProjectChanges entries.
        Assert.That(groupNames, Does.Not.Contain("Analyzers"));
        Assert.That(groupNames, Does.Not.Contain("SDKs"));
        Assert.That(groupNames, Does.Not.Contain("Frameworks"));
        Assert.That(groupNames, Does.Not.Contain("COM"));

        var packages = tree.Children.Single(child => child.Caption == "Packages");
        Assert.That(packages.Children.Single().Caption, Is.EqualTo("Newtonsoft.Json (13.0.3)"));

        await SharpDevelopDependenciesSnapshotFactory.ClearAllAsync();
    }

    [Test]
    public async Task MultiTargetProject_ProducesOneSlicePerTargetFramework()
    {
        var net8Items = new List<DependencyBridgeItem>
        {
            new(DependencyBridgeItemKind.Package, "Common.Pkg", null, ImmutableDictionary<string, string>.Empty.Add("Version", "1.0.0")),
            new(DependencyBridgeItemKind.Package, "Net8Only.Pkg", null, ImmutableDictionary<string, string>.Empty.Add("Version", "2.0.0")),
        };
        var net9Items = new List<DependencyBridgeItem>
        {
            new(DependencyBridgeItemKind.Package, "Common.Pkg", null, ImmutableDictionary<string, string>.Empty.Add("Version", "1.0.0")),
        };

        var itemsByTfm = new Dictionary<string, IReadOnlyList<DependencyBridgeItem>>
        {
            ["net8.0"] = net8Items,
            ["net9.0"] = net9Items,
        };

        var tree = await SharpDevelopDependenciesSnapshotFactory.BuildTreeAsync("/fake/Multi.csproj", itemsByTfm);

        Assert.That(tree, Is.Not.Null);
        Assert.That(tree!.Children.Select(child => child.Caption), Is.EquivalentTo(new[] { "net8.0", "net9.0" }));

        var net8Slice = tree.Children.Single(child => child.Caption == "net8.0");
        var net9Slice = tree.Children.Single(child => child.Caption == "net9.0");

        var net8Packages = net8Slice.Children.Single(child => child.Caption == "Packages");
        var net9Packages = net9Slice.Children.Single(child => child.Caption == "Packages");

        Assert.That(net8Packages.Children.Select(d => d.Caption), Is.EquivalentTo(new[] { "Common.Pkg (1.0.0)", "Net8Only.Pkg (2.0.0)" }));
        Assert.That(net9Packages.Children.Select(d => d.Caption), Is.EquivalentTo(new[] { "Common.Pkg (1.0.0)" }));

        await SharpDevelopDependenciesSnapshotFactory.ClearAllAsync();
    }

    [Test]
    public async Task RepeatedCallsForSameProject_ReuseSessionAndReflectUpdatedData()
    {
        // Slice 47: the dataflow graph (DependenciesSnapshotSession) is kept alive per project path
        // rather than rebuilt from scratch every call. This proves reuse actually works — not just
        // that a second call doesn't crash, but that posting new evaluation data through the same
        // long-lived graph produces a tree reflecting the new data.
        var projectPath = "/fake/Incremental.csproj";

        var firstItems = new List<DependencyBridgeItem>
        {
            new(DependencyBridgeItemKind.Package, "First.Pkg", null, ImmutableDictionary<string, string>.Empty.Add("Version", "1.0.0")),
        };
        var firstTree = await SharpDevelopDependenciesSnapshotFactory.BuildTreeAsync(
            projectPath, new Dictionary<string, IReadOnlyList<DependencyBridgeItem>> { [""] = firstItems });

        Assert.That(firstTree, Is.Not.Null);
        var firstPackages = firstTree!.Children.Single(child => child.Caption == "Packages");
        Assert.That(firstPackages.Children.Select(d => d.Caption), Is.EquivalentTo(new[] { "First.Pkg (1.0.0)" }));

        var secondItems = new List<DependencyBridgeItem>
        {
            new(DependencyBridgeItemKind.Package, "First.Pkg", null, ImmutableDictionary<string, string>.Empty.Add("Version", "1.0.0")),
            new(DependencyBridgeItemKind.Package, "Second.Pkg", null, ImmutableDictionary<string, string>.Empty.Add("Version", "2.0.0")),
        };
        var secondTree = await SharpDevelopDependenciesSnapshotFactory.BuildTreeAsync(
            projectPath, new Dictionary<string, IReadOnlyList<DependencyBridgeItem>> { [""] = secondItems });

        Assert.That(secondTree, Is.Not.Null);
        var secondPackages = secondTree!.Children.Single(child => child.Caption == "Packages");
        Assert.That(secondPackages.Children.Select(d => d.Caption), Is.EquivalentTo(new[] { "First.Pkg (1.0.0)", "Second.Pkg (2.0.0)" }));

        // And removing a package from the evaluation data removes it from the resulting tree too.
        var thirdItems = new List<DependencyBridgeItem>
        {
            new(DependencyBridgeItemKind.Package, "Second.Pkg", null, ImmutableDictionary<string, string>.Empty.Add("Version", "2.0.0")),
        };
        var thirdTree = await SharpDevelopDependenciesSnapshotFactory.BuildTreeAsync(
            projectPath, new Dictionary<string, IReadOnlyList<DependencyBridgeItem>> { [""] = thirdItems });

        Assert.That(thirdTree, Is.Not.Null);
        var thirdPackages = thirdTree!.Children.Single(child => child.Caption == "Packages");
        Assert.That(thirdPackages.Children.Select(d => d.Caption), Is.EquivalentTo(new[] { "Second.Pkg (2.0.0)" }));

        await SharpDevelopDependenciesSnapshotFactory.ClearAllAsync();
    }

    [Test]
    public async Task ChangingTargetFrameworkSet_RebuildsSessionInsteadOfFailing()
    {
        // If a project's TFM set changes (e.g. editing <TargetFrameworks>), the cached session's
        // fixed slice topology no longer matches — it must be discarded and rebuilt, not reused
        // incorrectly or left stale.
        var projectPath = "/fake/RetargetedApp.csproj";

        var singleTfmTree = await SharpDevelopDependenciesSnapshotFactory.BuildTreeAsync(
            projectPath,
            new Dictionary<string, IReadOnlyList<DependencyBridgeItem>> { [""] = new List<DependencyBridgeItem>() });
        Assert.That(singleTfmTree, Is.Not.Null);
        Assert.That(singleTfmTree!.Children.Any(child => child.Caption is "net8.0" or "net9.0"), Is.False);

        var multiTfmTree = await SharpDevelopDependenciesSnapshotFactory.BuildTreeAsync(
            projectPath,
            new Dictionary<string, IReadOnlyList<DependencyBridgeItem>>
            {
                ["net8.0"] = new List<DependencyBridgeItem>(),
                ["net9.0"] = new List<DependencyBridgeItem>(),
            });

        Assert.That(multiTfmTree, Is.Not.Null);
        Assert.That(multiTfmTree!.Children.Select(child => child.Caption), Is.EquivalentTo(new[] { "net8.0", "net9.0" }));

        await SharpDevelopDependenciesSnapshotFactory.ClearAllAsync();
    }

    [Test]
    public async Task PruneSessionsExceptAsync_DisposesUnvisitedProjectSessionsAndAllowsRebuild()
    {
        // Slice 48: since a project's session (and its underlying dataflow graph) is never disposed
        // automatically, Solution Explorer reconciles sessions against the live project set after
        // every full rebuild via PruneSessionsExceptAsync. This proves pruning actually disposes the
        // stale session's provider (not just forgets the dictionary entry) and that a later rebuild
        // for the same project path builds a fresh, working session rather than reusing or hanging
        // on the disposed one.
        var keptPath = "/fake/Kept.csproj";
        var removedPath = "/fake/Removed.csproj";

        var keptItems = new Dictionary<string, IReadOnlyList<DependencyBridgeItem>>
        {
            [""] = new List<DependencyBridgeItem> { new(DependencyBridgeItemKind.Package, "Kept.Pkg", null, ImmutableDictionary<string, string>.Empty.Add("Version", "1.0.0")) },
        };
        var removedItems = new Dictionary<string, IReadOnlyList<DependencyBridgeItem>>
        {
            [""] = new List<DependencyBridgeItem> { new(DependencyBridgeItemKind.Package, "Removed.Pkg", null, ImmutableDictionary<string, string>.Empty.Add("Version", "1.0.0")) },
        };

        Assert.That(await SharpDevelopDependenciesSnapshotFactory.BuildTreeAsync(keptPath, keptItems), Is.Not.Null);
        Assert.That(await SharpDevelopDependenciesSnapshotFactory.BuildTreeAsync(removedPath, removedItems), Is.Not.Null);

        // Simulate a Solution Explorer rebuild where only "kept" is still in the solution.
        await SharpDevelopDependenciesSnapshotFactory.PruneSessionsExceptAsync(new[] { keptPath });

        // The removed project's session was disposed; rebuilding it must produce a fresh, working
        // session (not throw ObjectDisposedException, not hang against a faulted/disposed provider).
        var rebuiltTree = await SharpDevelopDependenciesSnapshotFactory.BuildTreeAsync(removedPath, removedItems);
        Assert.That(rebuiltTree, Is.Not.Null);
        var rebuiltPackages = rebuiltTree!.Children.Single(child => child.Caption == "Packages");
        Assert.That(rebuiltPackages.Children.Select(d => d.Caption), Is.EquivalentTo(new[] { "Removed.Pkg (1.0.0)" }));

        // The kept project's session was untouched by the prune and still works normally.
        var keptTree = await SharpDevelopDependenciesSnapshotFactory.BuildTreeAsync(keptPath, keptItems);
        Assert.That(keptTree, Is.Not.Null);

        await SharpDevelopDependenciesSnapshotFactory.ClearAllAsync();
    }
}
