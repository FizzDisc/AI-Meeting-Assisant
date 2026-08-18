using System.Windows;
using Microsoft.Win32;
namespace AiMeetingAssistant.Desktop;
public partial class SettingsWindow : Window
{
    private readonly AppSettings _original=AppPreferences.Load();
    public SettingsWindow(){InitializeComponent();ScreenCaptureCheckBox.IsChecked=_original.ScreenCaptureEnabled;CaptureDirectoryTextBox.Text=_original.CaptureDirectory;ModelDirectoryTextBox.Text=_original.ModelDirectory??"";ComputeComboBox.ItemsSource=new[]{new Choice("Automatic","automatic"),new Choice("CPU only","cpu-only"),new Choice("Prefer NVIDIA CUDA","prefer-cuda")};ComputeComboBox.SelectedValue=_original.ComputePreference;SettingsPathText.Text=$"Settings file: {AppPreferences.FilePath}";}
    private void OnBrowseCapture(object s,RoutedEventArgs e){var d=new OpenFolderDialog{InitialDirectory=CaptureDirectoryTextBox.Text};if(d.ShowDialog(this)==true)CaptureDirectoryTextBox.Text=d.FolderName;}
    private void OnBrowseModel(object s,RoutedEventArgs e){var d=new OpenFolderDialog{InitialDirectory=ModelDirectoryTextBox.Text};if(d.ShowDialog(this)==true)ModelDirectoryTextBox.Text=d.FolderName;}
    private void OnSave(object s,RoutedEventArgs e){try{var v=new AppSettings(1,ScreenCaptureCheckBox.IsChecked==true,CaptureDirectoryTextBox.Text.Trim(),ModelDirectoryTextBox.Text.Trim(),(string?)ComputeComboBox.SelectedValue??"automatic");AppPreferences.Save(v);var restart=v.CaptureDirectory!=_original.CaptureDirectory||v.ModelDirectory!=_original.ModelDirectory||v.ComputePreference!=_original.ComputePreference;MessageBox.Show(this,restart?"Settings saved. Restart the application to apply storage, model, and compute changes.":"Settings saved.","Settings",MessageBoxButton.OK,MessageBoxImage.Information);DialogResult=true;}catch(Exception ex){ResultText.Text=ex.Message;}}
    private sealed record Choice(string Label,string Value);
}
