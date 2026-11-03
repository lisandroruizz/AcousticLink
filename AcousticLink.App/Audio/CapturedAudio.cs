using NAudio.Wave;

namespace AcousticLink.App.Audio;

public enum CaptureStopReason { None, Manual, DurationLimit, ApplicationClosing, DeviceStopped, Error, Imported }

// Buffer acotado: los canales de entrada se promedian explícitamente a mono.
public sealed class CapturedAudio
{
    private readonly object sync = new();
    private readonly float[] samples;
    private int count;
    private float peak;
    private double sumSquares;
    private CaptureStopReason stopReason;
    public const int DefaultMaximumSeconds = 10;
    public const int MinimumSeconds = 5;
    public const int MaximumSeconds = 30;
    public int MaximumDurationSeconds { get; }
    public int SampleCapacity => samples.Length;
    public int SampleRate { get; }
    public int InputChannels { get; }

    public CapturedAudio(int sampleRate, int inputChannels, int maximumDurationSeconds = DefaultMaximumSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(inputChannels);
        ValidateDuration(maximumDurationSeconds);
        SampleRate = sampleRate;
        InputChannels = inputChannels;
        MaximumDurationSeconds = maximumDurationSeconds;
        samples = new float[checked(sampleRate * maximumDurationSeconds)];
    }

    // Toma posesión del arreglo privado del importador: no convierte ni copia las muestras.
    internal CapturedAudio(float[] importedSamples, int sampleRate)
    {
        SampleRate = sampleRate;
        InputChannels = 1;
        MaximumDurationSeconds = MaximumSeconds;
        samples = importedSamples;
        count = samples.Length;
        stopReason = CaptureStopReason.Imported;
        foreach (float value in samples)
        {
            peak = Math.Max(peak, Math.Abs(value));
            sumSquares += (double)value * value;
        }
    }

    public static void ValidateDuration(int seconds)
    {
        if (seconds < MinimumSeconds || seconds > MaximumSeconds)
            throw new ArgumentOutOfRangeException(nameof(seconds), "La duración máxima debe estar entre 5 y 30 segundos.");
    }

    public (int Count, float Peak, double Rms, double DurationSeconds, CaptureStopReason StopReason) GetMetrics()
    {
        lock (sync) return (count, peak, count > 0 ? Math.Sqrt(sumSquares / count) : 0,
            count / (double)SampleRate, stopReason);
    }

    public void Complete(CaptureStopReason reason)
    {
        if (reason == CaptureStopReason.None || !Enum.IsDefined(reason)) throw new ArgumentOutOfRangeException(nameof(reason));
        lock (sync)
            if (stopReason == CaptureStopReason.None) stopReason = reason;
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
            if (stopReason != CaptureStopReason.None) return;
            for (int i = 0; i < length && count < samples.Length; i += InputChannels)
            {
                double sum = 0;
                for (int c = 0; c < InputChannels; c++) sum += interleaved[i + c];
                float value = (float)(sum / InputChannels);
                samples[count++] = value;
                peak = Math.Max(peak, Math.Abs(value));
                sumSquares += (double)value * value;
            }
            if (count == samples.Length) stopReason = CaptureStopReason.DurationLimit;
        }
    }

    public float[] GetSamples()
    {
        lock (sync) return samples[..count];
    }

    // Lee solo el prefijo nuevo publicado. Sus valores nunca cambian después de Append.
    // El bloqueo se limita a tomar el contador; la copia no detiene el callback de captura.
    public int CopySamples(int start, Span<float> destination)
    {
        int available;
        lock (sync) available = count;
        if (start < 0 || start > available) throw new ArgumentOutOfRangeException(nameof(start));
        int length = Math.Min(destination.Length, available - start);
        samples.AsSpan(start, length).CopyTo(destination);
        return length;
    }

    public void SaveWav(string path)
    {
        // El prefijo publicado nunca se modifica: solo se agregan nuevas muestras al final.
        // Exportar ese prefijo evita duplicar hasta 5,76 MB para guardar un WAV de 30 s.
        int snapshotCount;
        lock (sync) snapshotCount = count;
        using var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 1));
        writer.WriteSamples(samples, 0, snapshotCount);
    }
}
