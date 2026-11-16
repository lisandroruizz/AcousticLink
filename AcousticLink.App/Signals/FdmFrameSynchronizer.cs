namespace AcousticLink.App.Signals;

public enum FdmSynchronizationState { Failed, Partial, Complete }

public sealed record FdmSynchronizedChannel(bool HeaderValid, BfskSequenceResult Header, BfskSequenceResult Data);

public sealed record FdmSynchronizationResult(FdmSynchronizationState State, double? StartSeconds,
    double? DataStartSeconds, FdmSynchronizedChannel? ChannelA, FdmSynchronizedChannel? ChannelB, string Reason)
{
    public bool IsAmbiguous { get; init; }
}

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
        if (dataBitCount < 1 || dataBitCount > BfskFrameBuilder.MaximumDataBits)
            throw new ArgumentOutOfRangeException(nameof(dataBitCount), "Se requieren entre 1 y 16 bits de datos por canal.");
        return FindCore(samples, sampleRate, dataBitCount, cancellationToken);
    }

    public FdmSynchronizationResult FindHeaders(ReadOnlySpan<float> samples, int sampleRate,
        CancellationToken cancellationToken = default) => FindCore(samples, sampleRate, 0, cancellationToken);

    private FdmSynchronizationResult FindCore(ReadOnlySpan<float> samples, int sampleRate, int dataBitCount,
        CancellationToken cancellationToken)
    {
        if (sampleRate <= 2 * FdmToneMixer.ChannelBBit1Frequency)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        cancellationToken.ThrowIfCancellationRequested();
        foreach (float value in samples)
            if (!float.IsFinite(value)) throw new ArgumentException("Las muestras deben ser finitas.", nameof(samples));

        string header = BfskFrameBuilder.Preamble + BfskFrameBuilder.StartMarker;
        double step = FdmSequenceDecoder.SymbolDurationSeconds * sampleRate;
        double requiredSamples = Math.Round((header.Length + dataBitCount) * step);
        if (requiredSamples > samples.Length) return Failure("Grabación insuficiente o truncada para las tramas esperadas.");
        int lastStart = samples.Length - (int)requiredSamples;
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
        if (groups.Count > 1) return Failure("Sincronización ambigua: varias tramas o cabeceras separadas; no se elige una arbitrariamente.") with { IsAmbiguous = true };

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

        return Recover(samples, sampleRate, best.Position, dataBitCount, cancellationToken);
    }

    private FdmSynchronizationResult Recover(ReadOnlySpan<float> samples, int sampleRate, int position,
        int dataBitCount, CancellationToken cancellationToken)
    {
        string header = BfskFrameBuilder.Preamble + BfskFrameBuilder.StartMarker;
        double start = position / (double)sampleRate;
        double dataStart = start + header.Length * FdmSequenceDecoder.SymbolDurationSeconds;
        var recovered = dataBitCount == 0 ? decoder.DecodeHeaders(samples, sampleRate, start, cancellationToken)
            : decoder.DecodeFrameSymbols(samples, sampleRate, start, dataBitCount);
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
                ? dataBitCount == 0 ? "Ambas cabeceras validadas; longitud y datos todavía no analizados."
                    : "Ambas cabeceras validadas. Los datos conservan sus '?' y no tienen verificación de integridad."
                : "Solo una cabecera es válida: inicio estimado desde ese canal. Los datos del otro canal no están sincronizados de forma validada.");
    }

    // Conserva la grilla gruesa, agrupación y refinamiento del método manual.
    // Solo evalúa nuevos inicios completos; el extremo no alineado se incorpora al finalizar.
    public HeaderSearch CreateHeaderSearch(int sampleRate) => new(this, sampleRate);
    public sealed class HeaderSearch
    {
        private readonly FdmFrameSynchronizer owner;
        private readonly int rate, coarse, fine, headerSamples;
        private readonly List<Candidate> groups = [];
        private int next, lastMatch = -1, previousCount;
        private Candidate? refinedFrom;
        private Candidate? refiningBest;
        private int nextFine, endFine;
        private bool finePending;
        private FdmSynchronizationResult? cached;
        public int EvaluatedStarts { get; private set; }
        internal HeaderSearch(FdmFrameSynchronizer owner, int rate)
        {
            if (rate <= 2 * FdmToneMixer.ChannelBBit1Frequency) throw new ArgumentOutOfRangeException(nameof(rate));
            this.owner = owner; this.rate = rate;
            coarse = Math.Max(1, (int)Math.Round(CoarseStepSeconds * rate));
            fine = Math.Max(1, (int)Math.Round(FineStepSeconds * rate));
            headerSamples = (int)Math.Round(FdmBitstreamGenerator.HeaderBits * FdmSequenceDecoder.SymbolDurationSeconds * rate);
        }
        public FdmSynchronizationResult Advance(ReadOnlySpan<float> samples, bool final = false, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (samples.Length < previousCount) throw new ArgumentException("El prefijo no puede retroceder.");
            int addedSamples = samples.Length - previousCount;
            previousCount = samples.Length;
            int last = samples.Length - headerSamples;
            string header = BfskFrameBuilder.Preamble + BfskFrameBuilder.StartMarker;
            void Add(Candidate? candidate, int position)
            {
                if (candidate is null) return;
                if (lastMatch < 0 || position - lastMatch > FdmSequenceDecoder.SymbolDurationSeconds * rate) groups.Add(candidate);
                else if (Better(candidate, groups[^1])) groups[^1] = candidate;
                lastMatch = position;
            }
            while (next <= last)
            {
                token.ThrowIfCancellationRequested();
                Add(owner.Evaluate(samples, rate, next, header, token), next); EvaluatedStarts++; next += coarse;
            }
            if (final && last >= 0 && last % coarse != 0) { Add(owner.Evaluate(samples, rate, last, header, token), last); EvaluatedStarts++; }
            if (groups.Count == 0) return Failure("Buscando cabecera: todavía no hay una cabecera válida completa.");
            if (groups.Count > 1) return Failure("Sincronización ambigua: varias cabeceras separadas.") with { IsAmbiguous = true };
            // Esperar a que cierre el grupo evita fijar el borde inicial de una cabecera todavía incompleta.
            if (!final && last < lastMatch + (int)Math.Ceiling(.1 * rate) + coarse)
                return Failure("Candidata completa en evaluación temporal; cabecera todavía no confirmada.");
            var best = groups[0];
            if (!final && best == refinedFrom && cached is not null) return cached;
            if (best != refinedFrom || !finePending)
            {
                refinedFrom = best; refiningBest = best; cached = null;
                nextFine = Math.Max(0, best.Position - coarse);
                endFine = Math.Min(last, best.Position + coarse);
                finePending = true;
            }
            // Conservar las mismas posiciones y criterios; repartir la grilla según el audio nuevo.
            int budget = final ? int.MaxValue : Math.Min(12, Math.Max(1, addedSamples / Math.Max(1, rate / 200)));
            while (finePending && budget-- > 0)
            {
                token.ThrowIfCancellationRequested();
                int position = nextFine;
                var candidate = owner.Evaluate(samples, rate, position, header, token);
                EvaluatedStarts++;
                if (candidate is not null && Better(candidate, refiningBest!)) refiningBest = candidate;
                if (position == endFine) finePending = false;
                else nextFine = Math.Min(endFine, position + fine);
            }
            if (finePending) return Failure("Candidata completa en refinamiento temporal; cabecera todavía no confirmada.");
            return cached = owner.Recover(samples, rate, refiningBest!.Position, 0, token);
        }
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
