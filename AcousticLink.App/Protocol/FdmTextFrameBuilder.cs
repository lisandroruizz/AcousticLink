using AcousticLink.App.Signals;

namespace AcousticLink.App.Protocol;

public sealed record FdmTextChannelFrame(int Utf8ByteCount, string FrameBits, string TransmittedBits)
{
    public string OriginalBlockBits { get; init; } = "";
    public string EncodedBlockBits { get; init; } = "";
    public bool UsesHamming { get; init; }
    public int OriginalBlockBitCount => OriginalBlockBits.Length;
    public int EncodedBlockBitCount => EncodedBlockBits.Length;
    public int HeaderBitCount => BfskFrameBuilder.Preamble.Length + BfskFrameBuilder.StartMarker.Length;
    public int RealBitCount => FrameBits.Length;
    public int PaddingBitCount => TransmittedBits.Length - FrameBits.Length;
}

public sealed record FdmTextFrames(FdmTextChannelFrame ChannelA, FdmTextChannelFrame ChannelB)
{
    public int TotalSymbols => ChannelA.TransmittedBits.Length;
}

public sealed class FdmTextFrameBuilder
{
    public const int MaximumUtf8Bytes = 8;
    public static int MaximumFrameBits => BfskFrameBuilder.Preamble.Length + BfskFrameBuilder.StartMarker.Length
        + (1 + MaximumUtf8Bytes + MessageCodec.CrcBytes) * 8;
    public static int MaximumProtectedFrameBits => BfskFrameBuilder.Preamble.Length + BfskFrameBuilder.StartMarker.Length
        + ((1 + MaximumUtf8Bytes + MessageCodec.CrcBytes) * 8 / 4 * 7);
    private readonly MessageCodec codec = new();

    // No hay indicador de modo en el aire: el futuro receptor debe seleccionar el mismo modo.
    // Por compatibilidad, la omisión de useHamming conserva el formato original sin protección.
    public FdmTextFrames Build(string textA, string textB, bool useHamming = false)
    {
        var a = BuildChannel(textA, "A", useHamming);
        var b = BuildChannel(textB, "B", useHamming);
        int total = Math.Max(a.FrameBits.Length, b.FrameBits.Length);
        return new(a with { TransmittedBits = a.FrameBits.PadRight(total, '0') },
            b with { TransmittedBits = b.FrameBits.PadRight(total, '0') });
    }

    internal FdmTextChannelFrame BuildChannel(string text, string channel, bool useHamming)
    {
        // El códec es la única fuente de UTF-8, longitud, orden de bits y CRC.
        string bits = codec.Encode(text);
        int byteCount = Convert.ToInt32(bits[..8], 2);
        if (byteCount > MaximumUtf8Bytes)
            throw new ArgumentException($"El canal {channel} admite como máximo 8 bytes UTF-8 (recibidos: {byteCount}).");
        // Un único CRC, ya incluido por MessageCodec; proteger el bloque completo sin tocarlo.
        string encoded = useHamming ? new Hamming74Codec().Encode(bits) : bits;
        string frame = BfskFrameBuilder.Preamble + BfskFrameBuilder.StartMarker + encoded;
        return new(byteCount, frame, frame)
        {
            OriginalBlockBits = bits,
            EncodedBlockBits = encoded,
            UsesHamming = useHamming
        };
    }
}
