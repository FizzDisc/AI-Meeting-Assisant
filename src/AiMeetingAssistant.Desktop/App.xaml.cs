using System.Windows;
namespace AiMeetingAssistant.Desktop;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length == 1 && e.Args[0] == "--uninstall-ai-components")
        {
            new UninstallAiWindow().ShowDialog();
            Shutdown();
            return;
        }
        var settings = AppPreferences.Load();
        if (!settings.SetupCompleted || settings.AiRuntimeGeneration < 1)
        {
            var setup = new WelcomeWindow();
            setup.ShowDialog();
            if (!setup.ContinueToApp) { Shutdown(); return; }
        }
        MainWindow = new MainWindow();
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        MainWindow.Show();
    }
}

