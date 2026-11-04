using AcousticLink.App.Signals;
using AcousticLink.App.Protocol;

namespace AcousticLink.App.Visualization;

public sealed record FdmProtocolSegment(string Name, int FirstSymbol, int SymbolCount, bool Protected)
{
    public int EndSymbol => FirstSymbol + SymbolCount;
}

// Metadatos del emisor: nunca se deducen del brillo, del receptor ni de referencias experimentales.
public sealed record FdmTransmissionInfo(string BitsA, string BitsB, double SymbolSeconds, FdmTextFrames? Frames = null)
{
    public int SymbolAt(double seconds) => Math.Clamp((int)Math.Floor(seconds / SymbolSeconds + 1e-9), 0, BitsA.Length - 1);
    public (char Bit, double Frequency) Tone(bool channelB, double seconds)
    {
        char bit = (channelB ? BitsB : BitsA)[SymbolAt(seconds)];
        return (bit, CarrierEnergyMap.Frequencies[(channelB ? 2 : 0) + (bit == '1' ? 1 : 0)]);
    }
    public IReadOnlyList<FdmProtocolSegment> Segments(bool channelB)
    {
        if (Frames is null) return [];
        var frame = channelB ? Frames.ChannelB : Frames.ChannelA;
        int factor(int bits) => frame.UsesHamming ? bits / 4 * 7 : bits;
        int offset = 0;
        var result = new List<FdmProtocolSegment>();
        void Add(string name, int count, bool encoded = false)
        { if (count > 0) result.Add(new(name, offset, count, encoded)); offset += count; }
        Add("Preámbulo", BfskFrameBuilder.Preamble.Length);
        Add("Marcador", BfskFrameBuilder.StartMarker.Length);
        Add("Longitud", factor(8), frame.UsesHamming);
        Add("UTF-8", factor(frame.Utf8ByteCount * 8), frame.UsesHamming);
        Add("CRC-32", factor(32), frame.UsesHamming);
        Add("Relleno", frame.PaddingBitCount);
        return result.AsReadOnly();
    }
}
