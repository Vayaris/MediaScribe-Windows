using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MediaScribeRecorder.Services;

public sealed class StereoSampleProvider : ISampleProvider
{
    private readonly ISampleProvider source;
    private readonly int sourceChannels;
    private readonly float[] sourceBuffer;

    public StereoSampleProvider(ISampleProvider source)
    {
        this.source = source;
        sourceChannels = source.WaveFormat.Channels;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);
        sourceBuffer = new float[8192 * sourceChannels];
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        var framesRequested = count / 2;
        var sourceSamplesNeeded = framesRequested * sourceChannels;
        if (sourceBuffer.Length < sourceSamplesNeeded)
        {
            throw new InvalidOperationException("Internal source buffer is too small.");
        }

        var sourceSamplesRead = source.Read(sourceBuffer, 0, sourceSamplesNeeded);
        var framesRead = sourceSamplesRead / sourceChannels;
        for (var frame = 0; frame < framesRead; frame++)
        {
            var outIndex = offset + frame * 2;
            if (sourceChannels == 1)
            {
                var sample = sourceBuffer[frame];
                buffer[outIndex] = sample;
                buffer[outIndex + 1] = sample;
            }
            else
            {
                var inIndex = frame * sourceChannels;
                buffer[outIndex] = sourceBuffer[inIndex];
                buffer[outIndex + 1] = sourceBuffer[inIndex + 1];
            }
        }

        return framesRead * 2;
    }
}
