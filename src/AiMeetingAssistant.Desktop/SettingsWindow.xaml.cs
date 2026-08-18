using System.Windows;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using AiMeetingAssistant.Windows.Worker;
namespace AiMeetingAssistant.Desktop;
public partial class SettingsWindow : Window
{
    private readonly AppSettings _original=AppPreferences.Load();
    public SettingsWindow(){InitializeComponent();ScreenCaptureCheckBox.IsChecked=_original.ScreenCaptureEnabled;CaptureDirectoryTextBox.Text=_original.CaptureDirectory;ModelDirectoryTextBox.Text=_original.ModelDirectory??"";ComputeComboBox.ItemsSource=new[]{new Choice("Automatic","automatic"),new Choice("CPU only","cpu-only"),new Choice("Prefer NVIDIA CUDA (fallback to CPU)","prefer-cuda")};ComputeComboBox.SelectedValue=_original.ComputePreference;SettingsPathText.Text=$"Settings file: {AppPreferences.FilePath}";}
    private void OnBrowseCapture(object s,RoutedEventArgs e){var d=new OpenFolderDialog{InitialDirectory=CaptureDirectoryTextBox.Text};if(d.ShowDialog(this)==true)CaptureDirectoryTextBox.Text=d.FolderName;}
    private void OnBrowseModel(object s,RoutedEventArgs e){var d=new OpenFolderDialog{InitialDirectory=ModelDirectoryTextBox.Text};if(d.ShowDialog(this)==true)ModelDirectoryTextBox.Text=d.FolderName;}
    private async void OnInstallDiarization(object sender,RoutedEventArgs e)
    {
        var token=HuggingFaceTokenBox.Password;HuggingFaceTokenBox.Clear();
        if(!token.StartsWith("hf_",StringComparison.Ordinal)||token.Length<23){DiarizationInstallStatus.Text="Enter the complete token beginning with hf_.";return;}
        InstallDiarizationButton.IsEnabled=false;DiarizationInstallStatus.Text="Downloading and validating the local diarization model...";
        try
        {
            var script=FindWorkerFile("download_diarization_model.py");var target=Path.Combine(Path.GetDirectoryName(script)!,"models","speaker-diarization-community-1");
            var start=new ProcessStartInfo(PythonRuntimeResolver.Resolve()){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            start.ArgumentList.Add(script);start.ArgumentList.Add("--target");start.ArgumentList.Add(target);start.Environment["HF_TOKEN"]=token;token=string.Empty;
            using var process=Process.Start(start)??throw new InvalidOperationException("Could not start the model downloader.");
            var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();
            if(process.ExitCode!=0)throw new InvalidOperationException((await error).Trim());
            DiarizationInstallStatus.Text=$"Installed locally: {target}";
        }
        catch(Exception exception){DiarizationInstallStatus.Text=$"Installation failed: {exception.Message}";}
        finally{InstallDiarizationButton.IsEnabled=true;}
    }
    private static string FindWorkerFile(string name)
    {
        foreach(var start in new[]{Environment.CurrentDirectory,AppContext.BaseDirectory})for(var directory=new DirectoryInfo(start);directory is not null;directory=directory.Parent){var candidate=Path.Combine(directory.FullName,"worker",name);if(File.Exists(candidate))return candidate;}
        throw new FileNotFoundException($"Worker installer file not found: {name}");
    }
    private void OnSave(object s,RoutedEventArgs e){try{var v=new AppSettings(1,ScreenCaptureCheckBox.IsChecked==true,CaptureDirectoryTextBox.Text.Trim(),ModelDirectoryTextBox.Text.Trim(),(string?)ComputeComboBox.SelectedValue??"automatic");AppPreferences.Save(v);var restart=v.CaptureDirectory!=_original.CaptureDirectory||v.ModelDirectory!=_original.ModelDirectory||v.ComputePreference!=_original.ComputePreference;MessageBox.Show(this,restart?"Settings saved. Restart the application to apply storage, model, and compute changes.":"Settings saved.","Settings",MessageBoxButton.OK,MessageBoxImage.Information);DialogResult=true;}catch(Exception ex){ResultText.Text=ex.Message;}}
    private sealed record Choice(string Label,string Value);
}
