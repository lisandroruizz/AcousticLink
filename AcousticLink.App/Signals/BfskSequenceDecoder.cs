namespace AcousticLink.App.Signals;

public sealed record BfskSymbolResult(int Index, int StartSample, int EndSample,
    int AnalysisStartSample, int AnalysisSampleCount, BfskDetectionResult Detection);

public sealed record BfskSequenceResult(IReadOnlyList<BfskSymbolResult> Symbols)
{
    public string Bits => string.Concat(Symbols.Select(s => s.Detection.Bit?.ToString() ?? "?"));
    public int ConclusiveCount => Symbols.Count(s => s.Detection.Bit.HasValue);
    public int InconclusiveCount => Symbols.Count - ConclusiveCount;
    public bool IsComplete => InconclusiveCount == 0;
}

public sealed class BfskSequenceDecoder
{
    public const double BitDurationSeconds = 0.10;
    public const int MaximumBits = 64;
    private readonly BfskToneDetector detector;

    public BfskSequenceDecoder(BfskToneDetector? detector = null)
    {
        this.detector = detector ?? new BfskToneDetector();
    }

    public BfskSequenceResult Decode(ReadOnlySpan<float> samples, int sampleRate,
        double startSeconds, int bitCount)
        => DecodeCore(samples, sampleRate, startSeconds, bitCount, MaximumBits);

    internal BfskSequenceResult DecodeMessageBlock(ReadOnlySpan<float> samples, int sampleRate,
        double startSeconds, int bitCount, CancellationToken token = default, int firstSymbol = 0)
        => DecodeCore(samples, sampleRate, startSeconds, bitCount,
            Protocol.FdmTextFrameBuilder.MaximumProtectedFrameBits, token, firstSymbol);

    private BfskSequenceResult DecodeCore(ReadOnlySpan<float> samples, int sampleRate,
        double startSeconds, int bitCount, int maximumBits, CancellationToken token = default, int firstSymbol = 0)
    {
        if (sampleRate <= 2 * BfskToneGenerator.Bit1Frequency)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (!double.IsFinite(startSeconds) || startSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(startSeconds), "El inicio debe ser finito y no negativo.");
        if (bitCount < 1 || bitCount > maximumBits || firstSymbol < 0 || firstSymbol + bitCount > maximumBits)
            throw new ArgumentOutOfRangeException(nameof(bitCount));

        // Calcular cada límite desde el mismo origen: nunca sumar pasos redondeados.
        double origin = startSeconds * sampleRate;
        double step = BitDurationSeconds * sampleRate;
        double end = Math.Round(origin + (firstSymbol + bitCount) * step);
        if (!double.IsFinite(origin) || end > samples.Length)
            throw new ArgumentException("La secuencia completa debe estar dentro de las muestras disponibles.");

        var symbols = new List<BfskSymbolResult>(bitCount);
        for (int i = firstSymbol; i < firstSymbol + bitCount; i++)
        {
            token.ThrowIfCancellationRequested();
            int start = (int)Math.Round(origin + i * step);
            int finish = (int)Math.Round(origin + (i + 1) * step);
            int analysisStart = (int)Math.Round(origin + (i + 0.2) * step);
            int analysisEnd = (int)Math.Round(origin + (i + 0.8) * step);
            var detection = detector.Analyze(samples.Slice(analysisStart, analysisEnd - analysisStart), sampleRate);
            symbols.Add(new(i, start, finish, analysisStart, analysisEnd - analysisStart, detection));
        }
        return new(symbols.AsReadOnly());
    }
}
