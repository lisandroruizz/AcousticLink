namespace AcousticLink.App.Signals;

public sealed record FdmSequenceResult(BfskSequenceResult ChannelA, BfskSequenceResult ChannelB);

public sealed class FdmSequenceDecoder
{
    public const int MaximumSymbols = FdmBitstreamGenerator.MaximumBits;
    public const double SymbolDurationSeconds = FdmBitstreamGenerator.SymbolDurationSeconds;
    private readonly FdmToneDetector detector;

    public FdmSequenceDecoder(FdmToneDetector? detector = null)
    {
        this.detector = detector ?? new FdmToneDetector();
    }

    public FdmSequenceResult Decode(ReadOnlySpan<float> samples, int sampleRate,
        double startSeconds, int symbolCount)
    {
        if (sampleRate <= 2 * FdmToneMixer.ChannelBBit1Frequency)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (!double.IsFinite(startSeconds) || startSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(startSeconds), "El inicio debe ser finito y no negativo.");
        if (symbolCount < 1 || symbolCount > MaximumSymbols)
            throw new ArgumentOutOfRangeException(nameof(symbolCount), "Se requieren entre 1 y 16 símbolos.");
        double origin = startSeconds * sampleRate;
        double step = SymbolDurationSeconds * sampleRate;
        if (!double.IsFinite(origin) || Math.Round(origin + symbolCount * step) > samples.Length)
            throw new ArgumentException("Ambas secuencias completas deben estar dentro de la grabación.");

        var a = new List<BfskSymbolResult>(symbolCount);
        var b = new List<BfskSymbolResult>(symbolCount);
        for (int i = 0; i < symbolCount; i++)
        {
            // Cada límite parte del origen absoluto, sin sumar pasos ya redondeados.
            int start = (int)Math.Round(origin + i * step);
            int end = (int)Math.Round(origin + (i + 1) * step);
            int centerStart = (int)Math.Round(origin + (i + 0.2) * step);
            int centerEnd = (int)Math.Round(origin + (i + 0.8) * step);
            int length = centerEnd - centerStart;
            var detected = detector.Analyze(samples.Slice(centerStart, length), sampleRate);
            a.Add(new(i, start, end, centerStart, length, detected.ChannelA));
            b.Add(new(i, start, end, centerStart, length, detected.ChannelB));
        }
        // Reutiliza cadenas con '?', conteos y estado completo/parcial del resultado existente.
        return new(new(a.AsReadOnly()), new(b.AsReadOnly()));
    }
}
