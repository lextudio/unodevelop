using System.Linq;
using ICSharpCode.SharpDevelop.LanguageServices;
using ICSharpCode.SharpDevelop.Project;
using NUnit.Framework;
using UnoDevelop.Services;

#nullable enable

namespace UnoDevelop.Core.Tests
{
    public sealed class UnoTaskServiceTests
    {
        [Test]
        public void ReplaceLanguageDiagnostics_UpdatesOnlyMatchingLanguageDiagnostics()
        {
            var service = new UnoTaskService();
            service.Add(UnoTask.FromBuildError(new BuildError("/tmp/Program.cs", 1, 1, "CS1002", "Build failed")));
            service.ReplaceLanguageDiagnostics(
                "/tmp/Program.cs",
                new[]
                {
                    new LanguageDiagnostic(
                        "CS0029",
                        "Cannot implicitly convert type 'int' to 'string'",
                        DiagnosticSeverity.Error,
                        new TextSpan(new TextPosition(3, 20), new TextPosition(3, 22)))
                });

            service.ReplaceLanguageDiagnostics("/tmp/Program.cs", System.Array.Empty<LanguageDiagnostic>());

            Assert.That(service.Tasks.Count(), Is.EqualTo(1));
            Assert.That(service.Tasks.Single().Description, Is.EqualTo("Build failed (CS1002)"));
            Assert.That(service.GetCount(UnoTaskType.Error), Is.EqualTo(1));
        }

        [Test]
        public void ClearLanguageDiagnostics_RemovesOnlyMatchingLanguageDiagnostics()
        {
            var service = new UnoTaskService();
            service.Add(UnoTask.FromBuildError(new BuildError("/tmp/Program.cs", 1, 1, "CS1002", "Build failed")));
            service.ReplaceLanguageDiagnostics(
                "/tmp/Program.cs",
                new[]
                {
                    new LanguageDiagnostic(
                        "CS0029",
                        "Cannot implicitly convert type 'int' to 'string'",
                        DiagnosticSeverity.Error,
                        new TextSpan(new TextPosition(3, 20), new TextPosition(3, 22)))
                });
            service.ReplaceLanguageDiagnostics(
                "/tmp/Other.cs",
                new[]
                {
                    new LanguageDiagnostic(
                        "CS0168",
                        "Variable is declared but never used",
                        DiagnosticSeverity.Warning,
                        new TextSpan(new TextPosition(5, 17), new TextPosition(5, 21)))
                });

            service.ClearLanguageDiagnostics("/tmp/Program.cs");

            Assert.That(service.Tasks.Select(task => task.FileName), Is.EquivalentTo(new[] { "/tmp/Program.cs", "/tmp/Other.cs" }));
            Assert.That(service.Tasks.Count(task => task.Tag is LanguageDiagnostic), Is.EqualTo(1));
            Assert.That(service.GetCount(UnoTaskType.Error), Is.EqualTo(1));
            Assert.That(service.GetCount(UnoTaskType.Warning), Is.EqualTo(1));
        }

        [Test]
        public void FromBuildError_MapsSeverityAndCode()
        {
            var error = new BuildError("/tmp/Program.cs", 4, 9, "CS1002", "Semicolon expected")
            {
                IsWarning = true
            };

            var task = UnoTask.FromBuildError(error);

            Assert.That(task.TaskType, Is.EqualTo(UnoTaskType.Warning));
            Assert.That(task.Line, Is.EqualTo(4));
            Assert.That(task.Column, Is.EqualTo(9));
            Assert.That(task.File, Is.EqualTo("Program.cs"));
            Assert.That(task.Description, Is.EqualTo("Semicolon expected (CS1002)"));
            Assert.That(task.Tag, Is.SameAs(error));
        }

        [Test]
        public void FromDiagnostic_MapsSeverityAndLocation()
        {
            var task = UnoTask.FromDiagnostic(
                "/tmp/Program.cs",
                new LanguageDiagnostic(
                    "CS0168",
                    "Variable is declared but never used",
                    DiagnosticSeverity.Warning,
                    new TextSpan(new TextPosition(7, 13), new TextPosition(7, 17))));

            Assert.That(task.TaskType, Is.EqualTo(UnoTaskType.Warning));
            Assert.That(task.Line, Is.EqualTo(7));
            Assert.That(task.Column, Is.EqualTo(13));
            Assert.That(task.File, Is.EqualTo("Program.cs"));
            Assert.That(task.Description, Does.Contain("CS0168"));
        }
    }
}
