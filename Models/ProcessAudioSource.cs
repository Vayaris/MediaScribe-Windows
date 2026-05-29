using System.Windows.Media;

namespace MediaScribeRecorder.Models;

public sealed record ProcessAudioSource(int ProcessId, int CaptureProcessId, string Title, string ProcessName, ImageSource? Icon)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Title)
        ? $"{ProcessName} ({ProcessId})"
        : $"{Title} - {ProcessName} ({ProcessId})";

    public string CaptureHint => CaptureProcessId == ProcessId
        ? ""
        : $"Capture PID racine: {CaptureProcessId}";
}
