using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Diagnostics;

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
        const int maxChunkMilliseconds = 100;
        var maxSamples = TargetSampleRate * TargetChannels * maxChunkMilliseconds / 1000;
        var systemBuffer = new float[maxSamples];
        var micBuffer = new float[maxSamples];
        var mix = new float[maxSamples];
        var outputBytes = new byte[mix.Length * 2];
        var stopwatch = Stopwatch.StartNew();
        long samplesWritten = 0;

        while (!token.IsCancellationRequested)
        {
            var elapsedSamples = (long)(stopwatch.Elapsed.TotalSeconds * TargetSampleRate) * TargetChannels;
            var samplesDue = elapsedSamples - samplesWritten;
            if (samplesDue <= 0)
            {
                token.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(5));
                continue;
            }

            var samplesToWrite = (int)Math.Min(samplesDue, maxSamples);
            samplesToWrite -= samplesToWrite % TargetChannels;
            if (samplesToWrite <= 0)
            {
                continue;
            }

            Array.Clear(mix);

            var systemRead = systemSource.Read(systemBuffer, samplesToWrite);
            var micRead = micSource.Read(micBuffer, samplesToWrite);

            var systemLevel = Math.Clamp(systemSource.LatestPeak * systemGain, 0f, 1f);
            var micLevel = Math.Clamp(micSource.LatestPeak * microphoneGain, 0f, 1f);
            for (var i = 0; i < samplesToWrite; i++)
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

            FloatToPcm16(mix, samplesToWrite, outputBytes);
            lock (sync)
            {
                writer.Write(outputBytes, 0, samplesToWrite * 2);
            }
            samplesWritten += samplesToWrite;

            LevelsUpdated?.Invoke(this, new RecordingLevelsEventArgs(systemLevel, micLevel));
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

    private static void FloatToPcm16(float[] samples, int count, byte[] bytes)
    {
        for (var i = 0; i < count; i++)
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
                BufferDuration = TimeSpan.FromMilliseconds(750),
                DiscardOnBufferOverflow = true,
                ReadFully = false,
            };

            capture.DataAvailable += (_, args) =>
            {
                if (args.BytesRecorded > 0)
                {
                    LatestPeak = EstimatePeak(args.Buffer, args.BytesRecorded, capture.WaveFormat);
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
        public float LatestPeak { get; private set; }

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
