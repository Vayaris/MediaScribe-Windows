using System.Text.Json.Serialization;

namespace MediaScribeRecorder.Models;

public sealed class TranscriptionHistoryItem
{
    public string FileName { get; set; } = "";
    public string MediaPath { get; set; } = "";
    public string MixPath { get; set; } = "";
    public string MicrophonePath { get; set; } = "";
    public string SystemPath { get; set; } = "";
    public string TranscriptPath { get; set; } = "";
    public string RecordingFolder { get; set; } = "";
    public string Language { get; set; } = "fr";
    public string Model { get; set; } = "small";
    public string TranscriptMode { get; set; } = "Transcript normal";
    public bool IsSuspicious { get; set; }
    public string SuspicionReason { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [JsonIgnore]
    public string DisplayDate => CreatedAt.ToString("dd/MM/yyyy HH:mm");

    [JsonIgnore]
    public string DisplayWarning => IsSuspicious ? "Transcription suspecte" : "";

    [JsonIgnore]
    public string DisplayMode => string.IsNullOrWhiteSpace(TranscriptMode) ? "Transcript normal" : TranscriptMode;

    [JsonIgnore]
    public string PreviewPath => !string.IsNullOrWhiteSpace(MixPath) ? MixPath : MediaPath;
}
