using AcousticLink.App.Protocol;
using AcousticLink.App.Signals;

namespace AcousticLink.App.Visualization;

public sealed record DemodulationSelection(ReceivedSymbolTrace? Trace, int StartSample, int SampleCount,
    BfskDetectionResult Detection, int? BlockBitIndex, Hamming74WordTrace? Word, int? OriginalBitIndex)
{
    public static DemodulationSelection? From(MonitorSignal signal, FdmTextReceiveResult? result, bool channelB, double cursor)
    {
        var trace = ReceivedSymbols.At(ReceivedSymbols.From(result, channelB, signal.SampleRate), cursor, signal.SampleRate);
        if (trace is null) return null;
        int header = BfskFrameBuilder.Preamble.Length + BfskFrameBuilder.StartMarker.Length;
        int? blockBit = trace.Number > header ? trace.Number - header - 1 : null;
        var channel = channelB ? result!.ChannelB : result!.ChannelA;
        Hamming74WordTrace? word = blockBit.HasValue && channel.UsesHamming
            ? channel.HammingDiagnostics?.Traces.FirstOrDefault(w => w.WordIndex == blockBit.Value / 7) : null;
        int? original = channel.UsesHamming ? null : blockBit;
        if (word is not null && blockBit.HasValue)
        {
            int dataPosition = (blockBit.Value % 7) switch { 2 => 0, 4 => 1, 5 => 2, 6 => 3, _ => -1 };
            if (dataPosition >= 0) original = word.WordIndex * 4 + dataPosition;
        }
        return new(trace, trace.Symbol.AnalysisStartSample, trace.Symbol.AnalysisSampleCount, trace.Symbol.Detection, blockBit, word, original);
    }
    public static BfskSymbolResult? SymbolForBlockBit(FdmTextChannelResult? channel, int index) =>
        channel?.Block is { } block && index >= 0 && index < block.Symbols.Count ? block.Symbols[index] : null;
    // Navegación sobre las decisiones conservadas; nunca crea símbolos ausentes.
    public static ReceivedSymbolTrace? Find(IReadOnlyList<ReceivedSymbolTrace> traces, int currentNumber,
        int direction, bool unknownOnly = false, bool fromStart = false)
    {
        var candidates = traces.Where(t => !unknownOnly || t.Symbol.Detection.Bit is null);
        return direction < 0 ? candidates.LastOrDefault(t => t.Number < currentNumber)
            : candidates.FirstOrDefault(t => fromStart || t.Number > currentNumber);
    }
    public static BfskSymbolResult? NextProblem(FdmTextChannelResult? channel, int currentBlockBit) =>
        channel?.HeaderValid != true ? null : channel.HammingDiagnostics?.Traces
            .Where(w => w.State != Hamming74TraceState.Unchanged && w.EncodedBitOffset > currentBlockBit)
            .Select(w => SymbolForBlockBit(channel, w.EncodedBitOffset)).FirstOrDefault(s => s is not null);

    public string Field(FdmTextChannelResult? channel)
    {
        if (BlockBitIndex is null) return Trace is null ? "Sin posición de trama" : "Cabecera de sincronización";
        int? original = OriginalBitIndex ?? (Word is not null ? Word.WordIndex * 4 : channel?.UsesHamming == true ? BlockBitIndex.Value / 7 * 4 : null);
        if (original is null) return "Campo no determinado";
        if (original < 8) return "Campo longitud";
        if (channel?.ByteLength is not int length || length is < 1 or > 8) return "Campo no determinado: longitud no recuperada";
        if (original < 8 + length * 8) return "Contenido UTF-8";
        return original < 8 + length * 8 + 32 ? "Campo CRC-32" : "Fuera del bloque del mensaje";
    }
    public static double? EnergyRatio(BfskDetectionResult d)
    {
        double high = Math.Max(d.Energy0, d.Energy1), low = Math.Min(d.Energy0, d.Energy1);
        return high == 0 ? null : low == 0 ? double.PositiveInfinity : high / low;
    }
}
