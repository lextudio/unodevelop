using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.ProjectSystem;
using NUnit.Framework;
using UnoDevelop.Services;

namespace UnoDevelop.Core.Tests;

[TestFixture]
public sealed class ProjectSystemTreeProviderTests
{
    [Test]
    public void UnloadedProjectBuildsDependenciesTreeViaCpsBridge()
    {
        var root = Path.Combine(Path.GetTempPath(), "UnoDevelopProjectTreeTests-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, "Lib"));

            var projectPath = Path.Combine(root, "App.csproj");
            var referencedProject = Path.Combine(root, "Lib", "Lib.csproj");

            File.WriteAllText(referencedProject, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            File.WriteAllText(projectPath, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
                    <ProjectReference Include="Lib/Lib.csproj" />
                    <Reference Include="System.Xml" />
                    <Analyzer Include="analyzers/Demo.Analyzer.dll" />
                    <SDKReference Include="DemoSDK, Version=1.0">
                      <Version>1.0</Version>
                    </SDKReference>
                    <FrameworkReference Include="Microsoft.AspNetCore.App">
                      <Visible>false</Visible>
                    </FrameworkReference>
                  </ItemGroup>
                </Project>
                """);

            var tree = new UnoDevelopProjectTreeProvider(projectPath, "App").BuildTree();
            var dependencies = tree.Children.Single(child => child.Caption == "Dependencies");

            Assert.That(dependencies.Flags.Contains(ProjectTreeFlags.Common.DependenciesFolder), Is.True);
            Assert.That(dependencies.Children.Select(child => child.Caption), Is.EquivalentTo(new[]
            {
                "Analyzers",
                "Assemblies",
                "Packages",
                "Projects",
                "SDKs"
            }));
            Assert.That(dependencies.Children.Any(child => child.Caption == "Frameworks"), Is.False);

            var packages = dependencies.Children.Single(child => child.Caption == "Packages");
            var package = packages.Children.Single();
            Assert.That(packages.Flags.Contains(ProjectTreeFlags.Common.PackagesFolder), Is.True);
            Assert.That(package.Caption, Is.EqualTo("Newtonsoft.Json (13.0.3)"));
            Assert.That(package.Flags.Contains(ProjectTreeFlags.Common.PackageReference), Is.True);
            Assert.That(package.BrowseObjectProperties?.ItemName, Is.EqualTo("Newtonsoft.Json"));

            var projects = dependencies.Children.Single(child => child.Caption == "Projects");
            var project = projects.Children.Single();
            Assert.That(project.Caption, Is.EqualTo("Lib"));
            Assert.That(project.Flags.Contains(ProjectTreeFlags.Common.ProjectReference), Is.True);
            Assert.That(project.BrowseObjectProperties?.ItemName, Is.EqualTo("Lib/Lib.csproj"));

            var sdks = dependencies.Children.Single(child => child.Caption == "SDKs");
            var sdk = sdks.Children.Single();
            Assert.That(sdks.Flags.Contains(ProjectTreeFlags.Common.ReferencesFolder), Is.True);
            Assert.That(sdk.Caption, Is.EqualTo("DemoSDK (1.0)"));
            Assert.That(sdk.BrowseObjectProperties?.ItemType, Is.EqualTo(DependencyRuleNames.SdkReference));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public async Task UnloadedProjectBuildsDependenciesTreeViaRealDataflowPipeline()
    {
        // Same fixture as UnloadedProjectBuildsDependenciesTreeViaCpsBridge, but exercised through
        // BuildTreeAsync() — the real MSBuildDependencySubscriber/DependenciesSnapshotProvider
        // dataflow pipeline (slices 41-45) wired into the live Solution Explorer path (slice 46) —
        // instead of the imperative DependencyTreeBridgeBuilder. Proves the async wiring produces
        // an equivalent tree shape, not just that it compiles. See docs/project-system.md (Slice 46).
        var root = Path.Combine(Path.GetTempPath(), "UnoDevelopProjectTreeTests-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, "Lib"));

            var projectPath = Path.Combine(root, "App.csproj");
            var referencedProject = Path.Combine(root, "Lib", "Lib.csproj");

            File.WriteAllText(referencedProject, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            File.WriteAllText(projectPath, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
                    <ProjectReference Include="Lib/Lib.csproj" />
                    <Reference Include="System.Xml" />
                  </ItemGroup>
                </Project>
                """);

            var tree = await new UnoDevelopProjectTreeProvider(projectPath, "App").BuildTreeAsync();
            var dependencies = tree.Children.Single(child => child.Caption == "Dependencies");

            Assert.That(dependencies.Flags.Contains(ProjectTreeFlags.Common.DependenciesFolder), Is.True);
            Assert.That(dependencies.Children.Select(child => child.Caption), Is.EquivalentTo(new[]
            {
                "Assemblies",
                "Packages",
                "Projects",
            }));

            var packages = dependencies.Children.Single(child => child.Caption == "Packages");
            var package = packages.Children.Single();
            Assert.That(package.Caption, Is.EqualTo("Newtonsoft.Json (13.0.3)"));

            var projects = dependencies.Children.Single(child => child.Caption == "Projects");
            var project = projects.Children.Single();
            Assert.That(project.Caption, Is.EqualTo("Lib"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public void UnloadedMultiTargetProjectBuildsTargetFrameworkDependencySlices()
    {
        var root = Path.Combine(Path.GetTempPath(), "UnoDevelopProjectTreeTests-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);

            var projectPath = Path.Combine(root, "App.csproj");
            File.WriteAllText(projectPath, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFrameworks>net8.0;net9.0</TargetFrameworks>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
                    <PackageReference Include="OnlyNet8" Version="1.0.0" Condition="'$(TargetFramework)' == 'net8.0'" />
                    <PackageReference Include="OnlyNet9" Version="2.0.0" Condition="'$(TargetFramework)' == 'net9.0'" />
                    <PackageReference Include="NotNet9" Version="5.0.0" Condition="'$(TargetFramework)' != 'net9.0'" />
                    <PackageReference Include="ReverseNet9" Version="6.0.0" Condition="&quot;net9.0&quot; == &quot;$(TargetFramework)&quot;" />
                  </ItemGroup>
                  <ItemGroup Condition="'$(TargetFramework)' == 'net8.0'">
                    <PackageReference Include="GroupNet8" Version="3.0.0" />
                    <PackageReference Include="ConflictingCondition" Version="4.0.0" Condition="'$(TargetFramework)' == 'net9.0'" />
                  </ItemGroup>
                </Project>
                """);

            var tree = new UnoDevelopProjectTreeProvider(projectPath, "App").BuildTree();
            var dependencies = tree.Children.Single(child => child.Caption == "Dependencies");

            Assert.That(dependencies.Children.Select(child => child.Caption), Is.EquivalentTo(new[]
            {
                "net8.0",
                "net9.0"
            }));

            AssertPackages(dependencies.Children.Single(child => child.Caption == "net8.0"),
                "Newtonsoft.Json (13.0.3)",
                "OnlyNet8 (1.0.0)",
                "GroupNet8 (3.0.0)",
                "NotNet9 (5.0.0)");
            AssertPackages(dependencies.Children.Single(child => child.Caption == "net9.0"),
                "Newtonsoft.Json (13.0.3)",
                "OnlyNet9 (2.0.0)",
                "ReverseNet9 (6.0.0)");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public void UnloadedProjectAccumulatesRepeatedTargetFrameworksProperties()
    {
        var root = Path.Combine(Path.GetTempPath(), "UnoDevelopProjectTreeTests-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);

            var projectPath = Path.Combine(root, "App.csproj");
            File.WriteAllText(projectPath, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFrameworks>$(TargetFrameworks);net8.0</TargetFrameworks>
                    <TargetFrameworks>$(TargetFrameworks);net9.0</TargetFrameworks>
                    <TargetFrameworks>$(TargetFrameworks);net462</TargetFrameworks>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="Common" Version="1.0.0" />
                    <PackageReference Include="LegacyOnly" Version="2.0.0" Condition="'$(TargetFramework)' == 'net462'" />
                  </ItemGroup>
                </Project>
                """);

            var tree = new UnoDevelopProjectTreeProvider(projectPath, "App").BuildTree();
            var dependencies = tree.Children.Single(child => child.Caption == "Dependencies");

            Assert.That(dependencies.Children.Select(child => child.Caption), Is.EquivalentTo(new[]
            {
                "net8.0",
                "net9.0",
                "net462"
            }));

            AssertPackages(dependencies.Children.Single(child => child.Caption == "net8.0"), "Common (1.0.0)");
            AssertPackages(dependencies.Children.Single(child => child.Caption == "net9.0"), "Common (1.0.0)");
            AssertPackages(dependencies.Children.Single(child => child.Caption == "net462"), "Common (1.0.0)", "LegacyOnly (2.0.0)");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void AssertPackages(Microsoft.VisualStudio.ProjectSystem.IProjectTree targetFramework, params string[] expectedPackages)
    {
        var packages = targetFramework.Children.Single(child => child.Caption == "Packages");
        Assert.That(packages.Flags.Contains(ProjectTreeFlags.Common.PackagesFolder), Is.True);
        Assert.That(packages.Children.Select(child => child.Caption), Is.EquivalentTo(expectedPackages));
    }
}
