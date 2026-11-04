namespace AcousticLink.App.Visualization;

// Medidas sobre las muestras originales; los índices son absolutos y base cero.
public sealed record SymbolWindowObservation(int SampleRate, int StartSample, int SampleCount,
    int AnalysisStartSample, int AnalysisSampleCount, double Rms, float Peak, double AnalysisRms, float AnalysisPeak)
{
    public static SymbolWindowObservation? From(MonitorSignal signal, DemodulationSelection? selection,
        int exploratoryStart, int exploratoryCount)
    {
        int start = selection?.Trace?.Symbol.StartSample ?? exploratoryStart;
        int end = selection?.Trace?.Symbol.EndSample ?? exploratoryStart + exploratoryCount;
        int analysisStart = selection?.StartSample ?? exploratoryStart;
        int analysisCount = selection?.SampleCount ?? exploratoryCount;
        if (start < 0 || end <= start || end > signal.Samples.Length || analysisCount <= 0 || analysisStart < start || analysisStart + analysisCount > end) return null;
        static (double Rms, float Peak) Measure(ReadOnlySpan<float> samples)
        {
            double squares = 0; float peak = 0;
            foreach (float value in samples) { squares += (double)value * value; peak = Math.Max(peak, Math.Abs(value)); }
            return (Math.Sqrt(squares / samples.Length), peak);
        }
        var full = Measure(signal.Samples.Span.Slice(start, end - start));
        var center = Measure(signal.Samples.Span.Slice(analysisStart, analysisCount));
        return new(signal.SampleRate, start, end - start, analysisStart, analysisCount, full.Rms, full.Peak, center.Rms, center.Peak);
    }
}
