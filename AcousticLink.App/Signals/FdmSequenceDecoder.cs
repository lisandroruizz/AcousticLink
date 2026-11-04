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
        if (symbolCount < 1 || symbolCount > MaximumSymbols)
            throw new ArgumentOutOfRangeException(nameof(symbolCount), "Se requieren entre 1 y 16 símbolos.");
        return DecodeCore(samples, sampleRate, startSeconds, symbolCount);
    }

    // Inicio conocido; devuelve todos los símbolos sin buscar ni validar automáticamente cabeceras.
    public FdmSequenceResult DecodeFrameSymbols(ReadOnlySpan<float> samples, int sampleRate,
        double startSeconds, int dataBitCount)
    {
        if (dataBitCount < 1 || dataBitCount > BfskFrameBuilder.MaximumDataBits)
            throw new ArgumentOutOfRangeException(nameof(dataBitCount), "Se requieren entre 1 y 16 bits de datos.");
        return DecodeCore(samples, sampleRate, startSeconds, FdmBitstreamGenerator.HeaderBits + dataBitCount);
    }

    public FdmSequenceResult DecodeHeaders(ReadOnlySpan<float> samples, int sampleRate,
        double startSeconds, CancellationToken cancellationToken = default) =>
        DecodeCore(samples, sampleRate, startSeconds, FdmBitstreamGenerator.HeaderBits, cancellationToken);

    // Modo acotado del protocolo de texto; no cambia el máximo del modo manual de prueba.
    public FdmSequenceResult DecodeMessageBlock(ReadOnlySpan<float> samples, int sampleRate,
        double startSeconds, int symbolCount, CancellationToken cancellationToken = default)
    {
        int maximum = Protocol.FdmTextFrameBuilder.MaximumFrameBits - FdmBitstreamGenerator.HeaderBits;
        if (symbolCount < 8 || symbolCount > maximum) throw new ArgumentOutOfRangeException(nameof(symbolCount));
        return DecodeCore(samples, sampleRate, startSeconds, symbolCount, cancellationToken);
    }

    public FdmSequenceResult DecodeProtectedMessageBlock(ReadOnlySpan<float> samples, int sampleRate,
        double startSeconds, int symbolCount, CancellationToken cancellationToken = default)
    {
        int maximum = Protocol.FdmTextFrameBuilder.MaximumProtectedFrameBits - FdmBitstreamGenerator.HeaderBits;
        if (symbolCount < 14 || symbolCount > maximum || symbolCount % 7 != 0)
            throw new ArgumentOutOfRangeException(nameof(symbolCount));
        return DecodeCore(samples, sampleRate, startSeconds, symbolCount, cancellationToken);
    }

    // Una decisión nueva con el mismo origen absoluto y las mismas ventanas que el bloque manual.
    public FdmSequenceResult DecodeMessageSymbol(ReadOnlySpan<float> samples, int sampleRate,
        double blockStartSeconds, int index, CancellationToken token = default)
    {
        int maximum = Protocol.FdmTextFrameBuilder.MaximumProtectedFrameBits - FdmBitstreamGenerator.HeaderBits;
        if (index < 0 || index >= maximum) throw new ArgumentOutOfRangeException(nameof(index));
        return DecodeCore(samples, sampleRate, blockStartSeconds, 1, token, index);
    }

    private FdmSequenceResult DecodeCore(ReadOnlySpan<float> samples, int sampleRate,
        double startSeconds, int symbolCount, CancellationToken cancellationToken = default, int firstSymbol = 0)
    {
        if (sampleRate <= 2 * FdmToneMixer.ChannelBBit1Frequency)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (!double.IsFinite(startSeconds) || startSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(startSeconds), "El inicio debe ser finito y no negativo.");
        double origin = startSeconds * sampleRate;
        double step = SymbolDurationSeconds * sampleRate;
        if (!double.IsFinite(origin) || Math.Round(origin + (firstSymbol + symbolCount) * step) > samples.Length)
            throw new ArgumentException("Ambas secuencias completas deben estar dentro de la grabación.");

        var a = new List<BfskSymbolResult>(symbolCount);
        var b = new List<BfskSymbolResult>(symbolCount);
        for (int i = firstSymbol; i < firstSymbol + symbolCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
