using System.Windows;
using System.Windows.Controls;
using MediaScribeRecorder.Models;

namespace MediaScribeRecorder;

public partial class SettingsWindow : Window
{
    public SettingsWindow(RecordingSettings settings)
    {
        InitializeComponent();
        Settings = new RecordingSettings
        {
            OutputFolder = settings.OutputFolder,
            MicrophoneDeviceId = settings.MicrophoneDeviceId,
            LastSystemMode = settings.LastSystemMode,
            IncludeSystemAudio = settings.IncludeSystemAudio,
            TranscriptionLanguage = settings.TranscriptionLanguage,
            WhisperModel = settings.WhisperModel,
            SystemGain = settings.SystemGain,
            MicrophoneGain = settings.MicrophoneGain,
        };

        SystemGainSlider.Value = Settings.SystemGain;
        MicrophoneGainSlider.Value = Settings.MicrophoneGain;
        ModelComboBox.SelectedValue = string.IsNullOrWhiteSpace(Settings.WhisperModel) ? "small" : Settings.WhisperModel;
        UpdateGainLabels();
    }

    public RecordingSettings Settings { get; private set; }

    private void OnGainChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateGainLabels();
    }

    private void UpdateGainLabels()
    {
        if (SystemGainText is null || MicrophoneGainText is null)
        {
            return;
        }

        SystemGainText.Text = $"{SystemGainSlider.Value:0.00}x";
        MicrophoneGainText.Text = $"{MicrophoneGainSlider.Value:0.00}x";
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        Settings.SystemGain = Math.Round(SystemGainSlider.Value, 2);
        Settings.MicrophoneGain = Math.Round(MicrophoneGainSlider.Value, 2);
        Settings.WhisperModel = ModelComboBox.SelectedValue as string ?? "small";
        DialogResult = true;
    }
}
