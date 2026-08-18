using System.IO;
using System.Windows;
using AiMeetingAssistant.Core.Capture;
using AiMeetingAssistant.Desktop.ViewModels;
using AiMeetingAssistant.Windows.Capture;
using AiMeetingAssistant.Windows.Worker;

namespace AiMeetingAssistant.Desktop;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private bool _isShuttingDown;
    private bool _shutdownComplete;

    public MainWindow()
    {
        InitializeComponent();
        var workerPath = Path.Combine(AppContext.BaseDirectory, "worker", "main.py");
        var pythonExecutable = PythonRuntimeResolver.Resolve();
        var modelPath = LocalModelResolver.ResolveDevelopmentModel();
        _viewModel = new(new WindowsCaptureSourceDiscovery(), new CombinedCaptureCoordinator(), new PythonWorkerClient(pythonExecutable, workerPath), modelPath)
        {
            UIDispatcher = Dispatcher
        };
        DataContext = _viewModel;
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private async void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        Loaded -= OnLoaded;
        await _viewModel.InitializeAsync();
    }

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_shutdownComplete) return;

        e.Cancel = true;
        if (_isShuttingDown) return;
        _isShuttingDown = true;

        try
        {
            await _viewModel.ShutdownAsync();
        }
        catch
        {
            // Ignore shutdown errors
        }
        finally
        {
            _shutdownComplete = true;
            if (!Dispatcher.HasShutdownStarted)
                _ = Dispatcher.BeginInvoke(Close);
        }
    }
}
