namespace AcousticLink.App.Signals;

public sealed class BfskToneGenerator
{
    public const int SampleRate = 48_000;
    public const double Bit0Frequency = 1200;
    public const double Bit1Frequency = 1900;
    public const double DefaultBitDurationSeconds = 0.10;
    public const double DefaultAmplitude = 0.15;

    public float[] Generate(bool bit, double durationSeconds = DefaultBitDurationSeconds,
        double amplitude = DefaultAmplitude)
    {
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        if (!double.IsFinite(amplitude) || amplitude < 0 || amplitude > 1)
            throw new ArgumentOutOfRangeException(nameof(amplitude));
        int count = checked((int)Math.Round(durationSeconds * SampleRate));
        if (count < 3)
            throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        var samples = new float[count];
        double frequency = bit ? Bit1Frequency : Bit0Frequency;
        // Rampas lineales de hasta 5 ms, incluidas en la duración solicitada.
        int fadeSamples = Math.Min(240, (count - 1) / 2);
        for (int i = 0; i < count; i++)
        {
            double envelope = Math.Min(1.0, Math.Min(i, count - 1 - i) / (double)fadeSamples);
            samples[i] = (float)(amplitude * envelope * Math.Sin(2 * Math.PI * frequency * i / SampleRate));
        }
        return samples;
    }
}
