namespace AcousticLink.App.Signals;

public sealed record BfskSynchronizationResult(bool Found, double? StartSeconds, string Preamble,
    string Marker, double? DataStartSeconds, BfskSequenceResult? Data, string Reason);

public sealed class BfskFrameSynchronizer
{
    // Pasos de búsqueda iniciales: 20 ms y 1 ms. No se prueba cada muestra.
    private const double CoarseStepSeconds = 0.02;
    private const double FineStepSeconds = 0.001;
    private readonly BfskToneDetector detector;
    private readonly BfskSequenceDecoder decoder;

    public BfskFrameSynchronizer(BfskToneDetector? detector = null)
    {
        this.detector = detector ?? new BfskToneDetector();
        decoder = new BfskSequenceDecoder(this.detector);
    }

    public BfskSynchronizationResult Find(ReadOnlySpan<float> samples, int sampleRate, int dataBitCount,
        CancellationToken cancellationToken = default)
    {
        if (sampleRate <= 2 * BfskToneGenerator.Bit1Frequency)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (dataBitCount < 1 || dataBitCount > BfskFrameBuilder.MaximumDataBits)
            throw new ArgumentOutOfRangeException(nameof(dataBitCount), "Se requieren entre 1 y 16 bits de datos.");
        foreach (float value in samples)
            if (!float.IsFinite(value)) throw new ArgumentException("Las muestras deben ser finitas.", nameof(samples));

        string header = BfskFrameBuilder.Preamble + BfskFrameBuilder.StartMarker;
        double symbolSamples = BfskSequenceDecoder.BitDurationSeconds * sampleRate;
        int frameSamples = (int)Math.Round((header.Length + dataBitCount) * symbolSamples);
        int lastStart = samples.Length - frameSamples;
        if (lastStart < 0) return Failure("Grabación insuficiente o truncada para la trama esperada.");
        int coarseStep = Math.Max(1, (int)Math.Round(CoarseStepSeconds * sampleRate));
        int fineStep = Math.Max(1, (int)Math.Round(FineStepSeconds * sampleRate));
        var groups = new List<(int Position, double Score)>();
        int lastMatch = -1;
        // Posiciones ordenadas; coincidencias cercanas representan la misma trama.
        for (int position = 0; ; position = Math.Min(lastStart, position + coarseStep))
        {
            cancellationToken.ThrowIfCancellationRequested();
            double score = HeaderScore(samples, sampleRate, position, header);
            if (score >= 0)
            {
                if (lastMatch < 0 || position - lastMatch > symbolSamples)
                    groups.Add((position, score));
                else if (score > groups[^1].Score)
                    groups[^1] = (position, score);
                lastMatch = position;
            }
            if (position == lastStart) break;
        }
        if (groups.Count == 0) return Failure("No se encontró un preámbulo y marcador completos, exactos y concluyentes.");
        if (groups.Count > 1) return Failure("Sincronización ambigua: hay varias tramas candidatas separadas.");

        var best = groups[0];
        int begin = Math.Max(0, best.Position - coarseStep);
        int end = Math.Min(lastStart, best.Position + coarseStep);
        for (int position = begin; ; position = Math.Min(end, position + fineStep))
        {
            cancellationToken.ThrowIfCancellationRequested();
            double score = HeaderScore(samples, sampleRate, position, header);
            if (score > best.Score) best = (position, score);
            if (position == end) break;
        }

        double start = best.Position / (double)sampleRate;
        var recovered = decoder.Decode(samples, sampleRate, start, header.Length);
        double dataStart = start + header.Length * BfskSequenceDecoder.BitDurationSeconds;
        var data = decoder.Decode(samples, sampleRate, dataStart, dataBitCount);
        return new(true, start, recovered.Bits[..BfskFrameBuilder.Preamble.Length],
            recovered.Bits[BfskFrameBuilder.Preamble.Length..], dataStart, data,
            data.IsComplete ? "Cabecera validada; datos concluyentes, sin verificación de integridad."
                : "Cabecera validada; datos incompletos: se conservan los símbolos '?'.");
    }

    private double HeaderScore(ReadOnlySpan<float> samples, int rate, int position, string header)
    {
        double start = position / (double)rate;
        // Descartar al primer símbolo incorrecto. El decodificador reutiliza Goertzel.
        for (int i = 0; i < header.Length; i++)
        {
            var bit = decoder.Decode(samples, rate, start + i * BfskSequenceDecoder.BitDurationSeconds, 1);
            if (bit.Symbols[0].Detection.Bit != header[i] - '0') return -1;
        }
        double score = 0;
        for (int i = 0; i < header.Length; i++)
        {
            int begin = position + (int)Math.Round(i * BfskSequenceDecoder.BitDurationSeconds * rate);
            int end = position + (int)Math.Round((i + 1) * BfskSequenceDecoder.BitDurationSeconds * rate);
            // Solo para afinar el tiempo: medir el intervalo completo penaliza mezclar tonos vecinos.
            var result = detector.Analyze(samples.Slice(begin, end - begin), rate);
            double expected = header[i] == '0' ? result.Energy0 : result.Energy1;
            double other = header[i] == '0' ? result.Energy1 : result.Energy0;
            double total = expected + other;
            score += total > 0 ? (expected - other) / total : 0;
        }
        return score / header.Length;
    }

    private static BfskSynchronizationResult Failure(string reason) => new(false, null, "", "", null, null, reason);
}
