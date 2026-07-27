using ICSharpCode.Core;
using Microsoft.UI.Xaml;
using Microsoft.Maui.DevFlow.Agent.Core;
using LeXtudio.DevFlow.Agent.Uno;
using UnoDock.Themes.VS2013;
using UnoDevelop.Services;

namespace UnoDevelop;

public partial class App : Application
{
    public static Window? MainWindow { get; private set; }

    private UnoAgentService? _devFlowAgent;

    public App()
    {
        InitializeComponent();
        Current.Resources.MergedDictionaries.Add(new Vs2013LightTheme().ThemeResourceDictionary);

#if DEBUG
        // Turn on UnoEdit render diagnostics for this session and start from a clean log.
        //UnoEdit.Logging.HighlightLogger.Reset();
        //UnoEdit.Logging.HighlightLogger.Enabled = true;

        //UnoDock.Logging.UnoDockLogger.Reset();
        //UnoDock.Logging.UnoDockLogger.Enabled = true;
#endif
        ServiceBootstrapper.Initialize();
        _devFlowAgent = new UnoAgentService(new AgentOptions
        {
            Port = GetAgentPort()
        });
        _devFlowAgent.Start();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow ??= new Window();
        MainWindow.Title = "UnoDevelop";
        Microsoft.Win32.FileDialogHost.ActiveWindow = MainWindow;
        MainWindow.Content = new MainPage();
        MainWindow.Closed += (_, _) =>
        {
            UnoDevelop.Workbench.ChooseLayoutComboBox.StoreCurrentLayout();
            ICSharpCode.Core.PropertyService.Save();
        };
        MainWindow.Activate();
    }

    private const int UnoDevelopDevFlowPort = 9227;

    private static int GetAgentPort()
    {
        var portValue = Environment.GetEnvironmentVariable("DEVFLOW_AGENT_PORT");
        if (int.TryParse(portValue, out var parsedPort) && parsedPort > 0)
        {
            return parsedPort;
        }

        return UnoDevelopDevFlowPort;
    }
}
