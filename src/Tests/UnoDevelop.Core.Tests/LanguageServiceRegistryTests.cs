using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.IO;
using Microsoft.CodeAnalysis.Diagnostics;
using ICSharpCode.SharpDevelop.Editor.CodeCompletion;
using ICSharpCode.SharpDevelop.LanguageServices;
using ICSharpCode.SharpDevelop.LanguageServices.Roslyn;
using NUnit.Framework;

#nullable enable

namespace UnoDevelop.Core.Tests
{
    public sealed class LanguageServiceRegistryTests
    {
        [Test]
        public void GetService_ReturnsRegisteredServiceByFileName()
        {
            var fallback = new StubLanguageService();
            var csharp = new StubLanguageService();
            var registry = new LanguageServiceRegistry(fallback);

            registry.RegisterExtension(".cs", csharp);

            Assert.That(registry.GetService(@"C:\src\Program.CS"), Is.SameAs(csharp));
        }

        [Test]
        public void GetService_ReturnsRegisteredServiceByExtensionWithoutLeadingDot()
        {
            var fallback = new StubLanguageService();
            var csharp = new StubLanguageService();
            var registry = new LanguageServiceRegistry(fallback);

            registry.RegisterExtension("cs", csharp);

            Assert.That(registry.GetService(".CS"), Is.SameAs(csharp));
        }

        [Test]
        public void GetService_ReturnsFallbackForUnregisteredExtension()
        {
            var fallback = new StubLanguageService();
            var registry = new LanguageServiceRegistry(fallback);

            Assert.That(registry.GetService("README.md"), Is.SameAs(fallback));
        }

        [Test]
        public async Task NoOpLanguageService_ReturnsEmptyResults()
        {
            var documentId = new DocumentId("Program.cs");

            var completions = await NoOpLanguageService.Instance.GetCompletionsAsync(documentId, 0, CancellationToken.None);
            var diagnostics = await NoOpLanguageService.Instance.GetDiagnosticsAsync(documentId, CancellationToken.None);

            Assert.That(completions.Items, Is.Empty);
            Assert.That(diagnostics, Is.Empty);
        }

        [Test]
        public void CompletionItemList_AdaptsLanguageServiceCompletionResult()
        {
            var result = new CompletionResult(
                new[] { new CompletionItem("Console", "Console", "System.Console", "class") },
                null);

            var list = LanguageServiceCompletionItemList.FromResult(result);

            Assert.That(list.Items, Has.Count.EqualTo(1));
            Assert.That(((ICompletionItem)list.Items[0]).Text, Is.EqualTo("Console"));
            Assert.That(((ICompletionItem)list.Items[0]).Description, Is.EqualTo("System.Console"));
            Assert.That(list.SuggestedItem, Is.SameAs(list.Items[0]));
        }

        [Test]
        public async Task CSharpVBLanguageService_ReturnsCSharpDiagnostics()
        {
            using var service = new CSharpVBLanguageService();
            var documentId = new DocumentId("Diagnostics.cs");
            await service.UpsertDocumentAsync(
                documentId,
                "class C { void M() { string text = 42; } }",
                CancellationToken.None);

            var diagnostics = await service.GetDiagnosticsAsync(documentId, CancellationToken.None);

            Assert.That(diagnostics, Has.Some.Matches<LanguageDiagnostic>(diagnostic => diagnostic.Id == "CS0029"));
        }

        [Test]
        public async Task CSharpVBLanguageService_ReturnsCSharpCompletions()
        {
            using var service = new CSharpVBLanguageService();
            var text = "class C { void M() { System.Con } }";
            var documentId = new DocumentId("Completion.cs");
            await service.UpsertDocumentAsync(documentId, text, CancellationToken.None);

            var completions = await service.GetCompletionsAsync(documentId, text.IndexOf("Con", System.StringComparison.Ordinal) + 3, CancellationToken.None);

            Assert.That(completions.Items, Has.Some.Matches<CompletionItem>(item => item.DisplayText == "Console"));
        }

        [Test]
        public async Task CSharpVBLanguageService_ReturnsQuickInfo()
        {
            using var service = new CSharpVBLanguageService();
            var text = "class C { string Name { get; set; } }";
            var documentId = new DocumentId("QuickInfo.cs");
            await service.UpsertDocumentAsync(documentId, text, CancellationToken.None);

            var quickInfo = await service.GetQuickInfoAsync(
                documentId,
                text.IndexOf("Name", StringComparison.Ordinal) + 1,
                CancellationToken.None);

            Assert.That(quickInfo, Is.Not.Null);
            Assert.That(quickInfo!.Text, Does.Contain("string"));
            Assert.That(quickInfo.Text, Does.Contain("Name"));
        }

        [Test]
        public async Task CSharpVBLanguageService_UpsertDocumentReplacesExistingText()
        {
            using var service = new CSharpVBLanguageService();
            var documentId = new DocumentId("Replace.cs");

            await service.UpsertDocumentAsync(documentId, "class C { void M() { string text = 42; } }", CancellationToken.None);
            Assert.That(service.ContainsDocument(documentId), Is.True);
            Assert.That(await service.GetDiagnosticsAsync(documentId, CancellationToken.None),
                Has.Some.Matches<LanguageDiagnostic>(diagnostic => diagnostic.Id == "CS0029"));

            await service.UpsertDocumentAsync(documentId, "class C { void M() { string text = \"ok\"; } }", CancellationToken.None);

            Assert.That(await service.GetDiagnosticsAsync(documentId, CancellationToken.None),
                Has.None.Matches<LanguageDiagnostic>(diagnostic => diagnostic.Id == "CS0029"));
        }

