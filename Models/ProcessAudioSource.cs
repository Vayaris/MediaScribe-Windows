using System.Windows.Media;

namespace MediaScribeRecorder.Models;

public sealed record ProcessAudioSource(int ProcessId, string Title, string ProcessName, ImageSource? Icon)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Title)
        ? $"{ProcessName} ({ProcessId})"
        : $"{Title} - {ProcessName} ({ProcessId})";
}
