namespace AcousticLink.App.Signals;

public sealed class FdmToneMixer
{
    public const int SampleRate = BfskToneGenerator.SampleRate;
    public const double ChannelABit0Frequency = BfskToneGenerator.Bit0Frequency;
    public const double ChannelABit1Frequency = BfskToneGenerator.Bit1Frequency;
    public const double ChannelBBit0Frequency = 3000;
    public const double ChannelBBit1Frequency = 3700;
    public const double MaximumAmplitude = FdmAmplitudeProfiles.MaximumAmplitude;
    private readonly BfskToneGenerator generator = new();

    public float[] Generate(bool bitA, bool bitB, double durationSeconds = 1,
        double amplitude = MaximumAmplitude, FdmAmplitudeProfile profile = FdmAmplitudeProfile.Original)
        => GenerateStreams(bitA ? "1" : "0", bitB ? "1" : "0", durationSeconds, 1, profile, amplitude);

    internal float[] GenerateStreams(string bitsA, string bitsB, double symbolDurationSeconds, int maximumBits,
        FdmAmplitudeProfile profile, double amplitude = MaximumAmplitude)
    {
        if (bitsA.Length != bitsB.Length) throw new ArgumentException("Los canales deben tener igual cantidad de símbolos.");
        var gains = FdmAmplitudeProfiles.GetGains(profile, amplitude);
        float[] a = generator.GenerateSequence(bitsA, ChannelABit0Frequency, ChannelABit1Frequency,
            symbolDurationSeconds, gains.A, maximumBits);
        float[] b = generator.GenerateSequence(bitsB, ChannelBBit0Frequency, ChannelBBit1Frequency,
            symbolDurationSeconds, gains.B, maximumBits);
        // Envolvente común en los extremos, fases independientes; suma sin normalización ni recorte.
        return MixInPlace(a, b);
    }

    internal static float[] MixInPlace(float[] a, float[] b)
    {
        if (a.Length != b.Length) throw new ArgumentException("Los canales deben tener igual cantidad de muestras.");
        for (int i = 0; i < a.Length; i++) a[i] += b[i];
        return a;
    }
}
