using AcousticLink.App.Protocol;
using AcousticLink.App.Signals;

namespace AcousticLink.App.Visualization;

public sealed record ReceivedSymbolTrace(bool ChannelB, int Number, BfskSymbolResult Symbol, string Section)
{
    public string Value => Symbol.Detection.Bit?.ToString() ?? "?";
    public double? Frequency => Symbol.Detection.Bit is int bit ? CarrierEnergyMap.Frequencies[(ChannelB ? 2 : 0) + bit] : null;
}

public static class ReceivedSymbols
{
    // Solo referencias a decisiones ya tomadas. Sin detector nuevo, referencias de texto ni metadatos TX.
    public static IReadOnlyList<ReceivedSymbolTrace> From(FdmTextReceiveResult? result, bool channelB, int rate)
    {
        if (rate <= 0) throw new ArgumentOutOfRangeException(nameof(rate));
        if (result is null || result.Synchronization.IsAmbiguous || result.Synchronization.StartSeconds is null) return [];
        var channel = channelB ? result.ChannelB : result.ChannelA;
        var sync = channelB ? result.Synchronization.ChannelB : result.Synchronization.ChannelA;
        if (!channel.HeaderValid || sync is null) return [];
        var fields = ReceptionTimeline.Fields(result, channelB);
        var symbols = new List<ReceivedSymbolTrace>();
        void Add(BfskSymbolResult symbol, int number)
        {
            double time = (symbol.AnalysisStartSample + symbol.AnalysisSampleCount / 2.0) / rate;
            string section = fields.FirstOrDefault(f => time >= f.Start && time < f.End)?.Name ?? "Campo no ubicado";
            symbols.Add(new(channelB, number, symbol, section));
        }
        foreach (var symbol in sync.Header.Symbols) Add(symbol, symbol.Index + 1);
        if (channel.Block is not null)
            foreach (var symbol in channel.Block.Symbols) Add(symbol, sync.Header.Symbols.Count + symbol.Index + 1);
        return symbols.AsReadOnly();
    }
    public static ReceivedSymbolTrace? At(IReadOnlyList<ReceivedSymbolTrace> symbols, double seconds, int rate) =>
        symbols.FirstOrDefault(s => seconds * rate >= s.Symbol.StartSample && seconds * rate < s.Symbol.EndSample);
}
