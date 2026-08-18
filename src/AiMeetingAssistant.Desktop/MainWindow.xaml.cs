using System.Windows;
using AiMeetingAssistant.Core.Capture;
using AiMeetingAssistant.Desktop.ViewModels;
using AiMeetingAssistant.Windows.Capture;

namespace AiMeetingAssistant.Desktop;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private bool _isShuttingDown;
    private bool _shutdownComplete;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new(new WindowsCaptureSourceDiscovery(), new CombinedCaptureCoordinator())
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
