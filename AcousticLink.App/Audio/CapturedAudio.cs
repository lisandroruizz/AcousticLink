using NAudio.Wave;

namespace AcousticLink.App.Audio;

// Buffer acotado: los canales de entrada se promedian explícitamente a mono.
public sealed class CapturedAudio
{
    private readonly object sync = new();
    private readonly float[] samples;
    private int count;
    private float peak;
    public int SampleRate { get; }
    public int InputChannels { get; }

    public CapturedAudio(int sampleRate, int inputChannels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(inputChannels);
        SampleRate = sampleRate;
        InputChannels = inputChannels;
        samples = new float[checked(sampleRate * 10)];
    }

    public (int Count, float Peak) GetStatus()
    {
        lock (sync) return (count, peak);
    }

    public void Append(float[] interleaved, int length)
    {
        if (length < 0 || length > interleaved.Length || length % InputChannels != 0)
            throw new ArgumentException("El bloque debe contener cuadros completos.");
        lock (sync)
        {
            for (int i = 0; i < length && count < samples.Length; i += InputChannels)
            {
                double sum = 0;
                for (int c = 0; c < InputChannels; c++) sum += interleaved[i + c];
                float value = (float)(sum / InputChannels);
                samples[count++] = value;
                peak = Math.Max(peak, Math.Abs(value));
            }
        }
    }

    public float[] GetSamples()
    {
        lock (sync) return samples[..count];
    }

    public void SaveWav(string path)
    {
        float[] copy = GetSamples();
        using var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 1));
        writer.WriteSamples(copy, 0, copy.Length);
    }
}
