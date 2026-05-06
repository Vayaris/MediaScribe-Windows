using System.Text.Json;
using MediaScribeRecorder.Models;

namespace MediaScribeRecorder.Services;

public sealed class SettingsStore
{
    private readonly PortableAppPaths paths;

    public SettingsStore(PortableAppPaths paths)
    {
        this.paths = paths;
    }

    public RecordingSettings Load()
    {
        if (!File.Exists(paths.SettingsFile))
        {
            return new RecordingSettings { OutputFolder = paths.Recordings };
        }

        try
        {
            var json = File.ReadAllText(paths.SettingsFile);
            var settings = JsonSerializer.Deserialize<RecordingSettings>(json) ?? new RecordingSettings();
            if (string.IsNullOrWhiteSpace(settings.OutputFolder))
            {
                settings.OutputFolder = paths.Recordings;
            }

            return settings;
        }
        catch
        {
            return new RecordingSettings { OutputFolder = paths.Recordings };
        }
    }

    public void Save(RecordingSettings settings)
    {
        Directory.CreateDirectory(paths.Settings);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(paths.SettingsFile, json);
    }
}
