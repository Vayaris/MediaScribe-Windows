namespace MediaScribeRecorder.Models;

public sealed class RecordingSettings
{
    public string OutputFolder { get; set; } = "";
    public string MicrophoneDeviceId { get; set; } = "";
    public string LastSystemMode { get; set; } = "desktop";
    public bool IncludeSystemAudio { get; set; } = true;
    public string TranscriptionLanguage { get; set; } = "fr";
    public string WhisperModel { get; set; } = "small";
    public double SystemGain { get; set; } = 1.25;
    public double MicrophoneGain { get; set; } = 0.65;
}
