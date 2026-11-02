using AcousticLink.App.Protocol;

namespace AcousticLink.App.Signals;

public sealed class FdmBitstreamGenerator
{
    public const int MaximumBits = 16;
    public static int HeaderBits => BfskFrameBuilder.Preamble.Length + BfskFrameBuilder.StartMarker.Length;
    public static int MaximumFrameBits => HeaderBits + BfskFrameBuilder.MaximumDataBits;
    public const int SampleRate = FdmToneMixer.SampleRate;
    public const double SymbolDurationSeconds = BfskToneGenerator.DefaultBitDurationSeconds;
    private readonly FdmToneMixer mixer = new();

    public float[] Generate(string bitsA, string bitsB, FdmAmplitudeProfile profile = FdmAmplitudeProfile.Original)
    {
        ValidateSequences(bitsA, bitsB);
        return GenerateCore(bitsA, bitsB, profile);
    }

    public static (string ChannelA, string ChannelB) BuildFrames(string dataA, string dataB)
    {
        ValidateSequences(dataA, dataB);
        var builder = new BfskFrameBuilder();
        return (builder.Build(dataA), builder.Build(dataB));
    }

    // Recibe datos de 1–16 bits; solo este modo puede generar cabeceras + datos (25–40 símbolos).
    public float[] GenerateFrames(string dataA, string dataB, FdmAmplitudeProfile profile = FdmAmplitudeProfile.Original)
    {
        var frames = BuildFrames(dataA, dataB);
        return GenerateCore(frames.ChannelA, frames.ChannelB, profile);
    }

    public float[] GenerateTextFrames(string textA, string textB, bool useHamming = false,
        FdmAmplitudeProfile profile = FdmAmplitudeProfile.Original)
    {
        var frames = new FdmTextFrameBuilder().Build(textA, textB, useHamming);
        return GenerateCore(frames.ChannelA.TransmittedBits, frames.ChannelB.TransmittedBits, profile,
            useHamming ? FdmTextFrameBuilder.MaximumProtectedFrameBits : FdmTextFrameBuilder.MaximumFrameBits);
    }

    private float[] GenerateCore(string bitsA, string bitsB, FdmAmplitudeProfile profile,
        int maximumBits = BfskToneGenerator.MaximumSequenceLength) =>
        mixer.GenerateStreams(bitsA, bitsB, SymbolDurationSeconds, maximumBits, profile);

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
