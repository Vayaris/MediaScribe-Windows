namespace MediaScribeRecorder.Services;

public sealed class PortableAppPaths
{
    public PortableAppPaths()
    {
        var baseDir = AppContext.BaseDirectory;
        Root = baseDir;
        Recordings = Path.Combine(baseDir, "Recordings");
        Logs = Path.Combine(baseDir, "Logs");
        Settings = Path.Combine(baseDir, "Settings");
        Tools = Path.Combine(baseDir, "Tools");
        Models = Path.Combine(baseDir, "Models");

        Directory.CreateDirectory(Recordings);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Settings);
        Directory.CreateDirectory(Tools);
        Directory.CreateDirectory(Models);
    }

    public string Root { get; }
    public string Recordings { get; }
    public string Logs { get; }
    public string Settings { get; }
    public string Tools { get; }
    public string Models { get; }

    public string SettingsFile => Path.Combine(Settings, "settings.json");
    public string LogFile => Path.Combine(Logs, $"mediascribe-recorder-{DateTime.Now:yyyyMMdd}.log");
}
