using System.Windows;
using AiMeetingAssistant.Core.Capture;
using AiMeetingAssistant.Desktop.ViewModels;
using AiMeetingAssistant.Windows.Capture;

namespace AiMeetingAssistant.Desktop;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new(new WindowsCaptureSourceDiscovery(), new SprintOneSimulationCaptureCoordinator());
        DataContext = _viewModel;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        Loaded -= OnLoaded;
        await _viewModel.InitializeAsync();
    }
}
