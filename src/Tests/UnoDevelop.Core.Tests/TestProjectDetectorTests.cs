using System;
using System.IO;
using NUnit.Framework;
using ICSharpCode.UnitTesting.Simple;

namespace UnoDevelop.Core.Tests;

[TestFixture]
public sealed class TestProjectDetectorTests
{
    [Test]
    public void ProbeDirectory_NullPath_ReturnsFalse()
    {
        Assert.That(TestProjectDetector.ProbeDirectoryForTestFramework(null), Is.False);
    }

    [Test]
    public void ProbeDirectory_EmptyPath_ReturnsFalse()
    {
        Assert.That(TestProjectDetector.ProbeDirectoryForTestFramework(""), Is.False);
    }

    [Test]
    public void ProbeDirectory_DirDoesNotExist_ReturnsFalse()
    {
        Assert.That(TestProjectDetector.ProbeDirectoryForTestFramework("/tmp/nonexistent-dir-xyz123"), Is.False);
    }

    [Test]
    public void ProbeDirectory_NoDlls_ReturnsFalse()
    {
        using var dir = new TempDir();
        Assert.That(TestProjectDetector.ProbeDirectoryForTestFramework(dir.Path), Is.False);
    }

    [Test]
    public void ProbeDirectory_NonTestDlls_ReturnsFalse()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "Newtonsoft.Json.dll"), "");
        File.WriteAllText(Path.Combine(dir.Path, "Serilog.dll"), "");
        File.WriteAllText(Path.Combine(dir.Path, "System.Runtime.dll"), "");
        Assert.That(TestProjectDetector.ProbeDirectoryForTestFramework(dir.Path), Is.False);
    }

    [Test]
    public void ProbeDirectory_NUnitDll_ReturnsTrue()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "nunit.framework.dll"), "");
        Assert.That(TestProjectDetector.ProbeDirectoryForTestFramework(dir.Path), Is.True);
    }

    [Test]
    public void ProbeDirectory_XunitDll_ReturnsTrue()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "xunit.runner.visualstudio.dll"), "");
        Assert.That(TestProjectDetector.ProbeDirectoryForTestFramework(dir.Path), Is.True);
    }

    [Test]
    public void ProbeDirectory_MSTestDll_ReturnsTrue()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "MSTest.dll"), "");
        Assert.That(TestProjectDetector.ProbeDirectoryForTestFramework(dir.Path), Is.True);
    }

    [Test]
    public void ProbeDirectory_MicrosoftTestSdkDll_ReturnsTrue()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "Microsoft.Testing.Platform.dll"), "");
        Assert.That(TestProjectDetector.ProbeDirectoryForTestFramework(dir.Path), Is.True);
    }

    [Test]
    public void ProbeDirectory_TUnitDll_ReturnsTrue()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "TUnit.dll"), "");
        Assert.That(TestProjectDetector.ProbeDirectoryForTestFramework(dir.Path), Is.True);
    }

    [Test]
    public void ProbeDirectory_MarkersAreCaseInsensitive()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "NUNIT.FRAMEWORK.DLL"), "");
        Assert.That(TestProjectDetector.ProbeDirectoryForTestFramework(dir.Path), Is.True);
    }

    [Test]
    public void ProbeDirectory_SubdirectoryWithTestDll_DetectsViaRecursiveSearch()
    {
        using var dir = new TempDir();
        var subDir = Path.Combine(dir.Path, "net9.0");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "nunit.framework.dll"), "");
        Assert.That(TestProjectDetector.ProbeDirectoryForTestFramework(dir.Path), Is.True);
    }

    [Test]
    public void ProbeDirectory_MixedDlls_DetectsWhenTestFrameworkPresent()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "Newtonsoft.Json.dll"), "");
        File.WriteAllText(Path.Combine(dir.Path, "Serilog.dll"), "");
        File.WriteAllText(Path.Combine(dir.Path, "System.Runtime.dll"), "");

        Assert.That(TestProjectDetector.ProbeDirectoryForTestFramework(dir.Path), Is.False);

        File.WriteAllText(Path.Combine(dir.Path, "xunit.core.dll"), "");
        Assert.That(TestProjectDetector.ProbeDirectoryForTestFramework(dir.Path), Is.True);
    }
}

internal sealed class TempDir : IDisposable
{
    public string Path { get; }

    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ut-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { }
    }
}
