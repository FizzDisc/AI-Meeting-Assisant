using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Controls;
using System.Windows.Data;
using AiMeetingAssistant.Core.Capture;
using AiMeetingAssistant.Core.Transcripts;
using AiMeetingAssistant.Core.Storage;
using AiMeetingAssistant.Desktop.ViewModels;
using AiMeetingAssistant.Windows.Capture;
using AiMeetingAssistant.Windows.Storage;
using AiMeetingAssistant.Windows.Worker;
using Microsoft.Win32;

namespace AiMeetingAssistant.Desktop;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private TranscriptWindowViewModel? _transcriptViewModel;
    private bool _isShuttingDown;
    private bool _shutdownComplete;

    public MainWindow()
    {
        InitializeComponent();
        StorageSessionsGrid.Columns.Insert(2, new DataGridTextColumn { Header = "Archive state", Binding = new Binding(nameof(SessionStorageEntry.ArchiveStatus)), Width = new DataGridLength(135) });
        StorageSessionsGrid.Columns.Insert(3, new DataGridTextColumn { Header = "Reclaimable", Binding = new Binding(nameof(SessionStorageEntry.ReclaimableLabel)), Width = new DataGridLength(105) });
        StorageSessionsGrid.Columns.Insert(4, new DataGridTextColumn { Header = "Next storage action", Binding = new Binding(nameof(SessionStorageEntry.ArchiveAction)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        var workerPath = Path.Combine(AppContext.BaseDirectory, "worker", "main.py");
        var settings = AppPreferences.Load();
        var pythonExecutable = PythonRuntimeResolver.Resolve();
        var modelPath = settings.ModelDirectory ?? LocalModelResolver.ResolveSpeechModel(settings.SpeechModelId);
        var diarizationModelPath = LocalModelResolver.ResolveDiarizationModel();
        _viewModel = new(new WindowsCaptureSourceDiscovery(), new CombinedCaptureCoordinator(settings.CaptureDirectory, systemAudioGain: settings.SystemAudioGain, microphoneGain: settings.MicrophoneGain), new PythonWorkerClient(pythonExecutable, workerPath), modelPath, settings.CaptureDirectory, settings.ComputePreference, diarizationModelPath, settings.SpeechModelId, new WindowsAudioEndpointHealthProbe(), new TeamsUiAutomationMuteStateProbe(), settings.LiveTranscriptionEnabled)
        {
            UIDispatcher = Dispatcher
        };
        _viewModel.ApplyCaptureGains(settings.SystemAudioGain, settings.MicrophoneGain);
        _viewModel.CaptureSessionCompleted += OnCaptureSessionCompleted;
        _viewModel.FullTranscriptionCompleted += OnFullTranscriptionCompleted;
        DataContext = _viewModel;
        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
        Closing += OnClosing;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var enabled = 1;
        var handle = new WindowInteropHelper(this).Handle;
        if (DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int)) != 0)
            _ = DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    private async void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        Loaded -= OnLoaded;
        await _viewModel.InitializeAsync();
    }

    private async void OnCaptureSessionCompleted(object? sender, string sessionDirectory)
    {
        if (!AppPreferences.Load().AutomaticFlacArchival) return;
        try { await new CaptureFlacCompressor(new FfmpegCaptureCompressionMediaTool(AppContext.BaseDirectory)).CompressSessionAsync(sessionDirectory);if(StorageWorkspace.Visibility==Visibility.Visible)await RefreshStorageAsync(); }
        catch (Exception exception) { _viewModel.AddExternalStatus("STORAGE", $"Automatic FLAC archival failed: {exception.Message}"); }
    }

    private async void OnFullTranscriptionCompleted(object? sender, string sessionDirectory)
    {
        if (!AppPreferences.Load().AutomaticProvenWavRemoval) return;
        try { var plan=PcmMasterRemoval.Preview(sessionDirectory);if(plan.Candidates.Count>0)await Task.Run(()=>PcmMasterRemoval.Execute(sessionDirectory));if(StorageWorkspace.Visibility==Visibility.Visible)await RefreshStorageAsync(); }
        catch (Exception exception) { _viewModel.AddExternalStatus("STORAGE", $"Automatic WAV removal failed: {exception.Message}"); }
    }

    private void OnOpenTranscript(object sender, RoutedEventArgs eventArgs)
    {
        var latestPath = _viewModel.FindLatestAvailableTranscriptPath();
        if (latestPath is not null) LoadEmbeddedTranscript(latestPath);
        else ShowEmptyTranscriptState();
        ShowWorkspace(TranscriptWorkspace);
    }

    private void OnOpenMeetingLibrary(object sender, RoutedEventArgs eventArgs) =>
        new MeetingLibraryWindow(_viewModel) { Owner = this }.ShowDialog();

    private void OnOpenHelp(object sender, RoutedEventArgs eventArgs) => new HelpWindow { Owner = this }.ShowDialog();

    private void OnNavigateCapture(object sender, RoutedEventArgs eventArgs) => ShowWorkspace(CaptureWorkspace);

    private void OnNavigateRecordings(object sender, RoutedEventArgs eventArgs) => ShowWorkspace(RecordingsWorkspace);

    private async void OnNavigateStorage(object sender, RoutedEventArgs eventArgs) { ShowWorkspace(StorageWorkspace); await RefreshStorageAsync(); }
    private async void OnRefreshStorage(object sender, RoutedEventArgs eventArgs) => await RefreshStorageAsync();
    private async void OnCompressSelectedStorage(object sender, RoutedEventArgs eventArgs)
    {
        if (StorageSessionsGrid.SelectedItem is not SessionStorageEntry selected)
        {
            MessageBox.Show(this, "Select a meeting session first.", "Verified FLAC", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var sessionDirectory = Path.Combine(AppPreferences.Load().CaptureDirectory, selected.SessionName);
        var plan = CaptureCompressionPlanner.AnalyzeSession(sessionDirectory);
        if (plan.Candidates.Count == 0)
        {
            MessageBox.Show(this, "This session has no eligible WAV masters. Existing archives and incomplete sessions are skipped.", "Verified FLAC", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(this, $"Create {plan.Candidates.Count} verified FLAC archive(s)?\n\nThe original WAV files will be retained. Each FLAC is decoded and checked before it is published.", "Confirm verified FLAC", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        StorageCompressButton.IsEnabled = false;
        try
        {
            var progress = new Progress<CaptureCompressionProgress>(value => StorageRootText.Text = $"{value.Stage}: {value.StreamKind} · {value.Percent:P0}");
            var compressor = new CaptureFlacCompressor(new FfmpegCaptureCompressionMediaTool(AppContext.BaseDirectory));
            var result = await compressor.CompressSessionAsync(sessionDirectory, progress);
            await RefreshStorageAsync();
            MessageBox.Show(this, $"Verified FLAC complete: {result.CompletedCount} created, {result.FailedCount} failed.\nSaved archive size versus WAV: {StorageInventory.Format(result.SavedBytes)}.\n\nOriginal WAV files were retained.", "Verified FLAC", MessageBoxButton.OK, result.FailedCount == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception exception) { MessageBox.Show(this, $"FLAC compression failed: {exception.Message}", "Verified FLAC", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { StorageCompressButton.IsEnabled = true; }
    }
    private async void OnRemoveProvenPcm(object sender, RoutedEventArgs eventArgs)
    {
        if (StorageSessionsGrid.SelectedItem is not SessionStorageEntry selected) { MessageBox.Show(this, "Select a meeting session first.", "Remove proven WAV", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var sessionDirectory = Path.Combine(AppPreferences.Load().CaptureDirectory, selected.SessionName);
        var plan = PcmMasterRemoval.Preview(sessionDirectory);
        if (plan.Candidates.Count == 0) { MessageBox.Show(this, plan.BlockReason ?? "No WAV masters are eligible.", "Remove proven WAV", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        if (MessageBox.Show(this, $"Permanently remove {plan.Candidates.Count} WAV master(s) and reclaim {StorageInventory.Format(plan.ReclaimableBytes)}?\n\nThe verified FLAC archives remain. A completed transcript has proven that these exact FLAC files can be processed. This cannot be undone.", "Confirm WAV removal", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try { var result = await Task.Run(() => PcmMasterRemoval.Execute(sessionDirectory)); await RefreshStorageAsync(); MessageBox.Show(this, $"Removed {result.DeletedFiles} proven WAV master(s) and reclaimed {StorageInventory.Format(result.ReclaimedBytes)}.", "WAV removal complete", MessageBoxButton.OK, MessageBoxImage.Information); }
        catch (Exception exception) { MessageBox.Show(this, $"WAV removal failed: {exception.Message}", "Remove proven WAV", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
    private async void OnPreviewStorageCleanup(object sender, RoutedEventArgs eventArgs)
    {
        var root=AppPreferences.Load().CaptureDirectory;var preview=await Task.Run(()=>IncrementalProcessingCleanup.PreviewLibrary(root));
        if(preview.Files==0){MessageBox.Show(this,"No reproducible processing data is currently eligible for cleanup.","Storage cleanup",MessageBoxButton.OK,MessageBoxImage.Information);return;}
        if(MessageBox.Show(this,$"Reclaim {StorageInventory.Format(preview.ReclaimableBytes)} across {preview.Files} temporary file(s) in {preview.Sessions} session(s)?\n\nCapture masters, final transcripts, speaker names and session metadata are preserved.","Confirm safe cleanup",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
        var result=await Task.Run(()=>IncrementalProcessingCleanup.CleanLibrary(root));await RefreshStorageAsync();MessageBox.Show(this,$"Reclaimed {StorageInventory.Format(result.ReclaimedBytes)} from {result.DeletedFiles} file(s)."+(result.Warnings.Count>0?$"\n{result.Warnings.Count} item(s) could not be removed.":""),"Storage cleanup",MessageBoxButton.OK,result.Warnings.Count>0?MessageBoxImage.Warning:MessageBoxImage.Information);
    }
    private async Task RefreshStorageAsync()
    {
        var selectedSession = (StorageSessionsGrid.SelectedItem as SessionStorageEntry)?.SessionName;
        StorageRootText.Text = "Scanning local meeting library...";
        try { var root=AppPreferences.Load().CaptureDirectory;var result=await Task.Run(()=>(Inventory:StorageInventory.Scan(root),Compression:CaptureCompressionPlanner.AnalyzeLibrary(root)));var report=result.Inventory;StorageRootText.Text=$"{report.Root} · {report.LibraryLabel} across {report.Sessions.Count} session(s) · {result.Compression.CandidateCount} FLAC candidate(s), approx. {result.Compression.EstimatedSavingsLabel} potential savings";StorageFreeText.Text=report.FreeLabel;StorageCapacityText.Text=report.CapacityLabel;StorageCaptureText.Text=report.CaptureLabel;StorageTranscriptText.Text=report.TranscriptLabel;StorageProcessingText.Text=report.ProcessingLabel;StorageSessionsGrid.ItemsSource=null;StorageSessionsGrid.ItemsSource=report.Sessions;StorageSessionsGrid.SelectedItem=report.Sessions.FirstOrDefault(item=>item.SessionName==selectedSession);StorageSessionsGrid.Items.Refresh(); }
        catch(Exception exception){StorageRootText.Text=$"Storage inventory failed: {exception.Message}";}
    }

    private void OnNavigateTranscript(object sender, RoutedEventArgs eventArgs)
    {
        var latestPath = _viewModel.FindLatestAvailableTranscriptPath();
        if (latestPath is not null) LoadEmbeddedTranscript(latestPath);
        else ShowEmptyTranscriptState();
        ShowWorkspace(TranscriptWorkspace);
    }

    private void ShowWorkspace(UIElement workspace)
    {
        CaptureWorkspace.Visibility = workspace == CaptureWorkspace ? Visibility.Visible : Visibility.Collapsed;
        RecordingsWorkspace.Visibility = workspace == RecordingsWorkspace ? Visibility.Visible : Visibility.Collapsed;
        TranscriptWorkspace.Visibility = workspace == TranscriptWorkspace ? Visibility.Visible : Visibility.Collapsed;
        StorageWorkspace.Visibility = workspace == StorageWorkspace ? Visibility.Visible : Visibility.Collapsed;
        StorageCleanupButton.Visibility = workspace == StorageWorkspace ? Visibility.Visible : Visibility.Collapsed;
        StorageCompressButton.Visibility = workspace == StorageWorkspace ? Visibility.Visible : Visibility.Collapsed;
        StorageReleasePcmButton.Visibility = workspace == StorageWorkspace ? Visibility.Visible : Visibility.Collapsed;
        CaptureTabButton.Tag = workspace == CaptureWorkspace ? "Active" : null;
        RecordingsTabButton.Tag = workspace == RecordingsWorkspace ? "Active" : null;
        TranscriptTabButton.Tag = workspace == TranscriptWorkspace ? "Active" : null;
        StorageTabButton.Tag = workspace == StorageWorkspace ? "Active" : null;
    }

    private void OnViewSelectedTranscript(object sender, RoutedEventArgs eventArgs)
    {
        var path = _viewModel.SelectedTranscriptRun?.Path;
        if (path is null) return;
        LoadEmbeddedTranscript(path);
        ShowWorkspace(TranscriptWorkspace);
    }

    private void LoadEmbeddedTranscript(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            _transcriptViewModel = new(TranscriptDocumentStore.Load(fullPath), fullPath, SpeakerNameStore.LoadForTranscript(fullPath));
            TranscriptContent.DataContext = _transcriptViewModel;
            TranscriptContent.Visibility = Visibility.Visible;
            EmptyTranscriptState.Visibility = Visibility.Collapsed;
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"Could not open transcript: {exception.Message}", "Transcript", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowEmptyTranscriptState()
    {
        _transcriptViewModel = null;
        TranscriptContent.DataContext = null;
        TranscriptContent.Visibility = Visibility.Collapsed;
        EmptyTranscriptState.Visibility = Visibility.Visible;
    }

    private async void OnDeleteSelectedRecording(object sender, RoutedEventArgs eventArgs)
    {
        var session = _viewModel.SelectedMeetingSession;
        if (session is null) return;
        var confirmation = MessageBox.Show(this, $"Delete the recording from {session.StartedLabel}?\n\nThis permanently removes its video, audio, transcript and session metadata.", "Delete recording", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes) return;
        try { await _viewModel.DeleteSelectedMeetingAsync(); }
        catch (Exception exception) { MessageBox.Show(this, $"Could not delete recording: {exception.Message}", "Delete recording", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void OnManageEmbeddedSpeakers(object sender, RoutedEventArgs eventArgs)
    {
        if (_transcriptViewModel is null) return;
        var speakerIds = _transcriptViewModel.Document.Segments.Select(segment => segment.Speaker).Where(speaker => speaker == "You" || (speaker?.StartsWith("SPEAKER_", StringComparison.Ordinal) ?? false)).Cast<string>().Distinct(StringComparer.Ordinal).OrderBy(speaker => speaker == "You" ? "" : speaker).ToArray();
        var dialog = new SpeakerNamesWindow(speakerIds, _transcriptViewModel.SpeakerNames) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        SpeakerNameStore.SaveForTranscript(_transcriptViewModel.SourcePath, dialog.SpeakerNames);
        _transcriptViewModel.UpdateSpeakerNames(SpeakerNameStore.LoadForTranscript(_transcriptViewModel.SourcePath));
    }

    private void OnPreviousTranscriptMatch(object sender, RoutedEventArgs eventArgs){_transcriptViewModel?.GoToPreviousMatch();if(_transcriptViewModel?.SelectedSegment is not null)TranscriptSegmentsList.ScrollIntoView(_transcriptViewModel.SelectedSegment);}
    private void OnNextTranscriptMatch(object sender, RoutedEventArgs eventArgs){_transcriptViewModel?.GoToNextMatch();if(_transcriptViewModel?.SelectedSegment is not null)TranscriptSegmentsList.ScrollIntoView(_transcriptViewModel.SelectedSegment);}

    private void OnExportEmbeddedMarkdown(object sender, RoutedEventArgs eventArgs)
    {
        if (_transcriptViewModel is null) return;
        var dialog = CreateTranscriptExportDialog("Markdown document|*.md", ".md");
        if (dialog.ShowDialog(this) != true) return;
        TranscriptDocumentStore.ExportMarkdownAtomic(dialog.FileName, _transcriptViewModel.Document, _transcriptViewModel.SpeakerNames);
    }

    private void OnExportEmbeddedJson(object sender, RoutedEventArgs eventArgs)
    {
        if (_transcriptViewModel is null) return;
        var dialog = CreateTranscriptExportDialog("JSON document|*.json", ".json");
        if (dialog.ShowDialog(this) != true) return;
        TranscriptDocumentStore.ExportJsonAtomic(dialog.FileName, _transcriptViewModel.Document);
    }

    private SaveFileDialog CreateTranscriptExportDialog(string filter, string extension) => new()
    {
        Title = "Export meeting transcript", Filter = filter, DefaultExt = extension, AddExtension = true,
        FileName = $"transcript_{DateTime.Now:yyyyMMdd_HHmmss}{extension}",
        InitialDirectory = _transcriptViewModel is null ? null : Path.GetDirectoryName(_transcriptViewModel.SourcePath)
    };

    private void OnOpenSettings(object sender, RoutedEventArgs eventArgs)
    {
        if (new SettingsWindow { Owner = this }.ShowDialog() == true)
        {
            var settings = AppPreferences.Load();
            _viewModel.IsScreenCaptureEnabled = settings.ScreenCaptureEnabled;
            var modelPath = settings.ModelDirectory ?? LocalModelResolver.ResolveSpeechModel(settings.SpeechModelId);
            _viewModel.ApplyProcessingSettings(modelPath, settings.ComputePreference, settings.SpeechModelId);
            _viewModel.ApplyCaptureGains(settings.SystemAudioGain, settings.MicrophoneGain);
            _viewModel.LiveTranscriptionEnabled = settings.LiveTranscriptionEnabled;
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
