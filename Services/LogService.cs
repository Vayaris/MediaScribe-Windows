namespace MediaScribeRecorder.Services;

public sealed class LogService
{
    private readonly PortableAppPaths paths;
    private readonly object sync = new();
    private bool headerWritten;

    public LogService(PortableAppPaths paths)
    {
        this.paths = paths;
    }

    public void Info(string message) => Write("INFO", message);

    public void Error(Exception exception, string message)
    {
        Write("ERROR", $"{message}{Environment.NewLine}{exception}");
    }

    private void Write(string level, string message)
    {
        lock (sync)
        {
            if (!headerWritten)
            {
                headerWritten = true;
                File.AppendAllText(paths.LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [INFO] MediaScribe Recorder {AppVersion.Current} | App={paths.Root} | Data={paths.UserRoot}{Environment.NewLine}");
            }

            File.AppendAllText(paths.LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
        }
    }
}
