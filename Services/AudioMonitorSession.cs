using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Diagnostics;

namespace MediaScribeRecorder.Services;

public sealed class AudioMonitorSession : IDisposable
{
    private const int TargetSampleRate = 48000;
    private const int TargetChannels = 2;
    private readonly IAudioCaptureSource systemCapture;
    private readonly IAudioCaptureSource micCapture;
    private readonly LogService log;
    private readonly CancellationTokenSource cts = new();
    private readonly CapturedSource systemSource;
    private readonly CapturedSource micSource;
    private readonly Task monitorTask;
    private readonly float systemGain;
    private readonly float microphoneGain;
    private bool stopped;

    public AudioMonitorSession(
        IAudioCaptureSource systemCapture,
        IAudioCaptureSource micCapture,
        LogService log,
        double systemGain,
        double microphoneGain)
    {
        this.systemCapture = systemCapture;
        this.micCapture = micCapture;
        this.log = log;
        this.systemGain = (float)Math.Clamp(systemGain, 0.1, 4.0);
        this.microphoneGain = (float)Math.Clamp(microphoneGain, 0.1, 4.0);
        systemSource = new CapturedSource(systemCapture);
        micSource = new CapturedSource(micCapture);
        monitorTask = Task.Run(() => MonitorLoop(cts.Token));
    }

    public event EventHandler<RecordingLevelsEventArgs>? LevelsUpdated;
    public event EventHandler<string>? WarningRaised;

    public void Start()
    {
        systemCapture.RecordingStopped += OnCaptureStopped;
        micCapture.RecordingStopped += OnCaptureStopped;
        systemCapture.Start();
        micCapture.Start();
        log.Info("Audio input test started.");
    }

    public async Task StopAsync()
    {
        if (stopped)
        {
            return;
        }

        stopped = true;
        TryStop(systemCapture);
        TryStop(micCapture);
        cts.Cancel();

        try
        {
            await monitorTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        log.Info("Audio input test stopped.");
    }

    private void MonitorLoop(CancellationToken token)
    {
        var stopwatch = Stopwatch.StartNew();
        var lastUpdate = TimeSpan.Zero;
        while (!token.IsCancellationRequested)
        {
            var elapsed = stopwatch.Elapsed;
            if (elapsed - lastUpdate < TimeSpan.FromMilliseconds(40))
            {
                token.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(10));
                continue;
            }

            lastUpdate = elapsed;
            var systemLevel = Math.Clamp(systemSource.LatestPeak * systemGain, 0f, 1f);
            var micLevel = Math.Clamp(micSource.LatestPeak * microphoneGain, 0f, 1f);
            LevelsUpdated?.Invoke(this, new RecordingLevelsEventArgs(systemLevel, micLevel));
        }
    }

    private void OnCaptureStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            log.Error(e.Exception, "Audio monitor stopped with an error.");
            WarningRaised?.Invoke(this, e.Exception.Message);
        }
    }

    private static void TryStop(IAudioCaptureSource source)
    {
        try
        {
            source.Stop();
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        _ = StopAsync();
        systemCapture.Dispose();
        micCapture.Dispose();
        cts.Dispose();
    }

    private sealed class CapturedSource
    {
        public CapturedSource(IAudioCaptureSource capture)
        {
            capture.DataAvailable += (_, args) =>
            {
                if (args.BytesRecorded > 0)
                {
                    LatestPeak = EstimatePeak(args.Buffer, args.BytesRecorded, capture.WaveFormat);
                }
            };
        }

        public float LatestPeak { get; private set; }

        private static float EstimatePeak(byte[] buffer, int bytesRecorded, WaveFormat format)
        {
            var peak = 0f;
            if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
            {
                for (var i = 0; i + 3 < bytesRecorded; i += 4)
                {
                    peak = Math.Max(peak, Math.Abs(BitConverter.ToSingle(buffer, i)));
                }
            }
            else if (format.BitsPerSample == 16)
            {
                for (var i = 0; i + 1 < bytesRecorded; i += 2)
                {
                    peak = Math.Max(peak, Math.Abs(BitConverter.ToInt16(buffer, i) / (float)short.MaxValue));
                }
            }
            else if (format.BitsPerSample == 24)
            {
                for (var i = 0; i + 2 < bytesRecorded; i += 3)
                {
                    var sample = buffer[i] | (buffer[i + 1] << 8) | (buffer[i + 2] << 16);
                    if ((sample & 0x800000) != 0)
                    {
                        sample |= unchecked((int)0xff000000);
                    }

                    peak = Math.Max(peak, Math.Abs(sample / 8388608f));
                }
            }

            return Math.Clamp(peak, 0f, 1f);
        }
    }
}
