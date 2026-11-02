namespace AcousticLink.App.Signals;

public sealed class FdmToneMixer
{
    public const int SampleRate = BfskToneGenerator.SampleRate;
    public const double ChannelABit0Frequency = BfskToneGenerator.Bit0Frequency;
    public const double ChannelABit1Frequency = BfskToneGenerator.Bit1Frequency;
    public const double ChannelBBit0Frequency = 3000;
    public const double ChannelBBit1Frequency = 3700;
    public const double MaximumAmplitude = 0.15;
    private readonly BfskToneGenerator generator = new();

    public float[] Generate(bool bitA, bool bitB, double durationSeconds = 1,
        double amplitude = MaximumAmplitude)
    {
        if (!double.IsFinite(amplitude) || amplitude < 0 || amplitude > MaximumAmplitude)
            throw new ArgumentOutOfRangeException(nameof(amplitude), "La amplitud conjunta debe estar entre 0 y 0,15.");
        double frequencyA = bitA ? ChannelABit1Frequency : ChannelABit0Frequency;
        double frequencyB = bitB ? ChannelBBit1Frequency : ChannelBBit0Frequency;
        float[] a = generator.GenerateTone(frequencyA, durationSeconds, amplitude / 2);
        float[] b = generator.GenerateTone(frequencyB, durationSeconds, amplitude / 2);
        // Ambas senoides usan las mismas rampas de 5 ms, en los extremos del bloque.
        // e[n] * (A/2*sin(faseA[n]) + A/2*sin(faseB[n])); no se recorta ni normaliza.
        for (int i = 0; i < a.Length; i++) a[i] += b[i];
        return a;
    }
}
