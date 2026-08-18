using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using AiMeetingAssistant.Windows.Worker;
using Microsoft.Win32;
namespace AiMeetingAssistant.Desktop;
public partial class SettingsWindow : Window
{
    private readonly AppSettings _original=AppPreferences.Load();
    public SettingsWindow()
    {
        InitializeComponent();ScreenCaptureCheckBox.IsChecked=_original.ScreenCaptureEnabled;CaptureDirectoryTextBox.Text=_original.CaptureDirectory;ModelDirectoryTextBox.Text=_original.ModelDirectory??"";
        SpeechModelComboBox.ItemsSource=LocalModelResolver.SpeechModels.Select(m=>new SpeechModelChoice(m,LocalModelResolver.IsSpeechModelInstalled(m.Id))).ToArray();SpeechModelComboBox.SelectedValue=_original.SpeechModelId;
        ComputeComboBox.ItemsSource=new[]{new Choice("Automatic","automatic"),new Choice("CPU only","cpu-only"),new Choice("Prefer NVIDIA CUDA (fallback to CPU)","prefer-cuda")};ComputeComboBox.SelectedValue=_original.ComputePreference;SettingsPathText.Text=$"Settings file: {AppPreferences.FilePath}";UpdateSpeechModelDetails();
    }
    private void OnSpeechModelChanged(object sender,SelectionChangedEventArgs e)=>UpdateSpeechModelDetails();
    private void UpdateSpeechModelDetails(){if(SpeechModelComboBox.SelectedItem is not SpeechModelChoice s)return;SpeechModelStatusText.Text=s.Installed?"Installed · ready to select":"Not installed";SpeechModelStatusText.Foreground=(System.Windows.Media.Brush)FindResource(s.Installed?"AccentBrush":"MutedBrush");SpeechModelDetailText.Text=$"Quality: {s.Quality} · Download: {s.DownloadSize}\n{s.HardwareGuidance}\nLocal folder: {s.DirectoryName}";}
    private void OnBrowseCapture(object s,RoutedEventArgs e){var d=new OpenFolderDialog{InitialDirectory=CaptureDirectoryTextBox.Text};if(d.ShowDialog(this)==true)CaptureDirectoryTextBox.Text=d.FolderName;}
    private void OnBrowseModel(object s,RoutedEventArgs e){var d=new OpenFolderDialog{InitialDirectory=ModelDirectoryTextBox.Text};if(d.ShowDialog(this)==true)ModelDirectoryTextBox.Text=d.FolderName;}
    private async void OnInstallDiarization(object sender,RoutedEventArgs e)
    {
        var token=HuggingFaceTokenBox.Password;HuggingFaceTokenBox.Clear();if(!token.StartsWith("hf_",StringComparison.Ordinal)||token.Length<23){DiarizationInstallStatus.Text="Enter the complete token beginning with hf_.";return;}InstallDiarizationButton.IsEnabled=false;DiarizationInstallStatus.Text="Downloading and validating the local diarization model...";
        try{var script=FindWorkerFile("download_diarization_model.py");var target=Path.Combine(Path.GetDirectoryName(script)!,"models","speaker-diarization-community-1");var start=new ProcessStartInfo(PythonRuntimeResolver.Resolve()){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};start.ArgumentList.Add(script);start.ArgumentList.Add("--target");start.ArgumentList.Add(target);start.Environment["HF_TOKEN"]=token;token=string.Empty;using var process=Process.Start(start)??throw new InvalidOperationException("Could not start the model downloader.");var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();if(process.ExitCode!=0)throw new InvalidOperationException((await error).Trim());DiarizationInstallStatus.Text=$"Installed locally: {target}";}catch(Exception exception){DiarizationInstallStatus.Text=$"Installation failed: {exception.Message}";}finally{InstallDiarizationButton.IsEnabled=true;}
    }
    private static string FindWorkerFile(string name){foreach(var start in new[]{Environment.CurrentDirectory,AppContext.BaseDirectory})for(var d=new DirectoryInfo(start);d is not null;d=d.Parent){var candidate=Path.Combine(d.FullName,"worker",name);if(File.Exists(candidate))return candidate;}throw new FileNotFoundException($"Worker installer file not found: {name}");}
    private void OnSave(object sender,RoutedEventArgs e)
    {
        try{var selected=SpeechModelComboBox.SelectedItem as SpeechModelChoice??throw new InvalidOperationException("Select a speech model.");var custom=ModelDirectoryTextBox.Text.Trim();if(custom.Length==0&&!selected.Installed)throw new InvalidOperationException($"{selected.DisplayName} is not installed yet. Choose an installed model or wait for Sprint 3.4.2 installation support.");var value=new AppSettings(1,ScreenCaptureCheckBox.IsChecked==true,CaptureDirectoryTextBox.Text.Trim(),custom,(string?)ComputeComboBox.SelectedValue??"automatic",selected.Id);AppPreferences.Save(value);var restart=value.CaptureDirectory!=_original.CaptureDirectory||value.ModelDirectory!=_original.ModelDirectory||value.ComputePreference!=_original.ComputePreference||value.SpeechModelId!=_original.SpeechModelId;MessageBox.Show(this,restart?"Settings saved. Restart the application to apply storage, model, and compute changes.":"Settings saved.","Settings",MessageBoxButton.OK,MessageBoxImage.Information);DialogResult=true;}catch(Exception exception){ResultText.Text=exception.Message;}
    }
    private sealed record Choice(string Label,string Value);
    private sealed record SpeechModelChoice(string Id,string DisplayName,string DirectoryName,string Quality,string DownloadSize,string HardwareGuidance,bool Installed)
    {public SpeechModelChoice(LocalSpeechModelDefinition m,bool installed):this(m.Id,m.DisplayName,m.DirectoryName,m.Quality,m.DownloadSize,m.HardwareGuidance,installed){}public string InstallLabel=>Installed?"· Installed":"· Not installed";}
}