        [Test]
        public async Task CSharpVBLanguageService_LoadProjectAddsCompileDocuments()
        {
            var directory = Path.Combine(Path.GetTempPath(), "UnoDevelopRoslynTests", Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
            try
            {
                var modelFile = Path.Combine(directory, "Model.cs");
                var programFile = Path.Combine(directory, "Program.cs");
                await File.WriteAllTextAsync(modelFile, "public class ProjectModel { public string Name { get; set; } = \"\"; }");
                await File.WriteAllTextAsync(programFile, "class C { void M() { ProjectModel model = new ProjectModel(); model. } }");

                using var service = new CSharpVBLanguageService();
                await service.LoadProjectAsync(
                    new LanguageServiceProjectSnapshot(
                        Path.Combine(directory, "Test.csproj"),
                        "C#",
                        new[] { modelFile, programFile },
                        Array.Empty<string>(),
                        Array.Empty<string>(),
                        Array.Empty<string>(),
                        null,
                        null),
                    CancellationToken.None);

                var text = await File.ReadAllTextAsync(programFile);
                var completions = await service.GetCompletionsAsync(
                    new DocumentId(programFile),
                    text.LastIndexOf('.') + 1,
                    CancellationToken.None);

                Assert.That(service.ContainsDocument(new DocumentId(modelFile)), Is.True);
                Assert.That(service.ContainsDocument(new DocumentId(programFile)), Is.True);
                Assert.That(completions.Items, Has.Some.Matches<CompletionItem>(item => item.DisplayText == "Name"));
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public async Task CSharpVBLanguageService_LoadProjectRemovesDeletedCompileDocuments()
        {
            var directory = Path.Combine(Path.GetTempPath(), "UnoDevelopRoslynTests", Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
            try
            {
                var projectFile = Path.Combine(directory, "Test.csproj");
                var modelFile = Path.Combine(directory, "Model.cs");
                var programFile = Path.Combine(directory, "Program.cs");
                await File.WriteAllTextAsync(modelFile, "public class RemovedModel { public string OldName { get; set; } = \"\"; }");
                await File.WriteAllTextAsync(programFile, "class C { void M() { RemovedModel model = new RemovedModel(); model. } }");

                using var service = new CSharpVBLanguageService();
                await service.LoadProjectAsync(
                    new LanguageServiceProjectSnapshot(
                        projectFile,
                        "C#",
                        new[] { modelFile, programFile },
                        Array.Empty<string>(),
                        Array.Empty<string>(),
                        Array.Empty<string>(),
                        null,
                        null),
                    CancellationToken.None);

                await service.LoadProjectAsync(
                    new LanguageServiceProjectSnapshot(
                        projectFile,
                        "C#",
                        new[] { programFile },
                        Array.Empty<string>(),
                        Array.Empty<string>(),
                        Array.Empty<string>(),
                        null,
                        null),
                    CancellationToken.None);

                var text = await File.ReadAllTextAsync(programFile);
                var completions = await service.GetCompletionsAsync(
                    new DocumentId(programFile),
                    text.LastIndexOf('.') + 1,
                    CancellationToken.None);

                Assert.That(service.ContainsDocument(new DocumentId(modelFile)), Is.False);
                Assert.That(completions.Items, Has.None.Matches<CompletionItem>(item => item.DisplayText == "OldName"));
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public async Task CSharpVBLanguageService_AddCompileDocumentAsync_AddsSingleDocumentWithoutFullReload()
        {
            var directory = Path.Combine(Path.GetTempPath(), "UnoDevelopRoslynTests", Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
            try
            {
                var projectFile = Path.Combine(directory, "Test.csproj");
                var programFile = Path.Combine(directory, "Program.cs");
                await File.WriteAllTextAsync(programFile, "class C { void M() { var model = new AddedModel(); model. } }");

                using var service = new CSharpVBLanguageService();
                await service.LoadProjectAsync(
                    new LanguageServiceProjectSnapshot(
                        projectFile, "C#", new[] { programFile }, Array.Empty<string>(), Array.Empty<string>(),
                        Array.Empty<string>(), null, null),
                    CancellationToken.None);

                // AddedModel.cs is created and added to the project *after* the initial load —
                // simulates a ProjectItemAdded event for a new Compile item, which should be
                // picked up without re-snapshotting the whole project.
                var addedFile = Path.Combine(directory, "AddedModel.cs");
                await File.WriteAllTextAsync(addedFile, "public class AddedModel { public string NewName { get; set; } = \"\"; }");
                await service.AddCompileDocumentAsync(projectFile, addedFile, CancellationToken.None);

                Assert.That(service.ContainsDocument(new DocumentId(addedFile)), Is.True);

                var text = await File.ReadAllTextAsync(programFile);
                var completions = await service.GetCompletionsAsync(
                    new DocumentId(programFile), text.LastIndexOf('.') + 1, CancellationToken.None);
                Assert.That(completions.Items, Has.Some.Matches<CompletionItem>(item => item.DisplayText == "NewName"));
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public async Task CSharpVBLanguageService_RemoveDocument_RemovesSingleDocumentWithoutFullReload()
        {
            var directory = Path.Combine(Path.GetTempPath(), "UnoDevelopRoslynTests", Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
            try
            {
                var projectFile = Path.Combine(directory, "Test.csproj");
                var modelFile = Path.Combine(directory, "Model.cs");
                var programFile = Path.Combine(directory, "Program.cs");
                await File.WriteAllTextAsync(modelFile, "public class RemovedModel { public string OldName { get; set; } = \"\"; }");
                await File.WriteAllTextAsync(programFile, "class C { void M() { var model = new RemovedModel(); model. } }");

                using var service = new CSharpVBLanguageService();
                await service.LoadProjectAsync(
                    new LanguageServiceProjectSnapshot(
                        projectFile, "C#", new[] { modelFile, programFile }, Array.Empty<string>(), Array.Empty<string>(),
                        Array.Empty<string>(), null, null),
                    CancellationToken.None);

                // Simulates a ProjectItemRemoved event for Model.cs — should be picked up
                // directly by DocumentId, without re-snapshotting/diffing the whole project.
                service.RemoveDocument(modelFile);

                Assert.That(service.ContainsDocument(new DocumentId(modelFile)), Is.False);

                var text = await File.ReadAllTextAsync(programFile);
                var completions = await service.GetCompletionsAsync(
                    new DocumentId(programFile), text.LastIndexOf('.') + 1, CancellationToken.None);
                Assert.That(completions.Items, Has.None.Matches<CompletionItem>(item => item.DisplayText == "OldName"));
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public async Task CSharpVBLanguageService_AddCompileDocumentAsync_AddsToAllKnownTargetFrameworkSlices()
        {
            var directory = Path.Combine(Path.GetTempPath(), "UnoDevelopRoslynTests", Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
            try
            {
                var projectFile = Path.Combine(directory, "Test.csproj");
                var existingFile = Path.Combine(directory, "Existing.cs");
                await File.WriteAllTextAsync(existingFile, "public class Existing { }");

                using var service = new CSharpVBLanguageService();
                await service.LoadProjectsAsync(
                    new[]
                    {
                        new LanguageServiceProjectSnapshot(
                            projectFile, "C#", new[] { existingFile }, Array.Empty<string>(), Array.Empty<string>(),
                            Array.Empty<string>(), null, null, targetFramework: "net8.0"),
                        new LanguageServiceProjectSnapshot(
                            projectFile, "C#", new[] { existingFile }, Array.Empty<string>(), Array.Empty<string>(),
                            Array.Empty<string>(), null, null, targetFramework: "net9.0")
                    },
                    CancellationToken.None);

                var addedFile = Path.Combine(directory, "Added.cs");
                await File.WriteAllTextAsync(addedFile, "public class Added { void M() { int unused = 1; } }");
                await service.AddCompileDocumentAsync(projectFile, addedFile, CancellationToken.None);

                var documentId = new DocumentId(addedFile);
                Assert.That(service.ContainsDocument(documentId), Is.True);

                // Diagnostics for the added document must resolve regardless of which TFM is
                // active — proof the add landed in both TFM slices, not just the active one.
                Assert.That(await service.GetDiagnosticsAsync(documentId, CancellationToken.None),
                    Has.Some.Matches<LanguageDiagnostic>(d => d.Id == "CS0219"));
                service.SetActiveTargetFramework(projectFile, "net9.0");
                Assert.That(await service.GetDiagnosticsAsync(documentId, CancellationToken.None),
                    Has.Some.Matches<LanguageDiagnostic>(d => d.Id == "CS0219"));
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public async Task CSharpVBLanguageService_GoToDefinitionReturnsSourceLocation()
        {
            var directory = Path.Combine(Path.GetTempPath(), "UnoDevelopRoslynTests", Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
            try
            {
                var modelFile = Path.Combine(directory, "Model.cs");
                var programFile = Path.Combine(directory, "Program.cs");
                await File.WriteAllTextAsync(modelFile, "public class ProjectModel { }");
                await File.WriteAllTextAsync(programFile, "class C { ProjectModel model; }");

                using var service = new CSharpVBLanguageService();
                await service.LoadProjectAsync(
                    new LanguageServiceProjectSnapshot(
                        Path.Combine(directory, "Test.csproj"),
                        "C#",
                        new[] { modelFile, programFile },
                        Array.Empty<string>(),
                        Array.Empty<string>(),
                        Array.Empty<string>(),
                        null,
                        null),
                    CancellationToken.None);

                var text = await File.ReadAllTextAsync(programFile);
                var offset = text.IndexOf("ProjectModel", StringComparison.Ordinal) + "ProjectModel".Length / 2;
                var targets = await service.GoToDefinitionAsync(new DocumentId(programFile), offset, CancellationToken.None);

                Assert.That(targets, Has.Some.Matches<NavigationTarget>(
                    target => target.FileName == modelFile && target.Position.Line == 1 && target.Position.Column == 14));
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        // Test Explorer's "double-click a test to open its source" (docs: MTP test hosts report
        // only "class X, method Y" via location.type/location.method, never file/line) resolves
        // through this method instead - a solution-wide symbol-by-name lookup, unlike
        // GoToDefinitionAsync which needs a cursor position in an already-open document.
        [Test]
        public async Task CSharpVBLanguageService_FindMemberAsync_ResolvesDeclarationLocation()
        {
            var directory = Path.Combine(Path.GetTempPath(), "UnoDevelopRoslynTests", Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
            try
            {
                var testFile = Path.Combine(directory, "CalculatorTests.cs");
                await File.WriteAllTextAsync(testFile, """
                    namespace Sample
                    {
                        public class CalculatorTests
                        {
                            public void Add_ReturnsSum() { }
                            public void Divide_ReturnsQuotient(int divisor) { }
                        }
                    }
                    """);

                using var service = new CSharpVBLanguageService();
                await service.LoadProjectAsync(
                    new LanguageServiceProjectSnapshot(
                        Path.Combine(directory, "Test.csproj"),
                        "C#",
                        new[] { testFile },
                        Array.Empty<string>(),
                        Array.Empty<string>(),
                        Array.Empty<string>(),
                        null,
                        null),
                    CancellationToken.None);

                var noArgTargets = await service.FindMemberAsync("Sample.CalculatorTests", "Add_ReturnsSum", parameterCount: 0, CancellationToken.None);
                Assert.That(noArgTargets, Has.Some.Matches<NavigationTarget>(target => target.FileName == testFile && target.Position.Line == 5));

                // Parameter-count filter must actually discriminate, not just happen to match.
                var wrongArity = await service.FindMemberAsync("Sample.CalculatorTests", "Add_ReturnsSum", parameterCount: 1, CancellationToken.None);
                Assert.That(wrongArity, Is.Empty);

                var oneArgTargets = await service.FindMemberAsync("Sample.CalculatorTests", "Divide_ReturnsQuotient", parameterCount: 1, CancellationToken.None);
                Assert.That(oneArgTargets, Has.Some.Matches<NavigationTarget>(target => target.FileName == testFile && target.Position.Line == 6));

                var unknownType = await service.FindMemberAsync("Sample.DoesNotExist", "Add_ReturnsSum", parameterCount: null, CancellationToken.None);
                Assert.That(unknownType, Is.Empty);
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public async Task CSharpVBLanguageService_RenameSymbolAsync_RenamesAcrossFiles()
        {
            var directory = Path.Combine(Path.GetTempPath(), "UnoDevelopRoslynTests", Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
            try
            {
                var modelFile = Path.Combine(directory, "Model.cs");
                var programFile = Path.Combine(directory, "Program.cs");
                await File.WriteAllTextAsync(modelFile, "public class ProjectModel { }");
                await File.WriteAllTextAsync(programFile, "class C { void M() { ProjectModel model = new ProjectModel(); } }");

                using var service = new CSharpVBLanguageService();
                await service.LoadProjectAsync(
                    new LanguageServiceProjectSnapshot(
                        Path.Combine(directory, "Test.csproj"), "C#", new[] { modelFile, programFile },
                        Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), null, null),
                    CancellationToken.None);

                var modelText = await File.ReadAllTextAsync(modelFile);
                var offset = modelText.IndexOf("ProjectModel", StringComparison.Ordinal) + 1;
                var editsByFile = await service.RenameSymbolAsync(new DocumentId(modelFile), offset, "RenamedModel", CancellationToken.None);

                Assert.That(editsByFile.Keys, Is.EquivalentTo(new[] { modelFile, programFile }));

                var newModelText = ApplyEdits(modelText, editsByFile[modelFile]);
                Assert.That(newModelText, Does.Contain("public class RenamedModel"));
                Assert.That(newModelText, Does.Not.Contain("ProjectModel"));

                var programText = await File.ReadAllTextAsync(programFile);
                var newProgramText = ApplyEdits(programText, editsByFile[programFile]);
                Assert.That(newProgramText, Does.Contain("RenamedModel model = new RenamedModel();"));
                Assert.That(newProgramText, Does.Not.Contain("ProjectModel"));
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public async Task CSharpVBLanguageService_RenameSymbolAsync_ReturnsEmpty_WhenNoSymbolAtOffset()
        {
            var directory = Path.Combine(Path.GetTempPath(), "UnoDevelopRoslynTests", Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
            try
            {
                var sourceFile = Path.Combine(directory, "Program.cs");
                await File.WriteAllTextAsync(sourceFile, "class C { }");

                using var service = new CSharpVBLanguageService();
                await service.LoadProjectAsync(
                    new LanguageServiceProjectSnapshot(
                        Path.Combine(directory, "Test.csproj"), "C#", new[] { sourceFile },
                        Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), null, null),
                    CancellationToken.None);

                // Offset 0 sits on '}'/'{'-free whitespace-less punctuation — no renameable symbol.
                var editsByFile = await service.RenameSymbolAsync(new DocumentId(sourceFile), 0, "X", CancellationToken.None);

                Assert.That(editsByFile, Is.Empty);
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        static string ApplyEdits(string text, IReadOnlyList<TextEdit> edits)
        {
            var lines = text.Split('\n');
            int OffsetOf(TextPosition position)
            {
                var offset = 0;
                for (var i = 0; i < position.Line - 1; i++)
                    offset += lines[i].Length + 1;
                return offset + position.Column - 1;
            }

            var builder = new System.Text.StringBuilder(text);
            foreach (var edit in edits.OrderByDescending(e => OffsetOf(e.Span.Start)))
            {
                var start = OffsetOf(edit.Span.Start);
                var end = OffsetOf(edit.Span.End);
                builder.Remove(start, end - start);
                builder.Insert(start, edit.NewText);
            }

            return builder.ToString();
        }

        [Test]
        public async Task CSharpVBLanguageService_LoadProjectsAddsProjectReferences()
        {
            var directory = Path.Combine(Path.GetTempPath(), "UnoDevelopRoslynTests", Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
            try
            {
                var libraryProject = Path.Combine(directory, "Library.csproj");
                var appProject = Path.Combine(directory, "App.csproj");
                var libraryFile = Path.Combine(directory, "LibraryType.cs");
                var appFile = Path.Combine(directory, "Program.cs");
                await File.WriteAllTextAsync(libraryFile, "public class LibraryType { public int Value { get; set; } }");
                await File.WriteAllTextAsync(appFile, "class C { void M() { var item = new LibraryType(); item. } }");

                using var service = new CSharpVBLanguageService();
                await service.LoadProjectsAsync(
                    new[]
                    {
                        new LanguageServiceProjectSnapshot(
                            libraryProject,
                            "C#",
                            new[] { libraryFile },
                            Array.Empty<string>(),
                            Array.Empty<string>(),
                            Array.Empty<string>(),
                            null,
                            null),
                        new LanguageServiceProjectSnapshot(
                            appProject,
                            "C#",
                            new[] { appFile },
                            Array.Empty<string>(),
                            new[] { libraryProject },
                            Array.Empty<string>(),
                            null,
                            null)
                    },
                    CancellationToken.None);

                var text = await File.ReadAllTextAsync(appFile);
                var completions = await service.GetCompletionsAsync(
                    new DocumentId(appFile),
                    text.LastIndexOf('.') + 1,
                    CancellationToken.None);

                Assert.That(completions.Items, Has.Some.Matches<CompletionItem>(item => item.DisplayText == "Value"));
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public async Task CSharpVBLanguageService_ProjectReferences_ResolveToExactMatchingTargetFrameworkSlice()
        {
            var directory = Path.Combine(Path.GetTempPath(), "UnoDevelopRoslynTests", Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
            try
            {
                var libraryProject = Path.Combine(directory, "Library.csproj");
                var appProject = Path.Combine(directory, "App.csproj");
                var libraryFile = Path.Combine(directory, "LibraryType.cs");
                var appFile = Path.Combine(directory, "Program.cs");
                // Library's net9.0 slice has an extra member not present in its net8.0 slice.
                // Diagnostics (rather than completions) are asserted on below: Roslyn's
                // completion service can be linked-file-aware for multi-targeted projects and
                // surface members from a sibling TFM slice of the *same* file path, which would
                // make a completion-based assertion here unreliable for reasons unrelated to
                // project-reference resolution; diagnostics reflect the actual compiled slice.
                await File.WriteAllTextAsync(libraryFile, "public class LibraryType { public int Value { get; set; }\n#if NET9_0\npublic int OnlyOnNet9 { get; set; }\n#endif\n}");
                await File.WriteAllTextAsync(appFile, "class C { void M() { var item = new LibraryType(); var x = item.OnlyOnNet9; } }");

                using var service = new CSharpVBLanguageService();
                await service.LoadProjectsAsync(
                    new[]
                    {
                        new LanguageServiceProjectSnapshot(
                            libraryProject, "C#", new[] { libraryFile }, Array.Empty<string>(), Array.Empty<string>(),
                            Array.Empty<string>(), null, null, targetFramework: "net8.0"),
                        new LanguageServiceProjectSnapshot(
                            libraryProject, "C#", new[] { libraryFile }, Array.Empty<string>(), Array.Empty<string>(),
                            new[] { "NET9_0" }, null, null, targetFramework: "net9.0"),
                        new LanguageServiceProjectSnapshot(
                            appProject, "C#", new[] { appFile }, Array.Empty<string>(), new[] { libraryProject },
                            Array.Empty<string>(), null, null, targetFramework: "net8.0"),
                        new LanguageServiceProjectSnapshot(
                            appProject, "C#", new[] { appFile }, Array.Empty<string>(), new[] { libraryProject },
                            Array.Empty<string>(), null, null, targetFramework: "net9.0")
                    },
                    CancellationToken.None);

                // App's net8.0 slice is active by default (first TFM registered) — its exact
                // match, Library's net8.0 slice, must not expose OnlyOnNet9, so referencing it
                // is a compile error (CS1061).
                var documentId = new DocumentId(appFile);
                var diagnosticsNet8 = await service.GetDiagnosticsAsync(documentId, CancellationToken.None);
                Assert.That(diagnosticsNet8, Has.Some.Matches<LanguageDiagnostic>(d => d.Id == "CS1061"));

                service.SetActiveTargetFramework(appProject, "net9.0");
                var diagnosticsNet9 = await service.GetDiagnosticsAsync(documentId, CancellationToken.None);
                Assert.That(diagnosticsNet9, Has.None.Matches<LanguageDiagnostic>(d => d.Id == "CS1061"));
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public async Task CSharpVBLanguageService_LoadProjectsRemovesDeletedProjectReferences()
        {
            var directory = Path.Combine(Path.GetTempPath(), "UnoDevelopRoslynTests", Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
            try
            {
                var libraryProject = Path.Combine(directory, "Library.csproj");
                var appProject = Path.Combine(directory, "App.csproj");
                var libraryFile = Path.Combine(directory, "LibraryType.cs");
                var appFile = Path.Combine(directory, "Program.cs");
                await File.WriteAllTextAsync(libraryFile, "public class LibraryType { public int Value { get; set; } }");
                await File.WriteAllTextAsync(appFile, "class C { void M() { var item = new LibraryType(); item. } }");

                using var service = new CSharpVBLanguageService();
                await service.LoadProjectsAsync(
                    new[]
                    {
                        new LanguageServiceProjectSnapshot(
                            libraryProject,
                            "C#",
                            new[] { libraryFile },
                            Array.Empty<string>(),
                            Array.Empty<string>(),
                            Array.Empty<string>(),
                            null,
                            null),
                        new LanguageServiceProjectSnapshot(
                            appProject,
                            "C#",
                            new[] { appFile },
                            Array.Empty<string>(),
                            new[] { libraryProject },
                            Array.Empty<string>(),
                            null,
                            null)
                    },
                    CancellationToken.None);

                await service.LoadProjectsAsync(
                    new[]
                    {
                        new LanguageServiceProjectSnapshot(
                            libraryProject,
                            "C#",
                            new[] { libraryFile },
                            Array.Empty<string>(),
                            Array.Empty<string>(),
                            Array.Empty<string>(),
                            null,
                            null),
                        new LanguageServiceProjectSnapshot(
                            appProject,
                            "C#",
                            new[] { appFile },
                            Array.Empty<string>(),
                            Array.Empty<string>(),
                            Array.Empty<string>(),
                            null,
                            null)
                    },
                    CancellationToken.None);

                var text = await File.ReadAllTextAsync(appFile);
                var completions = await service.GetCompletionsAsync(
                    new DocumentId(appFile),
                    text.LastIndexOf('.') + 1,
                    CancellationToken.None);

                Assert.That(completions.Items, Has.None.Matches<CompletionItem>(item => item.DisplayText == "Value"));
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public async Task CSharpVBLanguageService_GoToDefinitionResolvesProjectReference()
        {
            var directory = Path.Combine(Path.GetTempPath(), "UnoDevelopRoslynTests", Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
            try
            {
                var libraryProject = Path.Combine(directory, "Library.csproj");
                var appProject = Path.Combine(directory, "App.csproj");
                var libraryFile = Path.Combine(directory, "LibraryType.cs");
                var appFile = Path.Combine(directory, "Program.cs");
                await File.WriteAllTextAsync(libraryFile, "public class LibraryType { public int Value { get; set; } }");
                await File.WriteAllTextAsync(appFile, "class C { LibraryType item; }");

                using var service = new CSharpVBLanguageService();
                await service.LoadProjectsAsync(
                    new[]
                    {
                        new LanguageServiceProjectSnapshot(
                            libraryProject,
                            "C#",
                            new[] { libraryFile },
                            Array.Empty<string>(),
                            Array.Empty<string>(),
                            Array.Empty<string>(),
                            null,
                            null),
                        new LanguageServiceProjectSnapshot(
                            appProject,
                            "C#",
                            new[] { appFile },
                            Array.Empty<string>(),
                            new[] { libraryProject },
                            Array.Empty<string>(),
                            null,
                            null)
                    },
                    CancellationToken.None);

                var text = await File.ReadAllTextAsync(appFile);
                var offset = text.IndexOf("LibraryType", StringComparison.Ordinal) + "LibraryType".Length / 2;
                var targets = await service.GoToDefinitionAsync(new DocumentId(appFile), offset, CancellationToken.None);

                Assert.That(targets, Has.Some.Matches<NavigationTarget>(
                    target => target.FileName == libraryFile && target.Position.Line == 1 && target.Position.Column == 14));
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public async Task CSharpVBLanguageService_FormatReturnsTextEdits()
        {
            using var service = new CSharpVBLanguageService();
            var documentId = new DocumentId("Format.cs");
            var text = "class C{void M(){if(true){System.Console.WriteLine(1);}}}";
            await service.UpsertDocumentAsync(documentId, text, CancellationToken.None);

            var edits = await service.FormatAsync(documentId, null, CancellationToken.None);

            Assert.That(edits, Is.Not.Empty);
            var formatted = ApplyTextEdits(text, edits);
            Assert.That(formatted, Does.Contain("class C"));
            Assert.That(formatted, Does.Contain("void M()"));
            Assert.That(formatted, Does.Contain("if (true)"));
        }

        static string ApplyTextEdits(string text, System.Collections.Generic.IReadOnlyList<TextEdit> edits)
        {
            foreach (var edit in edits.OrderByDescending(edit => GetOffset(text, edit.Span.Start)))
            {
                var start = GetOffset(text, edit.Span.Start);
                var end = GetOffset(text, edit.Span.End);
                text = text.Remove(start, end - start).Insert(start, edit.NewText);
            }

            return text;
        }

        static int GetOffset(string text, TextPosition position)
        {
            var line = 1;
            var column = 1;
            for (var i = 0; i < text.Length; i++)
            {
                if (line == position.Line && column == position.Column)
                    return i;

                if (text[i] == '\n')
                {
                    line++;
                    column = 1;
                }
                else
                {
                    column++;
                }
            }

            return text.Length;
        }

        [Test]
        public async Task CSharpVBLanguageService_MultiTargetedProject_RoutesToActiveTargetFramework()
        {
            var directory = Path.Combine(Path.GetTempPath(), "UnoDevelopRoslynTests", Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
            try
            {
                var projectFile = Path.Combine(directory, "Test.csproj");
                var sourceFile = Path.Combine(directory, "Program.cs");
                // #if NET9_0 only compiles method M's body under that TFM's DefineConstants —
                // this is what makes the two TFM slices' *compilations* genuinely different, not
                // just differently labeled. Diagnostics (rather than completions) are asserted
                // on below: Roslyn's completion service can suggest types from sibling projects
                // in the same workspace ("add a reference" style unimported-type suggestions),
                // which would make a completion-based assertion here flaky for reasons unrelated
                // to per-TFM slicing; diagnostics don't do that cross-project search.
                await File.WriteAllTextAsync(sourceFile, "class C {\n#if NET9_0\n    void M() { int unused = 1; }\n#endif\n}\n");

                using var service = new CSharpVBLanguageService();
                await service.LoadProjectsAsync(
                    new[]
                    {
                        new LanguageServiceProjectSnapshot(
                            projectFile, "C#", new[] { sourceFile }, Array.Empty<string>(), Array.Empty<string>(),
                            Array.Empty<string>(), null, null, targetFramework: "net8.0"),
                        new LanguageServiceProjectSnapshot(
                            projectFile, "C#", new[] { sourceFile }, Array.Empty<string>(), Array.Empty<string>(),
                            new[] { "NET9_0" }, null, null, targetFramework: "net9.0")
                    },
                    CancellationToken.None);

                Assert.That(service.GetTargetFrameworks(projectFile), Is.EquivalentTo(new[] { "net8.0", "net9.0" }));
                // First TFM registered becomes the default active one.
                Assert.That(service.GetActiveTargetFramework(projectFile), Is.EqualTo("net8.0"));

                var documentId = new DocumentId(sourceFile);
                // Under net8.0, method M (and its use of Widget) is preprocessed out entirely —
                // nothing in this file references Widget, so no diagnostics.
                var diagnosticsUnderNet8 = await service.GetDiagnosticsAsync(documentId, CancellationToken.None);
                Assert.That(diagnosticsUnderNet8, Is.Empty);

                service.SetActiveTargetFramework(projectFile, "net9.0");
                Assert.That(service.GetActiveTargetFramework(projectFile), Is.EqualTo("net9.0"));

                // Under net9.0, method M is compiled and its unused local variable is flagged —
                // proof this slice's compilation genuinely differs, not just its TFM label.
                var diagnosticsUnderNet9 = await service.GetDiagnosticsAsync(documentId, CancellationToken.None);
                Assert.That(diagnosticsUnderNet9, Has.Some.Matches<LanguageDiagnostic>(diagnostic => diagnostic.Id == "CS0219"));
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public void CSharpVBLanguageService_SingleTargetedProject_HasNoTargetFrameworksToPickBetween()
        {
            using var service = new CSharpVBLanguageService();

            // Never loaded — GetTargetFrameworks must return empty rather than throwing, so a
            // navigation bar can call it defensively for any project file name.
            Assert.That(service.GetTargetFrameworks(@"C:\does\not\exist.csproj"), Is.Empty);
        }

        [Test]
        public async Task CSharpVBLanguageService_GetDocumentOutlineAsync_ReturnsTypesAndMembers()
        {
            var directory = Path.Combine(Path.GetTempPath(), "UnoDevelopRoslynTests", Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
            try
            {
                var sourceFile = Path.Combine(directory, "Outline.cs");
                await File.WriteAllTextAsync(sourceFile, "public class Widget { public int Count { get; set; } public void Reset() { } } public struct Point { public int X; }");

                using var service = new CSharpVBLanguageService();
                await service.LoadProjectAsync(
                    new LanguageServiceProjectSnapshot(
                        Path.Combine(directory, "Test.csproj"),
                        "C#",
                        new[] { sourceFile },
                        Array.Empty<string>(),
                        Array.Empty<string>(),
                        Array.Empty<string>(),
                        null,
                        null),
                    CancellationToken.None);

                var outline = await service.GetDocumentOutlineAsync(new DocumentId(sourceFile), CancellationToken.None);

                Assert.That(outline.Select(type => type.Name), Is.EquivalentTo(new[] { "Widget", "Point" }));
                var widget = outline.Single(type => type.Name == "Widget");
                Assert.That(widget.Children.Select(member => member.Name), Has.Some.EqualTo("Count"));
                Assert.That(widget.Children, Has.Some.Matches<DocumentOutlineNode>(member => member.Name.StartsWith("Reset(")));
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public async Task CSharpVBLanguageService_GetDocumentOutlineAsync_ExtentSpanCoversWholeDeclarationAndAccessibilityIsReported()
        {
            var directory = Path.Combine(Path.GetTempPath(), "UnoDevelopRoslynTests", Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
            try
            {
                var sourceFile = Path.Combine(directory, "Outline.cs");
                await File.WriteAllTextAsync(sourceFile, "public class Widget\n{\n    private int _count;\n\n    public int Count\n    {\n        get { return _count; }\n    }\n}\n");

                using var service = new CSharpVBLanguageService();
                await service.LoadProjectAsync(
                    new LanguageServiceProjectSnapshot(
                        Path.Combine(directory, "Test.csproj"), "C#", new[] { sourceFile }, Array.Empty<string>(),
                        Array.Empty<string>(), Array.Empty<string>(), null, null),
                    CancellationToken.None);

                var outline = await service.GetDocumentOutlineAsync(new DocumentId(sourceFile), CancellationToken.None);
                var widget = outline.Single(type => type.Name == "Widget");

                // The name-only nav span sits on line 1; the full declaration extends to the
                // closing brace on line 9 — proof ExtentSpan is genuinely wider than Span, not
                // just an alias for it.
                Assert.That(widget.Span.Start.Line, Is.EqualTo(1));
                Assert.That(widget.ExtentSpan.End.Line, Is.EqualTo(9));
                Assert.That(widget.Accessibility, Is.EqualTo("Public"));

                var field = widget.Children.Single(m => m.Name == "_count");
                Assert.That(field.Accessibility, Is.EqualTo("Private"));

                var property = widget.Children.Single(m => m.Name == "Count");
                Assert.That(property.Accessibility, Is.EqualTo("Public"));
                // The property's extent covers its accessor body (line 7), beyond its own
                // single-line name-only Span.
                Assert.That(property.ExtentSpan.End.Line, Is.GreaterThanOrEqualTo(7));
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public async Task CSharpVBLanguageService_LoadsThirdPartyAnalyzer_AndSurfacesItsDiagnostics()
        {
            var directory = Path.Combine(Path.GetTempPath(), "UnoDevelopRoslynTests", Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
            try
            {
                var analyzerPath = CompileTestAnalyzer(directory);

                var sourceFile = Path.Combine(directory, "Program.cs");
                // "Trigger" is the one class name the test analyzer (below) flags; anything else
                // must not trip it, proving the diagnostic really comes from the loaded analyzer
                // and isn't some unrelated compiler diagnostic.
                await File.WriteAllTextAsync(sourceFile, "class Trigger { }\nclass NotFlagged { }\n");

                using var service = new CSharpVBLanguageService();
                await service.LoadProjectAsync(
                    new LanguageServiceProjectSnapshot(
                        Path.Combine(directory, "Test.csproj"), "C#", new[] { sourceFile }, Array.Empty<string>(),
                        Array.Empty<string>(), Array.Empty<string>(), null, null,
                        targetFramework: null, analyzerAssemblyFileNames: new[] { analyzerPath }),
                    CancellationToken.None);

                var diagnostics = await service.GetDiagnosticsAsync(new DocumentId(sourceFile), CancellationToken.None);

                Assert.That(diagnostics, Has.Some.Matches<LanguageDiagnostic>(d => d.Id == "TEST001"));
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        // Proves externals/OpenDevelop/doc/technotes/language-services.md §8.3 (Roslyn code fixes) end to end against a real
        // built-in fixer, per the doc's own "prove it against one well-known built-in fixer
        // before broadening" plan: a missing `using System.Collections.Generic;` produces CS0246
        // on `List<int>`, which Roslyn's built-in add-import CodeFixProvider (MEF-discovered via
        // GetCodeFixProviders) should offer to fix.
        [Test]
        public async Task CSharpVBLanguageService_GetCodeActionsAsync_OffersAddMissingUsing()
        {
            var directory = Path.Combine(Path.GetTempPath(), "UnoDevelopRoslynTests", Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
            try
            {
                var sourceFile = Path.Combine(directory, "Program.cs");
                const string source = "using System;\n\nclass Program\n{\n    static void Main()\n    {\n        var items = new List<int>();\n    }\n}\n";
                await File.WriteAllTextAsync(sourceFile, source);

                using var service = new CSharpVBLanguageService();
                await service.LoadProjectAsync(
                    new LanguageServiceProjectSnapshot(
                        Path.Combine(directory, "Test.csproj"), "C#", new[] { sourceFile }, Array.Empty<string>(),
                        Array.Empty<string>(), Array.Empty<string>(), null, null),
                    CancellationToken.None);

                var documentId = new DocumentId(sourceFile);
                var position = OffsetToPosition(source, source.IndexOf("List", StringComparison.Ordinal));
                var span = new TextSpan(position, position);

                var actions = await service.GetCodeActionsAsync(documentId, span, CancellationToken.None);
                // CS0246 has (at least) two legitimate built-in fixes - "using ..." (AddImport)
                // and "fully qualify" (FullyQualify), both mentioning the namespace - so match
                // on the "using " prefix specifically rather than just the namespace substring.
                var addImport = actions.FirstOrDefault(a => a.Title.StartsWith("using ", StringComparison.Ordinal));

                Assert.That(addImport, Is.Not.Null,
                    () => "No add-import action found. Actions returned: " + string.Join(", ", actions.Select(a => a.Title)));

                var editsByFile = await service.ApplyCodeActionAsync(documentId, addImport!.Id, CancellationToken.None);

                Assert.That(editsByFile, Contains.Key(sourceFile));
                var edits = editsByFile[sourceFile];
                Assert.That(edits, Is.Not.Empty);
                var editedText = ApplyEditsForTest(source, edits);
                Assert.That(editedText, Does.Contain("using System.Collections.Generic;"));
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public async Task CSharpVBLanguageService_GetCodeActionsAsync_NoDiagnosticsAtSpan_ReturnsEmpty()
        {
            var directory = Path.Combine(Path.GetTempPath(), "UnoDevelopRoslynTests", Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
            try
            {
                var sourceFile = Path.Combine(directory, "Program.cs");
                const string source = "class Program\n{\n    static void Main()\n    {\n    }\n}\n";
                await File.WriteAllTextAsync(sourceFile, source);

                using var service = new CSharpVBLanguageService();
                await service.LoadProjectAsync(
                    new LanguageServiceProjectSnapshot(
                        Path.Combine(directory, "Test.csproj"), "C#", new[] { sourceFile }, Array.Empty<string>(),
                        Array.Empty<string>(), Array.Empty<string>(), null, null),
                    CancellationToken.None);

                var span = new TextSpan(new TextPosition(1, 1), new TextPosition(1, 1));
                var actions = await service.GetCodeActionsAsync(new DocumentId(sourceFile), span, CancellationToken.None);

                Assert.That(actions, Is.Empty);
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        static TextPosition OffsetToPosition(string text, int offset)
        {
            var line = 1;
            var lineStart = 0;
            for (var i = 0; i < offset; i++)
            {
                if (text[i] == '\n')
                {
                    line++;
                    lineStart = i + 1;
                }
            }

            return new TextPosition(line, offset - lineStart + 1);
        }

        static string ApplyEditsForTest(string text, IReadOnlyList<TextEdit> edits)
        {
            var lines = text.Split('\n');
            static int ToOffset(string[] textLines, TextPosition position)
            {
                var offset = 0;
                for (var i = 0; i < position.Line - 1; i++)
                    offset += textLines[i].Length + 1;
                return offset + position.Column - 1;
            }

            foreach (var edit in edits.OrderByDescending(e => ToOffset(lines, e.Span.Start)))
            {
                var start = ToOffset(lines, edit.Span.Start);
                var end = ToOffset(lines, edit.Span.End);
                text = text[..start] + edit.NewText + text[end..];
                lines = text.Split('\n');
            }

            return text;
        }

        /// <summary>
        /// Compiles a minimal real <see cref="Microsoft.CodeAnalysis.Diagnostics.DiagnosticAnalyzer"/>
        /// to a .dll on disk, to exercise <see cref="CSharpVBLanguageService"/>'s analyzer-loading
        /// path (AnalyzerFileReference) end-to-end rather than just asserting it compiles.
        /// </summary>
        static string CompileTestAnalyzer(string directory)
        {
            const string analyzerSource = """
                using System.Collections.Immutable;
                using Microsoft.CodeAnalysis;
                using Microsoft.CodeAnalysis.CSharp;
                using Microsoft.CodeAnalysis.CSharp.Syntax;
                using Microsoft.CodeAnalysis.Diagnostics;

                [DiagnosticAnalyzer(LanguageNames.CSharp)]
                public sealed class TriggerAnalyzer : DiagnosticAnalyzer
                {
                    static readonly DiagnosticDescriptor Rule = new(
                        "TEST001", "Trigger class found", "Trigger class found", "Test", DiagnosticSeverity.Warning, true);

                    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

                    public override void Initialize(AnalysisContext context)
                    {
                        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
                        context.EnableConcurrentExecution();
                        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.ClassDeclaration);
                    }

                    static void Analyze(SyntaxNodeAnalysisContext context)
                    {
                        var declaration = (ClassDeclarationSyntax)context.Node;
                        if (declaration.Identifier.Text == "Trigger")
                            context.ReportDiagnostic(Diagnostic.Create(Rule, declaration.Identifier.GetLocation()));
                    }
                }
                """;

            var syntaxTree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(analyzerSource);
            var referenceAssemblies = new[]
            {
                typeof(object).Assembly,
                typeof(System.Collections.Immutable.ImmutableArray).Assembly,
                typeof(DiagnosticAnalyzer).Assembly,
                typeof(Microsoft.CodeAnalysis.CSharp.SyntaxKind).Assembly,
                System.Reflection.Assembly.Load("netstandard"),
                System.Reflection.Assembly.Load("System.Runtime")
            };
            var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
                "TestAnalyzer",
                new[] { syntaxTree },
                referenceAssemblies.Select(a => Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(a.Location)),
                new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));

            var analyzerPath = Path.Combine(directory, "TestAnalyzer.dll");
            Microsoft.CodeAnalysis.Emit.EmitResult emitResult;
            using (var stream = File.Create(analyzerPath))
            {
                emitResult = compilation.Emit(stream);
            }

            Assert.That(emitResult.Success, Is.True,
                () => string.Join(Environment.NewLine, emitResult.Diagnostics.Select(d => d.ToString())));

            return analyzerPath;
        }

        sealed class StubLanguageService : ILanguageService
        {
            public Task UpsertDocumentAsync(DocumentId documentId, string text, CancellationToken cancellationToken) =>
                NoOpLanguageService.Instance.UpsertDocumentAsync(documentId, text, cancellationToken);

            public Task<CompletionResult> GetCompletionsAsync(DocumentId documentId, int offset, CancellationToken cancellationToken) =>
                NoOpLanguageService.Instance.GetCompletionsAsync(documentId, offset, cancellationToken);

            public Task<QuickInfo?> GetQuickInfoAsync(DocumentId documentId, int offset, CancellationToken cancellationToken) =>
                NoOpLanguageService.Instance.GetQuickInfoAsync(documentId, offset, cancellationToken);

            public Task<System.Collections.Generic.IReadOnlyList<LanguageDiagnostic>> GetDiagnosticsAsync(DocumentId documentId, CancellationToken cancellationToken) =>
                NoOpLanguageService.Instance.GetDiagnosticsAsync(documentId, cancellationToken);

            public Task<System.Collections.Generic.IReadOnlyList<NavigationTarget>> GoToDefinitionAsync(DocumentId documentId, int offset, CancellationToken cancellationToken) =>
                NoOpLanguageService.Instance.GoToDefinitionAsync(documentId, offset, cancellationToken);

            public Task<System.Collections.Generic.IReadOnlyList<TextEdit>> FormatAsync(DocumentId documentId, TextSpan? span, CancellationToken cancellationToken) =>
                NoOpLanguageService.Instance.FormatAsync(documentId, span, cancellationToken);

            public void OnTextChanged(DocumentId documentId, TextChange change)
            {
            }

            public Task<System.Collections.Generic.IReadOnlyList<DocumentOutlineNode>> GetDocumentOutlineAsync(DocumentId documentId, CancellationToken cancellationToken) =>
                NoOpLanguageService.Instance.GetDocumentOutlineAsync(documentId, cancellationToken);

            public Task<System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IReadOnlyList<TextEdit>>> RenameSymbolAsync(
                DocumentId documentId, int offset, string newName, CancellationToken cancellationToken) =>
                NoOpLanguageService.Instance.RenameSymbolAsync(documentId, offset, newName, cancellationToken);

            public Task<System.Collections.Generic.IReadOnlyList<NavigationTarget>> FindMemberAsync(
                string typeFullName, string methodName, int? parameterCount, CancellationToken cancellationToken) =>
                NoOpLanguageService.Instance.FindMemberAsync(typeFullName, methodName, parameterCount, cancellationToken);

            public Task<System.Collections.Generic.IReadOnlyList<CodeActionInfo>> GetCodeActionsAsync(
                DocumentId documentId, TextSpan span, CancellationToken cancellationToken) =>
                NoOpLanguageService.Instance.GetCodeActionsAsync(documentId, span, cancellationToken);

            public Task<System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IReadOnlyList<TextEdit>>> ApplyCodeActionAsync(
                DocumentId documentId, string actionId, CancellationToken cancellationToken) =>
                NoOpLanguageService.Instance.ApplyCodeActionAsync(documentId, actionId, cancellationToken);
        }
    }
}
