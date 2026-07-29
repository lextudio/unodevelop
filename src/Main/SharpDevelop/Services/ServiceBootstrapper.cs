using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Threading;
using ICSharpCode.Core;
using ICSharpCode.Core.Implementation;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Editor;
using ICSharpCode.SharpDevelop.LanguageServices;
using ICSharpCode.SharpDevelop.LanguageServices.Lsp;
using ICSharpCode.SharpDevelop.LanguageServices.Roslyn;
using ICSharpCode.SharpDevelop.Parser;
using ICSharpCode.SharpDevelop.Project;
using IBuildService = ICSharpCode.SharpDevelop.Project.IBuildService;
using ICSharpCode.SharpDevelop.Gui;
using ICSharpCode.SharpDevelop.Workbench;
using IOutputPad = ICSharpCode.SharpDevelop.Workbench.IOutputPad;
using ILanguageService = ICSharpCode.SharpDevelop.ILanguageService;
using IBookmarkManager = ICSharpCode.SharpDevelop.Editor.Bookmarks.IBookmarkManager;
using IClipboard = ICSharpCode.SharpDevelop.IClipboard;
using ICSharpCode.UnitTesting;

namespace UnoDevelop.Services;

internal static class ServiceBootstrapper
{
    public static void Initialize()
    {
        // Must run before anything else touches a Microsoft.Build.* type (the first real usage
        // is UnoProjectService's constructor a few lines down, via UnoSolutionModel) - see
        // MSBuildEnvironmentInitializer's doc comment for why.
        ICSharpCode.SharpDevelop.MSBuildHosting.MSBuildEnvironmentInitializer.EnsureRegistered();

        var container = new SharpDevelopServiceContainer();
        container.AddFallbackProvider(ServiceSingleton.FallbackServiceProvider);

        container.AddService(typeof(ICSharpCode.Core.IAnalyticsMonitor), new UnoAnalyticsMonitor());
        container.AddService(typeof(ILoggingService), new TextWriterLoggingService(System.Console.Out));
        container.AddService(typeof(IMessageService), new UnoMessageService());
        var propertyService = new UnoPropertyService();
        container.AddService(typeof(IPropertyService), propertyService);
        // Required before any AddInManager.Enable/Disable/SaveAddInConfiguration call (used by
        // AddInScoutViewContent's enable/disable toggle) - upstream AddInManager.SaveAddInConfiguration
        // writes directly to this path with no null-check of its own.
        System.IO.Directory.CreateDirectory(propertyService.ConfigDirectory.ToString());
        AddInManager.ConfigurationFileName = Path.Combine(propertyService.ConfigDirectory.ToString(), "AddIns.xml");
        // Target directory for NuGet-packaged AddIn installs (AddInPackageManagerService /
        // AddInManager2 parity) - upstream CoreStartup.ConfigureUserAddIns isn't called anywhere
        // in this bootstrap, so UserAddInPath is otherwise left null.
        AddInManager.UserAddInPath = Path.Combine(propertyService.ConfigDirectory.ToString(), "AddIns");
        System.IO.Directory.CreateDirectory(AddInManager.UserAddInPath);
        container.AddService(typeof(ICSharpCode.Core.IResourceService), new ResourceServiceImpl(Path.Combine(propertyService.DataDirectory.ToString(), "resources"), propertyService));
        container.AddService(typeof(ApplicationStateInfoService), new ApplicationStateInfoService());
        container.AddService(typeof(IShutdownService), new UnoShutdownService());
        container.AddService(typeof(IWorkbench), new UnoWorkbenchService());
        container.AddService(typeof(IMessageLoop), new DispatcherMessageLoop(Dispatcher.CurrentDispatcher, SynchronizationContext.Current));
        container.AddService(typeof(IFileSystem), new ICSharpCode.SharpDevelop.FileSystem());
        container.AddService(typeof(IFileService), new UnoFileService());
        container.AddService(typeof(IProjectService), new UnoProjectService());
        container.AddService(typeof(IStatusBarService), new UnoStatusBarService());
        container.AddService(typeof(IRecentOpen), new RecentOpen(propertyService.NestedProperties("RecentOpen")));
        container.AddService(typeof(IOutputPad), new UnoOutputPadService());
        var unoTaskService = new UnoTaskService();
        container.AddService(typeof(UnoTaskService), unoTaskService);
        container.AddService(typeof(ITaskListService), new UnoTaskListService(unoTaskService));
        container.AddService(typeof(IBuildService), new UnoBuildService());
        container.AddService(typeof(IBookmarkManager), new UnoBookmarkManager());
        container.AddService(typeof(IClipboard), new UnoClipboardService());
        container.AddService(typeof(IUnoAddInContextMenuBuilder), new UnoAddInContextMenuBuilder());
        container.AddService(typeof(IUnoAddInMenuBarBuilder), new UnoAddInMenuBarBuilder());
        container.AddService(typeof(IUnoAddInToolbarBuilder), new UnoAddInToolbarBuilder());

        var addInTree = new AddInTreeImpl(container.GetService(typeof(ApplicationStateInfoService)) as ApplicationStateInfoService);
        addInTree.Doozers.TryAdd("Pad", new PadDoozer());
        addInTree.Doozers.TryAdd("PadMenu", new PadMenuDoozer());
        addInTree.Doozers.TryAdd("OptionPanel", new OptionPanels.OptionPanelDoozer());
        addInTree.Doozers.TryAdd("CustomTool", new ICSharpCode.SharpDevelop.Project.CustomToolDoozer());
        addInTree.Doozers.TryAdd("DisplayBinding", new DisplayBindingDoozer());
        // Command-state condition evaluators (SolutionOpen / ExecutionActive / Debugging / Paused)
        // are declared in the .addin <Runtime> blocks, matching the SharpDevelop mechanism.
        container.AddService(typeof(IAddInTree), addInTree);

        ServiceSingleton.ServiceProvider = container;

        CommandWrapper.RegisterConditionRequerySuggestedHandler = _ => { };
        CommandWrapper.UnregisterConditionRequerySuggestedHandler = _ => { };

        LoadBuiltInAddIns(addInTree, container);
        (container.GetService(typeof(IProjectService)) as UnoProjectService)
            ?.LoadAddInProjectBindings(addInTree);
        InitializeFSharpAddIn(addInTree);

        // TestSolution's constructor (the classic backend's ITestSolution) needs SD.ParserService.
        container.AddService(typeof(IParserService), new LanguageServiceParserAdapter());

        // SDTestService's constructor eagerly reads SD.AddInTree.BuildItems<TestFrameworkDescriptor>
        // ("/SharpDevelop/UnitTesting/TestFrameworks") - must come after LoadBuiltInAddIns has
        // parsed UnitTesting.addin's <TestFramework> entry, not just after IAddInTree itself exists.
        container.AddService(typeof(ITestService), new SDTestService());

        // Services that require IAddInTree to be registered first
        container.AddService(typeof(IDisplayBindingService), new DisplayBindingService());
        container.AddService(typeof(ILanguageService), new SDLanguageService());
        var languageServiceRegistry = new LanguageServiceRegistry();
        var csharpVbLanguageService = new CSharpVBLanguageService();
        languageServiceRegistry.RegisterExtension(".cs", csharpVbLanguageService);
        languageServiceRegistry.RegisterExtension(".vb", csharpVbLanguageService);
        // Pilot LSP backends (externals/OpenDevelop/doc/technotes/language-services.md slices 5-6). Each process is started
        // lazily on first document of its language; silently falls back to lexical-only
        // highlighting if the configured command isn't on PATH.
        var lspServerRegistry = LspServerRegistry.CreateDefault();
        // CreateDefault() maps ".xaml" to OpenDevelop's WPF language server (both hosts share
        // this Base-layer registry via SharpDevelopSourceRoot), which serves WPF's XAML dialect,
        // not Uno's. Overwrite it with UnoDevelop's own Uno-framework language server before the
        // registration loop below picks it up - see externals/OpenDevelop/doc/technotes/language-services.md.
        var unoDevelopRoot = FindUnoDevelopRoot();
        if (unoDevelopRoot != null)
        {
            var unoServerDll = FindUnoLanguageServerDll(unoDevelopRoot);
            if (unoServerDll != null)
            {
                // "dotnet exec <dll>" instead of "dotnet run --project <csproj>": a plain
                // "dotnet run" triggers an implicit restore/build whenever anything is out of
                // date, and MSBuild/NuGet write that progress to stdout - the same stream this
                // process's stdio-framed LSP protocol lives on, corrupting every frame after it.
                // "dotnet exec" runs the already-built assembly directly with no such check, so
                // the very first byte on stdout is the LSP handshake. This does mean the project
                // must have been built at least once (true for a normal solution build, since
                // XamlLanguageServer.Uno.csproj is now part of UnoDevelop.slnx) - if it hasn't,
                // there's no fallback here and the language service quietly stays unavailable
                // (falls back to lexical-only highlighting) rather than corrupting its own pipe.
                lspServerRegistry.Register(".xaml", new LspServerLaunchSpec(
                    "xaml",
                    "dotnet",
                    unoDevelopRoot,
                    "exec",
                    unoServerDll,
                    "--workspace",
                    unoDevelopRoot));
            }
        }
        var rootUri = new System.Uri(System.IO.Path.GetFullPath(System.IO.Directory.GetCurrentDirectory())).AbsoluteUri;
        var lspServicesByLanguageId = new Dictionary<string, LspLanguageService>();
        foreach (var extension in new[] { ".xaml", ".ts", ".tsx", ".js", ".jsx", ".py", ".fs", ".fsi" })
        {
            if (!lspServerRegistry.TryGetLaunchSpec(extension, out var launchSpec))
                continue;

            // One shared process (and LspLanguageService) per language server command, since
            // a single typescript-language-server instance already handles all four extensions.
            if (!lspServicesByLanguageId.TryGetValue(launchSpec.LanguageId, out var lspLanguageService))
            {
                lspLanguageService = new LspLanguageService(launchSpec, rootUri);
                lspServicesByLanguageId[launchSpec.LanguageId] = lspLanguageService;
            }

            languageServiceRegistry.RegisterExtension(extension, lspLanguageService);
        }
        container.AddService(typeof(LanguageServiceRegistry), languageServiceRegistry);
        container.AddService(typeof(ICSharpCode.SharpDevelop.Editor.Search.UnoSearchResultsService),
            new ICSharpCode.SharpDevelop.Editor.Search.UnoSearchResultsService());
        // IEditorControlService must be registered after ServiceSingleton.ServiceProvider is set
        // because UnoCodeEditorOptions.Instance (accessed by its constructor) requires PropertyService.
        container.AddService(typeof(IEditorControlService), new UnoEditorControlService());

        // T4 (Text Template Transformation Toolkit) — register syntax highlighting
        // for .tt / .t4 / .ttinclude files.
        InitializeTextTemplatingAddIn(addInTree);

        // Custom tools (externals/OpenDevelop/doc/technotes/t4-templating.md): builds the /SharpDevelop/CustomTools registry
        // and wires auto-run-on-save (FileUtility.FileSaved) + auto-run-before-build.
        ICSharpCode.SharpDevelop.Project.CustomToolsService.Initialize();
    }

