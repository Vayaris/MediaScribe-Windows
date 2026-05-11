namespace MediaScribeRecorder.Services;

public sealed class PortableAppPaths
{
    public PortableAppPaths()
    {
        var baseDir = AppContext.BaseDirectory;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var userRoot = Path.Combine(appData, "MediaScribe Recorder");
        Root = baseDir;
        UserRoot = userRoot;
        Recordings = Path.Combine(userRoot, "Recordings");
        Logs = Path.Combine(userRoot, "Logs");
        Settings = Path.Combine(userRoot, "Settings");
        Tools = Path.Combine(baseDir, "Tools");
        Models = Path.Combine(baseDir, "Models");

        Directory.CreateDirectory(UserRoot);
        Directory.CreateDirectory(Recordings);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Settings);
        Directory.CreateDirectory(Tools);
        Directory.CreateDirectory(Models);
    }

    public string Root { get; }
    public string UserRoot { get; }
    public string Recordings { get; }
    public string Logs { get; }
    public string Settings { get; }
    public string Tools { get; }
    public string Models { get; }

    public string SettingsFile => Path.Combine(Settings, "settings.json");
    public string HistoryFile => Path.Combine(Settings, "history.json");
    public string LogFile => Path.Combine(Logs, $"mediascribe-recorder-{DateTime.Now:yyyyMMdd}.log");
}
