using System.Net.Http;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using AiMeetingAssistant.Windows.Worker;
using Microsoft.Win32;

namespace AiMeetingAssistant.Desktop;

public partial class WelcomeWindow : Window
{
    private readonly AppSettings _original = AppPreferences.Load();
    private int _step;
    private bool _runtimeReady;
    private bool _recordingOnly;
    private CancellationTokenSource? _operation;
    public bool ContinueToApp { get; private set; }
    private sealed record ComputeChoice(string DisplayName, string Value);

    public WelcomeWindow()
    {
        InitializeComponent();
        RecordingFolder.Text = _original.CaptureDirectory;
        ScreenCapture.IsChecked = _original.ScreenCaptureEnabled;
        LoadRuntimePackage();
        Loaded += async (_, _) =>
        {
            if (File.Exists(PythonRuntimeResolver.Resolve())) await Operate(CheckRuntimeAsync);
        };
        Models.ItemsSource = LocalModelResolver.SpeechModels;
        Models.SelectedValue = _original.SpeechModelId;
        Compute.ItemsSource = new[] { new ComputeChoice("Automatic · CPU / NVIDIA when available", "automatic"),
            new ComputeChoice("CPU only · broadest compatibility", "cpu-only"),
            new ComputeChoice("Prefer NVIDIA CUDA · CPU fallback", "prefer-cuda") };
        Compute.SelectedValue = _original.ComputePreference == "intel-gpu" ? "automatic" : _original.ComputePreference;
        Closing += (_, e) =>
        {
            if (_operation is null) return;
            e.Cancel = true;
            Status.Text = "Cancel the current operation before closing setup.";
        };
        ShowStep();
    }

    private void ShowStep()
    {
        var pages = new[] { WelcomePage, StoragePage, AiPage, FinishPage };
        for (var index = 0; index < pages.Length; index++) pages[index].Visibility = index == _step ? Visibility.Visible : Visibility.Collapsed;
        Heading.Text = new[] { "Welcome. Let's get you set up.", "Make room for your meetings.", "Choose how to transcribe.", "Ready for your first meeting." }[_step];
        StepLabel.Text = $"Step {_step + 1} of 4 · Welcome / Storage / Local AI / Ready";
        BackButton.IsEnabled = _step > 0;
        NextButton.Content = _step == 3 ? "Start using the app" : "Next";
        if (_step == 3)
        {
            var model = LocalModelResolver.GetSpeechModel(Models.SelectedValue as string);
            Summary.Text = $"Recordings: {RecordingFolder.Text}\n\nScreen recording: {(ScreenCapture.IsChecked == true ? "On" : "Off")}\n\n"
                + (_recordingOnly ? "Recording only. Live transcription is off; AI can be added later."
                    : $"Local AI: checked\nSpeech model: {model.DisplayName}\nProcessing: {(Compute.SelectedItem as ComputeChoice)?.DisplayName}")
                + "\n\nSpeaker identification: optional; configure it in Settings.";
        }
    }

    private async void Next(object sender, RoutedEventArgs e)
    {
        if (_step == 3)
        {
            if (_recordingOnly) { SaveAndOpen(true, true); return; }
            await Operate(async token =>
            {
                await CheckRuntimeAsync(token);
                if (!_runtimeReady || !SelectedModelReady()) throw new InvalidOperationException("AI setup is not ready. Go back to install missing components, or choose Recording only.");
                SaveAndOpen(false, true);
            });
            return;
        }
        try
        {
            if (_step == 1) FirstRunSetup.CheckWritableDirectory(RecordingFolder.Text.Trim());
            if (_step == 2)
            {
                await Operate(async token =>
                {
                    await CheckRuntimeAsync(token);
                    if (!_runtimeReady || !SelectedModelReady()) throw new InvalidOperationException("Install the missing AI components/model, or choose Recording only to continue without downloads.");
                    _recordingOnly = false;
                    _step++;
                    ShowStep();
                });
                return;
            }
            _step++;
            Status.Text = "";
            ShowStep();
        }
        catch (Exception error) { Status.Text = error.Message; }
    }

