namespace MediaScribeRecorder.Models;

public sealed class TranscriptionHistoryItem
{
    public string FileName { get; set; } = "";
    public string MediaPath { get; set; } = "";
    public string TranscriptPath { get; set; } = "";
    public string Language { get; set; } = "fr";
    public string Model { get; set; } = "small";
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string DisplayDate => CreatedAt.ToString("dd/MM/yyyy HH:mm");
}
