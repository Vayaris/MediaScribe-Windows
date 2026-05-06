using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Diagnostics;

namespace MediaScribeRecorder.Services;

public interface IAudioCaptureSource : IDisposable
{
    WaveFormat WaveFormat { get; }
    event EventHandler<WaveInEventArgs>? DataAvailable;
    event EventHandler<StoppedEventArgs>? RecordingStopped;
    void Start();
    void Stop();
}

public sealed class WasapiCaptureSource : IAudioCaptureSource
{
    private readonly WasapiCapture capture;

    public WasapiCaptureSource(MMDevice device)
    {
        capture = new WasapiCapture(device);
        capture.ShareMode = AudioClientShareMode.Shared;
    }

    public WaveFormat WaveFormat => capture.WaveFormat;
    public event EventHandler<WaveInEventArgs>? DataAvailable
    {
        add => capture.DataAvailable += value;
        remove => capture.DataAvailable -= value;
    }

    public event EventHandler<StoppedEventArgs>? RecordingStopped
    {
        add => capture.RecordingStopped += value;
        remove => capture.RecordingStopped -= value;
    }

    public void Start() => capture.StartRecording();
    public void Stop() => capture.StopRecording();
    public void Dispose() => capture.Dispose();
}

public sealed class WasapiLoopbackCaptureSource : IAudioCaptureSource
{
    private readonly WasapiLoopbackCapture capture;

    public WasapiLoopbackCaptureSource(MMDevice device)
    {
        capture = new WasapiLoopbackCapture(device);
    }

    public WaveFormat WaveFormat => capture.WaveFormat;
    public event EventHandler<WaveInEventArgs>? DataAvailable
    {
        add => capture.DataAvailable += value;
        remove => capture.DataAvailable -= value;
    }

    public event EventHandler<StoppedEventArgs>? RecordingStopped
    {
        add => capture.RecordingStopped += value;
        remove => capture.RecordingStopped -= value;
    }

    public void Start() => capture.StartRecording();
    public void Stop() => capture.StopRecording();
    public void Dispose() => capture.Dispose();
}

public sealed class SilentCaptureSource : IAudioCaptureSource
{
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
#pragma warning disable CS0067
    public event EventHandler<WaveInEventArgs>? DataAvailable;
#pragma warning restore CS0067
    public event EventHandler<StoppedEventArgs>? RecordingStopped;

    public void Start()
    {
    }

    public void Stop()
    {
        RecordingStopped?.Invoke(this, new StoppedEventArgs(null));
    }

    public void Dispose()
    {
    }
}

public sealed class ProcessLoopbackCaptureSource : IAudioCaptureSource
{
    private readonly string stopFile;
    private Process? process;
    private CancellationTokenSource? readerCts;
    private Task? readerTask;

    public ProcessLoopbackCaptureSource(int processId)
    {
        ProcessId = processId;
        WaveFormat = new WaveFormat(48000, 16, 2);
        stopFile = Path.Combine(Path.GetTempPath(), $"mediascribe-process-loopback-{Guid.NewGuid():N}.stop");
    }

    public int ProcessId { get; }
    public WaveFormat WaveFormat { get; }
    public event EventHandler<WaveInEventArgs>? DataAvailable;
    public event EventHandler<StoppedEventArgs>? RecordingStopped;

    public void Start()
    {
        var helper = Path.Combine(AppContext.BaseDirectory, "Tools", "MediaScribeProcessLoopback.exe");
        if (!File.Exists(helper))
        {
            throw new FileNotFoundException("Module de capture application introuvable.", helper);
        }

        File.Delete(stopFile);
        readerCts = new CancellationTokenSource();
        var startInfo = new ProcessStartInfo
        {
            FileName = helper,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add(ProcessId.ToString());
        startInfo.ArgumentList.Add("-");
        startInfo.ArgumentList.Add(stopFile);

        process = Process.Start(startInfo) ?? throw new InvalidOperationException("Impossible de démarrer la capture application.");
        readerTask = Task.Run(() => ReadLoop(process, readerCts.Token));
    }

    public void Stop()
    {
        try
        {
            File.WriteAllText(stopFile, "stop");
        }
        catch
        {
        }

        if (process is { HasExited: false })
        {
            if (!process.WaitForExit(5000))
            {
                process.Kill(entireProcessTree: true);
            }
        }

        readerCts?.Cancel();
    }

    public void Dispose()
    {
        Stop();
        readerCts?.Dispose();
        process?.Dispose();
        try
        {
            File.Delete(stopFile);
        }
        catch
        {
        }
    }

    private async Task ReadLoop(Process activeProcess, CancellationToken token)
    {
        var buffer = new byte[WaveFormat.AverageBytesPerSecond / 100];
        try
        {
            while (!token.IsCancellationRequested)
            {
                var read = await activeProcess.StandardOutput.BaseStream.ReadAsync(buffer.AsMemory(0, buffer.Length), token).ConfigureAwait(false);
                if (read <= 0)
                {
                    break;
                }

                var copy = new byte[read];
                Buffer.BlockCopy(buffer, 0, copy, 0, read);
                DataAvailable?.Invoke(this, new WaveInEventArgs(copy, read));
            }

            if (activeProcess.HasExited && activeProcess.ExitCode != 0)
            {
                var error = await activeProcess.StandardError.ReadToEndAsync().ConfigureAwait(false);
                RecordingStopped?.Invoke(this, new StoppedEventArgs(new InvalidOperationException(error.Trim())));
            }
            else
            {
                RecordingStopped?.Invoke(this, new StoppedEventArgs(null));
            }
        }
        catch (OperationCanceledException)
        {
            RecordingStopped?.Invoke(this, new StoppedEventArgs(null));
        }
        catch (Exception ex)
        {
            RecordingStopped?.Invoke(this, new StoppedEventArgs(ex));
        }
    }
}
