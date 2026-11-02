namespace AcousticLink.App.Signals;

public enum FdmSynchronizationState { Failed, Partial, Complete }

public sealed record FdmSynchronizedChannel(bool HeaderValid, BfskSequenceResult Header, BfskSequenceResult Data);

public sealed record FdmSynchronizationResult(FdmSynchronizationState State, double? StartSeconds,
    double? DataStartSeconds, FdmSynchronizedChannel? ChannelA, FdmSynchronizedChannel? ChannelB, string Reason);

public sealed class FdmFrameSynchronizer
{
    // Misma estrategia temporal del sincronizador BFSK: búsqueda a 20 ms y ajuste a 1 ms.
    private const double CoarseStepSeconds = 0.02;
    private const double FineStepSeconds = 0.001;
    private readonly FdmToneDetector detector;
    private readonly FdmSequenceDecoder decoder;
    private sealed record Candidate(int Position, int ValidChannels, double Score);

    public FdmFrameSynchronizer(FdmToneDetector? detector = null)
    {
        this.detector = detector ?? new FdmToneDetector();
        decoder = new FdmSequenceDecoder(this.detector);
    }

    public FdmSynchronizationResult Find(ReadOnlySpan<float> samples, int sampleRate, int dataBitCount,
        CancellationToken cancellationToken = default)
    {
        if (sampleRate <= 2 * FdmToneMixer.ChannelBBit1Frequency)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (dataBitCount < 1 || dataBitCount > BfskFrameBuilder.MaximumDataBits)
            throw new ArgumentOutOfRangeException(nameof(dataBitCount), "Se requieren entre 1 y 16 bits de datos por canal.");
        cancellationToken.ThrowIfCancellationRequested();
        foreach (float value in samples)
            if (!float.IsFinite(value)) throw new ArgumentException("Las muestras deben ser finitas.", nameof(samples));

        string header = BfskFrameBuilder.Preamble + BfskFrameBuilder.StartMarker;
        double step = FdmSequenceDecoder.SymbolDurationSeconds * sampleRate;
        int lastStart = samples.Length - (int)Math.Round((header.Length + dataBitCount) * step);
        if (lastStart < 0) return Failure("Grabación insuficiente o truncada para las tramas esperadas.");
        int coarseStep = Math.Max(1, (int)Math.Round(CoarseStepSeconds * sampleRate));
        int fineStep = Math.Max(1, (int)Math.Round(FineStepSeconds * sampleRate));
        var groups = new List<Candidate>();
        int lastMatch = -1;
        for (int position = 0; ; position = Math.Min(lastStart, position + coarseStep))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = Evaluate(samples, sampleRate, position, header, cancellationToken);
            if (candidate is not null)
            {
                if (lastMatch < 0 || position - lastMatch > step) groups.Add(candidate);
                else if (Better(candidate, groups[^1])) groups[^1] = candidate;
                lastMatch = position;
            }
            if (position == lastStart) break;
        }
        if (groups.Count == 0) return Failure("Ningún canal presenta preámbulo y marcador exactos y concluyentes.");
        if (groups.Count > 1) return Failure("Sincronización ambigua: varias tramas o cabeceras separadas; no se elige una arbitrariamente.");

        Candidate best = groups[0];
        int begin = Math.Max(0, best.Position - coarseStep);
        int end = Math.Min(lastStart, best.Position + coarseStep);
        for (int position = begin; ; position = Math.Min(end, position + fineStep))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = Evaluate(samples, sampleRate, position, header, cancellationToken);
            if (candidate is not null && Better(candidate, best)) best = candidate;
            if (position == end) break;
        }

        double start = best.Position / (double)sampleRate;
        double dataStart = start + header.Length * FdmSequenceDecoder.SymbolDurationSeconds;
        var recovered = decoder.DecodeFrameSymbols(samples, sampleRate, start, dataBitCount);
        FdmSynchronizedChannel Split(BfskSequenceResult channel)
        {
            var head = new BfskSequenceResult(Array.AsReadOnly(channel.Symbols.Take(header.Length).ToArray()));
            var data = new BfskSequenceResult(Array.AsReadOnly(channel.Symbols.Skip(header.Length).ToArray()));
            return new(head.IsComplete && head.Bits == header, head, data);
        }
        var a = Split(recovered.ChannelA);
        var b = Split(recovered.ChannelB);
        bool complete = a.HeaderValid && b.HeaderValid;
        return new(complete ? FdmSynchronizationState.Complete : FdmSynchronizationState.Partial,
            start, dataStart, a, b, complete
                ? "Ambas cabeceras validadas. Los datos conservan sus '?' y no tienen verificación de integridad."
                : "Solo una cabecera es válida: inicio estimado desde ese canal. Los datos del otro canal no están sincronizados de forma validada.");
    }

    private Candidate? Evaluate(ReadOnlySpan<float> samples, int rate, int position, string header, CancellationToken token)
    {
        bool validA = true, validB = true;
        double start = position / (double)rate;
        for (int i = 0; i < header.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            var symbol = decoder.Decode(samples, rate, start + i * FdmSequenceDecoder.SymbolDurationSeconds, 1);
            int expected = header[i] - '0';
            validA &= symbol.ChannelA.Symbols[0].Detection.Bit == expected;
            validB &= symbol.ChannelB.Symbols[0].Detection.Bit == expected;
            if (!validA && !validB) return null;
        }
        // Afinar con intervalos completos penaliza ventanas que mezclan símbolos vecinos.
        // Solo se puntúan los canales cuya cabecera pasó la validación central completa.
        double score = 0;
        for (int i = 0; i < header.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            int begin = position + (int)Math.Round(i * FdmSequenceDecoder.SymbolDurationSeconds * rate);
            int end = position + (int)Math.Round((i + 1) * FdmSequenceDecoder.SymbolDurationSeconds * rate);
            var result = detector.Analyze(samples.Slice(begin, end - begin), rate);
            if (validA) score += Contrast(result.ChannelA, header[i]);
            if (validB) score += Contrast(result.ChannelB, header[i]);
        }
        int validChannels = (validA ? 1 : 0) + (validB ? 1 : 0);
        return new(position, validChannels, score / (header.Length * validChannels));
    }

    private static double Contrast(BfskDetectionResult result, char bit)
    {
        double total = result.Energy0 + result.Energy1;
        double difference = bit == '0' ? result.Energy0 - result.Energy1 : result.Energy1 - result.Energy0;
        return total > 0 ? difference / total : 0;
    }

    private static bool Better(Candidate candidate, Candidate current) =>
        candidate.ValidChannels > current.ValidChannels ||
        (candidate.ValidChannels == current.ValidChannels && candidate.Score > current.Score);

    private static FdmSynchronizationResult Failure(string reason) =>
        new(FdmSynchronizationState.Failed, null, null, null, null, reason);
}
