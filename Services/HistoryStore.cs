using System.Text.Json;
using MediaScribeRecorder.Models;

namespace MediaScribeRecorder.Services;

public sealed class HistoryStore
{
    private const int MaxItems = 50;
    private readonly PortableAppPaths paths;

    public HistoryStore(PortableAppPaths paths)
    {
        this.paths = paths;
    }

    public IReadOnlyList<TranscriptionHistoryItem> Load()
    {
        if (!File.Exists(paths.HistoryFile))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(paths.HistoryFile);
            return JsonSerializer.Deserialize<List<TranscriptionHistoryItem>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public IReadOnlyList<TranscriptionHistoryItem> Add(TranscriptionHistoryItem item)
    {
        var items = Load()
            .Where(existing => !existing.TranscriptPath.Equals(item.TranscriptPath, StringComparison.OrdinalIgnoreCase))
            .Prepend(item)
            .Take(MaxItems)
            .ToList();
        Save(items);
        return items;
    }

    private void Save(IReadOnlyList<TranscriptionHistoryItem> items)
    {
        Directory.CreateDirectory(paths.Settings);
        var json = JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(paths.HistoryFile, json);
    }
}
