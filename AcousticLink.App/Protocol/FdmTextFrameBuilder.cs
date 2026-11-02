using AcousticLink.App.Signals;

namespace AcousticLink.App.Protocol;

public sealed record FdmTextChannelFrame(int Utf8ByteCount, string FrameBits, string TransmittedBits)
{
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
    private readonly MessageCodec codec = new();

    public FdmTextFrames Build(string textA, string textB)
    {
        var a = BuildChannel(textA, "A");
        var b = BuildChannel(textB, "B");
        int total = Math.Max(a.FrameBits.Length, b.FrameBits.Length);
        return new(a with { TransmittedBits = a.FrameBits.PadRight(total, '0') },
            b with { TransmittedBits = b.FrameBits.PadRight(total, '0') });
    }

    private FdmTextChannelFrame BuildChannel(string text, string channel)
    {
        // El códec es la única fuente de UTF-8, longitud, orden de bits y CRC.
        byte[] block = codec.EncodeBytes(text);
        int byteCount = block[0];
        if (byteCount > MaximumUtf8Bytes)
            throw new ArgumentException($"El canal {channel} admite como máximo 8 bytes UTF-8 (recibidos: {byteCount}).");
        string bits = codec.Encode(text);
        string frame = BfskFrameBuilder.Preamble + BfskFrameBuilder.StartMarker + bits;
        return new(byteCount, frame, frame);
    }
}
