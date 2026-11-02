namespace AcousticLink.App.Signals;

public sealed class BfskToneGenerator
{
    public const int MaximumSequenceLength = 64;
    public const int SampleRate = 48_000;
    public const double Bit0Frequency = 1200;
    public const double Bit1Frequency = 1900;
    public const double DefaultBitDurationSeconds = 0.10;
    public const double DefaultAmplitude = 0.15;

    public float[] Generate(bool bit, double durationSeconds = DefaultBitDurationSeconds,
        double amplitude = DefaultAmplitude)
    {
        double frequency = bit ? Bit1Frequency : Bit0Frequency;
        return GenerateTone(frequency, durationSeconds, amplitude);
    }

    // Comparte la misma senoide y envolvente con la prueba de mezcla FDM.
    internal float[] GenerateTone(double frequency, double durationSeconds, double amplitude)
    {
        if (!double.IsFinite(frequency) || frequency <= 0 || frequency >= SampleRate / 2.0)
            throw new ArgumentOutOfRangeException(nameof(frequency));
        int sampleCount = GetSampleCount(durationSeconds);
        return GenerateWaveform([frequency], sampleCount, amplitude);
    }

    public float[] GenerateSequence(string bits, double bitDurationSeconds = DefaultBitDurationSeconds,
        double amplitude = DefaultAmplitude)
    {
        ArgumentNullException.ThrowIfNull(bits);
        if (bits.Length == 0)
            throw new ArgumentException("La secuencia no puede estar vacía.", nameof(bits));
        if (bits.Length > MaximumSequenceLength)
            throw new ArgumentException($"La secuencia no puede superar {MaximumSequenceLength} bits.", nameof(bits));

        var frequencies = new double[bits.Length];
        for (int i = 0; i < bits.Length; i++)
        {
            frequencies[i] = bits[i] switch
            {
                '0' => Bit0Frequency,
                '1' => Bit1Frequency,
                _ => throw new ArgumentException("La secuencia solo puede contener caracteres 0 y 1.", nameof(bits))
            };
        }

        return GenerateWaveform(frequencies, GetSampleCount(bitDurationSeconds), amplitude);
    }

    private static float[] GenerateWaveform(IReadOnlyList<double> frequencies, int samplesPerSymbol,
        double amplitude)
    {
        ValidateAmplitude(amplitude);
        int sampleCount = checked(frequencies.Count * samplesPerSymbol);
        var samples = new float[sampleCount];
        // Una envolvente global evita chasquidos sin crear rampas ni silencios entre bits.
        int fadeSamples = Math.Min(240, (sampleCount - 1) / 2);
        double phase = 0;
        for (int i = 0; i < sampleCount; i++)
        {
            double envelope = Math.Min(1.0, Math.Min(i, sampleCount - 1 - i) / (double)fadeSamples);
            samples[i] = (float)(amplitude * envelope * Math.Sin(phase));
            phase += 2 * Math.PI * frequencies[i / samplesPerSymbol] / SampleRate;
            if (phase >= 2 * Math.PI)
                phase %= 2 * Math.PI;
        }
        return samples;
    }

    private static int GetSampleCount(double durationSeconds)
    {
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        int sampleCount = checked((int)Math.Round(durationSeconds * SampleRate));
        return sampleCount >= 3
            ? sampleCount
            : throw new ArgumentOutOfRangeException(nameof(durationSeconds));
    }

    private static void ValidateAmplitude(double amplitude)
    {
        if (!double.IsFinite(amplitude) || amplitude < 0 || amplitude > 1)
            throw new ArgumentOutOfRangeException(nameof(amplitude));
    }
}
