using AcousticLink.App.Protocol;
using AcousticLink.App.Signals;

namespace AcousticLink.App.Visualization;

public sealed record ReceiverObservation(double Start, double End, FdmDetectionResult Detection)
{
    public static ReceiverObservation Analyze(MonitorSignal signal, double cursor)
    {
        int count = Math.Min(signal.Samples.Length, (int)Math.Round(signal.SampleRate * .06));
        int end = Math.Clamp((int)Math.Round(cursor * signal.SampleRate), count, signal.Samples.Length);
        int start = end - count;
        return new(start / (double)signal.SampleRate, end / (double)signal.SampleRate,
            new FdmToneDetector().Analyze(signal.Samples.Span.Slice(start, count), signal.SampleRate));
    }

}

public sealed record ReceivedField(string Name, double Start, double End, bool Encoded);
public static class ReceptionTimeline
{
    public static IReadOnlyList<ReceivedField> Fields(FdmTextReceiveResult? reception, bool channelB)
    {
        if (reception?.Synchronization.StartSeconds is not double origin || reception.Synchronization.IsAmbiguous) return [];
        var channel = channelB ? reception.ChannelB : reception.ChannelA;
        if (!channel.HeaderValid) return [];
        var fields = new List<ReceivedField>(); double t = origin;
        void Add(string name, int symbols, bool encoded = false)
        { fields.Add(new(name, t, t + symbols * FdmSequenceDecoder.SymbolDurationSeconds, encoded)); t += symbols * FdmSequenceDecoder.SymbolDurationSeconds; }
        Add("Preámbulo", BfskFrameBuilder.Preamble.Length); Add("Marcador", BfskFrameBuilder.StartMarker.Length);
        int Expand(int bits) => channel.UsesHamming ? bits / 4 * 7 : bits;
        Add("Longitud", Expand(8), channel.UsesHamming);
        if (channel.ByteLength is not >= 1 or > FdmTextFrameBuilder.MaximumUtf8Bytes) return fields.AsReadOnly();
        Add("UTF-8 (según longitud leída)", Expand(channel.ByteLength.Value * 8), channel.UsesHamming);
        Add("CRC", Expand(32), channel.UsesHamming);
        var other = channelB ? reception.ChannelA : reception.ChannelB;
        if (channel.CrcValid == true && other.CrcValid == true && other.ByteLength > channel.ByteLength)
            Add("Relleno esperado, no verificado", Expand((other.ByteLength!.Value - channel.ByteLength.Value) * 8));
        return fields.AsReadOnly();
    }
}
