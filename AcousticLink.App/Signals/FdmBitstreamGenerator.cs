namespace AcousticLink.App.Signals;

public sealed class FdmBitstreamGenerator
{
    public const int MaximumBits = 16;
    public const int SampleRate = FdmToneMixer.SampleRate;
    public const double SymbolDurationSeconds = BfskToneGenerator.DefaultBitDurationSeconds;
    private readonly BfskToneGenerator generator = new();

    public float[] Generate(string bitsA, string bitsB)
    {
        ValidateSequences(bitsA, bitsB);
        double perChannelAmplitude = FdmToneMixer.MaximumAmplitude / 2;
        // Cada llamada conserva su propio acumulador de fase y usa una envolvente global.
        float[] a = generator.GenerateSequence(bitsA, FdmToneMixer.ChannelABit0Frequency,
            FdmToneMixer.ChannelABit1Frequency, SymbolDurationSeconds, perChannelAmplitude);
        float[] b = generator.GenerateSequence(bitsB, FdmToneMixer.ChannelBBit0Frequency,
            FdmToneMixer.ChannelBBit1Frequency, SymbolDurationSeconds, perChannelAmplitude);
        return FdmToneMixer.MixInPlace(a, b);
    }

    public static void ValidateSequences(string bitsA, string bitsB)
    {
        ArgumentNullException.ThrowIfNull(bitsA);
        ArgumentNullException.ThrowIfNull(bitsB);
        if (bitsA.Length < 1 || bitsA.Length > MaximumBits || bitsB.Length < 1 || bitsB.Length > MaximumBits)
            throw new ArgumentException("Cada canal requiere entre 1 y 16 bits.");
        if (bitsA.Length != bitsB.Length)
            throw new ArgumentException("Las dos secuencias deben tener igual longitud.");
        if (bitsA.Any(c => c != '0' && c != '1') || bitsB.Any(c => c != '0' && c != '1'))
            throw new ArgumentException("Ambos canales deben contener solamente caracteres 0 y 1.");
    }
}