    private static void InitializeTextTemplatingAddIn(IAddInTree addInTree)
    {
        foreach (var addIn in addInTree.AddIns)
        {
            if (!addIn.Enabled || !addIn.Manifest.Identities.ContainsKey("UnoDevelop.TextTemplating"))
            {
                continue;
            }

            foreach (var runtime in addIn.Runtimes)
            {
                var startupType = runtime.LoadedAssembly?.GetType("UnoDevelop.TextTemplating.TextTemplatingStartup");
                startupType?.GetMethod("Initialize", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                    ?.Invoke(null, null);
                if (startupType is not null)
                {
                    return;
                }
            }
        }
    }

    private static void InitializeFSharpAddIn(IAddInTree addInTree)
    {
        foreach (var addIn in addInTree.AddIns)
        {
            if (!addIn.Enabled || !addIn.Manifest.Identities.ContainsKey("ICSharpCode.FSharpBinding"))
                continue;

            foreach (var runtime in addIn.Runtimes)
            {
                var startupType = runtime.LoadedAssembly?.GetType("FSharpBinding.FSharpBindingStartup");
                startupType?.GetMethod("Initialize", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                    ?.Invoke(null, null);
                if (startupType is not null)
                    return;
            }
        }
    }

    private static void LoadBuiltInAddIns(AddInTreeImpl addInTree, SharpDevelopServiceContainer container)
    {
        var addInFiles = new List<string>();
        var outputAddInDirectory = Path.Combine(AppContext.BaseDirectory, "AddIns");
        if (Directory.Exists(outputAddInDirectory))
        {
            addInFiles.AddRange(Directory.GetFiles(outputAddInDirectory, "*.addin", SearchOption.AllDirectories));
        }

        var sourceAddInDirectory = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Main", "SharpDevelop", "AddIns");
        var sourceAddInDirectoryFull = Path.GetFullPath(sourceAddInDirectory);
        if (Directory.Exists(sourceAddInDirectoryFull))
        {
            addInFiles.AddRange(Directory.GetFiles(sourceAddInDirectoryFull, "*.addin", SearchOption.AllDirectories));
        }

        addInFiles = addInFiles
            .Distinct(System.StringComparer.OrdinalIgnoreCase)
            .ToList();
        addInTree.Load(addInFiles, new List<string>());
        addInTree.BuildItems<object>("/SharpDevelop/Services", container, false);
    }

    /// <summary>
    /// Walks up from the running assembly's output directory (and, as a fallback, the current
    /// directory) to find the UnoDevelop repository root - identified by the presence of
    /// src/LanguageServer/XamlLanguageServer.Uno, which only exists in this repository, not in
    /// OpenDevelop's. Mirrors LspServerRegistry.FindOpenDevelopRoot's search strategy.
    /// </summary>
    private static string? FindUnoDevelopRoot()
    {
        foreach (var candidate in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var directory = string.IsNullOrEmpty(candidate) ? null : new DirectoryInfo(candidate);
            while (directory != null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "src", "LanguageServer", "XamlLanguageServer.Uno")))
                    return directory.FullName;
                directory = directory.Parent;
            }
        }
        return null;
    }

    /// <summary>
    /// Finds the built uno-xaml-ls.dll under XamlLanguageServer.Uno's bin output, preferring
    /// Release over Debug and the most recently written one within a configuration (multiple
    /// TFM/RID subfolders are possible depending on how it was last built). Returns null if it
    /// has never been built - callers must not fall back to "dotnet run", which would corrupt
    /// the LSP stdio stream (see the call site's comment).
    /// </summary>
    private static string? FindUnoLanguageServerDll(string unoDevelopRoot)
    {
        var binRoot = Path.Combine(
            unoDevelopRoot, "src", "LanguageServer", "XamlLanguageServer.Uno", "bin");
        if (!Directory.Exists(binRoot))
            return null;

        return new[] { "Release", "Debug" }
            .Select(configuration => Path.Combine(binRoot, configuration))
            .Where(Directory.Exists)
            .SelectMany(configurationDirectory => Directory.GetFiles(configurationDirectory, "uno-xaml-ls.dll", SearchOption.AllDirectories))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }
}