    private void Back(object sender, RoutedEventArgs e) { if (_step > 0) _step--; Status.Text = ""; ShowStep(); }
    private void RecordingOnly(object sender, RoutedEventArgs e)
    {
        try { FirstRunSetup.CheckWritableDirectory(RecordingFolder.Text.Trim()); _recordingOnly = true; _step = 3; Status.Text = ""; ShowStep(); }
        catch (Exception error) { Status.Text = error.Message; }
    }
    private void Later(object sender, RoutedEventArgs e) => SaveAndOpen(true, false);

    private void SaveAndOpen(bool recordingOnly, bool completed)
    {
        try
        {
            FirstRunSetup.CheckWritableDirectory(RecordingFolder.Text.Trim());
            AppPreferences.Save(_original with { CaptureDirectory = RecordingFolder.Text.Trim(),
                ScreenCaptureEnabled = ScreenCapture.IsChecked == true, SetupCompleted = completed,
                LiveTranscriptionEnabled = !recordingOnly,
                SpeechModelId = recordingOnly ? _original.SpeechModelId : (string)Models.SelectedValue,
                ModelDirectory = recordingOnly ? _original.ModelDirectory : null,
                ComputePreference = recordingOnly ? _original.ComputePreference : (string)Compute.SelectedValue });
            ContinueToApp = true;
            // Completion can occur inside an async operation; release the close guard first.
            _operation?.Dispose();
            _operation = null;
            DialogResult = true;
        }
        catch (Exception error) { Status.Text = error.Message; }
    }

    private void BrowseStorage(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog();
        if (dialog.ShowDialog(this) == true) RecordingFolder.Text = dialog.FolderName;
    }
    private void LoadRuntimePackage()
    {
        var free = new DriveInfo(Path.GetPathRoot(UserAiPaths.Root)!).AvailableFreeSpace;
        RuntimeDetails.Text = $"Automatic setup from python.org, PyPI and PyTorch. No separate installer needed."
            + $"\nAllow 6 GiB of free space during setup · Available: {FormatBytes(free)}"
            + "\nDownloads show the size of the current file. Speech models are downloaded separately.";
    }
    private static string FormatBytes(long bytes) => bytes >= 1_073_741_824
        ? $"{bytes / 1_073_741_824d:N2} GiB" : $"{bytes / 1_048_576d:N1} MiB";

