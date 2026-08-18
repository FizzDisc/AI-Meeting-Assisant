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
        var settings = AppPreferences.Load();
        var pythonExecutable = PythonRuntimeResolver.Resolve();
        var modelPath = settings.ModelDirectory ?? LocalModelResolver.ResolveSpeechModel(settings.SpeechModelId);
        var diarizationModelPath = LocalModelResolver.ResolveDiarizationModel();
        _viewModel = new(new WindowsCaptureSourceDiscovery(), new CombinedCaptureCoordinator(settings.CaptureDirectory), new PythonWorkerClient(pythonExecutable, workerPath), modelPath, settings.CaptureDirectory, settings.ComputePreference, diarizationModelPath, settings.SpeechModelId)
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

    private void OnOpenTranscript(object sender, RoutedEventArgs eventArgs)
    {
        if (!_viewModel.HasTranscript || _viewModel.TranscriptPath is null) return;
        try
        {
            new TranscriptWindow(_viewModel.TranscriptPath) { Owner = this }.ShowDialog();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"Could not open transcript: {exception.Message}", "Transcript",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnOpenMeetingLibrary(object sender, RoutedEventArgs eventArgs) =>
        new MeetingLibraryWindow(_viewModel) { Owner = this }.ShowDialog();

    private void OnOpenSettings(object sender, RoutedEventArgs eventArgs)
    {
        if (new SettingsWindow { Owner = this }.ShowDialog() == true)
        {
            var settings = AppPreferences.Load();
            _viewModel.IsScreenCaptureEnabled = settings.ScreenCaptureEnabled;
            var modelPath = settings.ModelDirectory ?? LocalModelResolver.ResolveSpeechModel(settings.SpeechModelId);
            _viewModel.ApplyProcessingSettings(modelPath, settings.ComputePreference, settings.SpeechModelId);
        }
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
                _ = Dispatcher.BeginInvoke(() => Application.Current.Shutdown());
        }
    }
}
