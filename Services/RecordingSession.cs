using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MediaScribeRecorder.Services;

public sealed class RecordingSession : IDisposable
{
    private const int TargetSampleRate = 48000;
    private const int TargetChannels = 2;
    private readonly IAudioCaptureSource systemCapture;
    private readonly IAudioCaptureSource micCapture;
    private readonly LogService log;
    private readonly object sync = new();
    private readonly CancellationTokenSource cts = new();
    private readonly CapturedSource systemSource;
    private readonly CapturedSource micSource;
    private readonly WaveFileWriter writer;
    private readonly Task writerTask;
    private readonly float systemGain;
    private readonly float microphoneGain;
    private bool stopped;

    public RecordingSession(
        IAudioCaptureSource systemCapture,
        IAudioCaptureSource micCapture,
        string outputPath,
        LogService log,
        double systemGain,
        double microphoneGain)
    {
        this.systemCapture = systemCapture;
        this.micCapture = micCapture;
        this.log = log;
        this.systemGain = (float)Math.Clamp(systemGain, 0.1, 4.0);
        this.microphoneGain = (float)Math.Clamp(microphoneGain, 0.1, 4.0);
        OutputPath = outputPath;

        systemSource = new CapturedSource(systemCapture, "Système");
        micSource = new CapturedSource(micCapture, "Micro");
        writer = new WaveFileWriter(outputPath, new WaveFormat(TargetSampleRate, 16, TargetChannels));
        writerTask = Task.Run(() => WriteLoop(cts.Token));
    }

    public string OutputPath { get; }
    public event EventHandler<RecordingLevelsEventArgs>? LevelsUpdated;
    public event EventHandler<string>? WarningRaised;

    public void Start()
    {
        systemCapture.RecordingStopped += OnCaptureStopped;
        micCapture.RecordingStopped += OnCaptureStopped;
        systemCapture.Start();
        micCapture.Start();
        log.Info($"Recording started: {OutputPath}");
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
            await writerTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        lock (sync)
        {
            writer.Dispose();
        }

        log.Info($"Recording stopped: {OutputPath}");
    }

    private void WriteLoop(CancellationToken token)
    {
        var systemBuffer = new float[TargetSampleRate * TargetChannels / 50];
        var micBuffer = new float[systemBuffer.Length];
        var mix = new float[systemBuffer.Length];
        var outputBytes = new byte[mix.Length * 2];
        var interval = TimeSpan.FromMilliseconds(20);

        while (!token.IsCancellationRequested)
        {
            var started = DateTime.UtcNow;
            Array.Clear(mix);

            var systemRead = systemSource.Read(systemBuffer, systemBuffer.Length);
            var micRead = micSource.Read(micBuffer, micBuffer.Length);

            var systemLevel = Peak(systemBuffer, systemRead, systemGain);
            var micLevel = Peak(micBuffer, micRead, microphoneGain);
            for (var i = 0; i < mix.Length; i++)
            {
                var sample = 0f;
                if (i < systemRead)
                {
                    sample += systemBuffer[i] * systemGain;
                }

                if (i < micRead)
                {
                    sample += micBuffer[i] * microphoneGain;
                }

                mix[i] = Math.Clamp(sample, -1f, 1f);
            }

            FloatToPcm16(mix, outputBytes);
            lock (sync)
            {
                writer.Write(outputBytes, 0, outputBytes.Length);
            }

            LevelsUpdated?.Invoke(this, new RecordingLevelsEventArgs(systemLevel, micLevel));

            var elapsed = DateTime.UtcNow - started;
            var sleep = interval - elapsed;
            if (sleep > TimeSpan.Zero)
            {
                token.WaitHandle.WaitOne(sleep);
            }
        }
    }

    private void OnCaptureStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            log.Error(e.Exception, "Audio capture stopped with an error.");
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

    private static float Peak(float[] buffer, int count, float gain)
    {
        var peak = 0f;
        for (var i = 0; i < count; i++)
        {
            var value = Math.Abs(buffer[i] * gain);
            if (value > peak)
            {
                peak = value;
            }
        }

        return Math.Clamp(peak, 0f, 1f);
    }

    private static void FloatToPcm16(float[] samples, byte[] bytes)
    {
        for (var i = 0; i < samples.Length; i++)
        {
            var value = (short)Math.Clamp(samples[i] * short.MaxValue, short.MinValue, short.MaxValue);
            var byteIndex = i * 2;
            bytes[byteIndex] = (byte)(value & 0xff);
            bytes[byteIndex + 1] = (byte)((value >> 8) & 0xff);
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
        private readonly BufferedWaveProvider buffered;
        private readonly ISampleProvider provider;

        public CapturedSource(IAudioCaptureSource capture, string name)
        {
            buffered = new BufferedWaveProvider(capture.WaveFormat)
            {
                BufferDuration = TimeSpan.FromSeconds(5),
                DiscardOnBufferOverflow = true,
                ReadFully = false,
            };

            capture.DataAvailable += (_, args) =>
            {
                if (args.BytesRecorded > 0)
                {
                    buffered.AddSamples(args.Buffer, 0, args.BytesRecorded);
                }
            };

            ISampleProvider samples = buffered.ToSampleProvider();
            if (samples.WaveFormat.Channels != TargetChannels)
            {
                samples = new StereoSampleProvider(samples);
            }

            if (samples.WaveFormat.SampleRate != TargetSampleRate)
            {
                samples = new WdlResamplingSampleProvider(samples, TargetSampleRate);
            }

            provider = samples;
            Name = name;
        }

        public string Name { get; }

        public int Read(float[] buffer, int count)
        {
            Array.Clear(buffer, 0, count);
            try
            {
                return provider.Read(buffer, 0, count);
            }
            catch
            {
                return 0;
            }
        }
    }
}

public sealed class RecordingLevelsEventArgs : EventArgs
{
    public RecordingLevelsEventArgs(float systemLevel, float micLevel)
    {
        SystemLevel = systemLevel;
        MicrophoneLevel = micLevel;
    }

    public float SystemLevel { get; }
    public float MicrophoneLevel { get; }
}