    private async void InstallRuntime(object sender, RoutedEventArgs e) => await Operate(async token =>
    {
        _runtimeReady = false;
        using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AI-Meeting-Assistant/1.0");
        var progress = new Progress<RuntimeInstallProgress>(update =>
        {
            if (_operation is null) return;
            BusyProgress.IsIndeterminate = update.TotalBytes <= 0 || update.Phase == "Connecting";
            BusyProgress.Value = update.TotalBytes > 0 ? 100d * update.CompletedBytes / update.TotalBytes : 0;
            var details = update.TotalBytes > 0
                ? $" · {FormatBytes(update.CompletedBytes)} / {FormatBytes(update.TotalBytes)}" : "";
            if (update.BytesPerSecond > 0)
            {
                details += $" · {update.BytesPerSecond / 1_048_576:N1} MiB/s";
                var remaining = TimeSpan.FromSeconds(Math.Max(0, update.TotalBytes - update.CompletedBytes) / update.BytesPerSecond);
                details += $" · about {Math.Ceiling(remaining.TotalMinutes):N0} min remaining";
            }
            RuntimeStatus.Text = Status.Text = update.Phase + details;
        });
        await PublicRuntimeSetup.InstallAsync(client, UserAiPaths.Root, WorkerFile("runtime-requirements-win-x64.txt"), progress, token);
        BusyProgress.IsIndeterminate = true;
        await CheckRuntimeAsync(token);
        if (!_runtimeReady) throw new InvalidOperationException("The AI components could not be started. Check the status and retry.");
        Status.Text = "AI components are ready. Choose and download a speech model below.";
    });
    private async void InstallModel(object sender, RoutedEventArgs e) => await Operate(async token =>
    {
        await CheckRuntimeAsync(token);
        if (!_runtimeReady) throw new InvalidOperationException("Download the AI components first.");
        if (SelectedModelReady()) { Status.Text = "The selected model is already installed."; return; }
        var model = LocalModelResolver.GetSpeechModel(Models.SelectedValue as string);
        await FirstRunSetup.InstallModelAsync(WorkerFile("manage_speech_model.py"), model.Id, Report, token);
        UpdateModel();
        if (!SelectedModelReady()) throw new InvalidOperationException("The downloaded model is incomplete. Retry the installation.");
        Status.Text = "Speech model installed. You can continue.";
    });
    private async Task CheckRuntimeAsync(CancellationToken token)
    {
        _runtimeReady = false;
        if (!File.Exists(PythonRuntimeResolver.Resolve()))
        {
            RuntimeStatus.Text = "Download the AI components first.";
            return;
        }
        RuntimeStatus.Text = "Checking AI components and available hardware...";
        await using var worker = new PythonWorkerClient(PythonRuntimeResolver.Resolve(), WorkerFile("main.py"));
        var health = await worker.CheckHealthAsync(token);
        _runtimeReady = health.RuntimeSupported && health.MlReady;
        RuntimeDownloadButton.Content = _runtimeReady ? "AI components installed" : "Download AI components";
        RuntimeDownloadButton.IsEnabled = !_runtimeReady;
        RuntimeStatus.Text = _runtimeReady ? $"Ready · Python {health.PythonVersion} · {health.Diagnostics.Compute.Mode}"
            : $"Setup required · Python {health.PythonVersion} · Missing: {string.Join(", ", health.Diagnostics.MissingRequirements)}"
                + (health.RuntimeSupported ? "" : " · Python version is not supported.");
        UpdateModel();
    }
    private static string WorkerFile(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "worker", name);
        if (!File.Exists(path)) throw new FileNotFoundException("The installation is missing a setup component. Reinstall the application.", path);
        return path;
    }
    private bool SelectedModelReady() => FirstRunSetup.HasModel(LocalModelResolver.ResolveSpeechModel(Models.SelectedValue as string));
    private void ModelChanged(object sender, SelectionChangedEventArgs e) => UpdateModel();
    private void UpdateModel()
    {
        if (Models.SelectedItem is not LocalSpeechModelDefinition model || ModelDetails is null) return;
        ModelDetails.Text = $"{model.Quality} accuracy · Download {model.DownloadSize}\n{model.HardwareGuidance}\nTiny is a quick starting point; Small prioritizes accuracy.";
        ModelStatus.Text = SelectedModelReady() ? "Installed and available." : "Not installed yet.";
    }
    private void Report(string line) => Dispatcher.InvokeAsync(() =>
    {
        if (_operation is null) return;
        try
        {
            using var json = JsonDocument.Parse(line);
            var root = json.RootElement;
            if (root.TryGetProperty("status", out var phase) && phase.GetString() == "validating")
            { Status.Text = "Download finished. Validating the model..."; return; }

            if (root.TryGetProperty("downloadedBytes", out var done) && root.TryGetProperty("totalBytes", out var total))
            { Status.Text = $"Downloading model · {done.GetInt64() / 1048576:N0} / {total.GetInt64() / 1048576:N0} MB"; return; }
        }
        catch (JsonException) { }
        Status.Text = line.Length > 500 ? line[..500] : line;
    });
    private async Task Operate(Func<CancellationToken, Task> action)
    {
        if (_operation is not null) return;
        _operation = new CancellationTokenSource();
        BusyProgress.IsIndeterminate = true;
        BusyProgress.Value = 0;
        SetBusy(true);
        Status.Text = "Working...";
        try { await action(_operation.Token); }
        catch (OperationCanceledException) { Status.Text = "Cancelled. You can retry or continue with recording only."; }
        catch (Exception error) { Status.Text = error.Message.Length > 700 ? error.Message[^700..] : error.Message; RuntimeStatus.Text = "Setup needs attention. Check the message below and retry."; }
        finally { _operation?.Dispose(); _operation = null; SetBusy(false); }
    }
    private void SetBusy(bool busy)
    {
        Pages.IsEnabled = !busy;
        NextButton.IsEnabled = BackButton.IsEnabled = LaterButton.IsEnabled = RecordingOnlyButton.IsEnabled = !busy;
        BackButton.IsEnabled = !busy && _step > 0;
        BusyProgress.Visibility = CancelOperation.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }
    private void CancelSetup(object sender, RoutedEventArgs e) => _operation?.Cancel();
}
