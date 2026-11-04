using AcousticLink.App.Signals;

namespace AcousticLink.App.Protocol;

public enum FdmTextReceiveState
{
    Valid, HeaderNotSynchronized, AmbiguousSynchronization, InvalidLength, InconclusiveLength,
    Truncated, InconclusiveBits, CrcIncorrect, InvalidUtf8
}

public sealed record FdmTextChannelResult(FdmTextReceiveState State, bool HeaderValid, int? ByteLength,
    bool? CrcValid, string? Text, BfskSequenceResult? Block, string Reason)
{
    public bool IsReceiving { get; init; }
    public bool UsesHamming { get; init; }
    public bool ErasureRecoveryEnabled { get; init; }
    public Hamming74DecodeResult? HammingDiagnostics { get; init; }
    public bool HammingCoversCompleteBlock { get; init; }
}

public sealed record FdmTextReceiveResult(FdmSynchronizationResult Synchronization,
    FdmTextChannelResult ChannelA, FdmTextChannelResult ChannelB);

public sealed class FdmTextReceiver
{
    private readonly FdmFrameSynchronizer synchronizer = new();
    private readonly FdmSequenceDecoder decoder = new();
    private readonly MessageCodec codec = new();

    // Modo explícito acordado con el emisor; no se detecta automáticamente.
    public FdmTextReceiveResult Receive(ReadOnlySpan<float> samples, int sampleRate, CancellationToken token = default,
        bool useHamming = false, bool recoverErasures = false)
    {
        var sync = synchronizer.FindHeaders(samples, sampleRate, token);
        return ReceiveSynchronized(samples, sampleRate, sync, token, useHamming, recoverErasures);
    }

    internal FdmTextReceiveResult ReceiveSynchronized(ReadOnlySpan<float> samples, int sampleRate,
        FdmSynchronizationResult sync, CancellationToken token, bool useHamming, bool recoverErasures)
    {
        if (!sync.DataStartSeconds.HasValue)
        {
            var state = sync.IsAmbiguous ? FdmTextReceiveState.AmbiguousSynchronization
                : samples.Length / (double)sampleRate < FdmBitstreamGenerator.HeaderBits * FdmSequenceDecoder.SymbolDurationSeconds
                    ? FdmTextReceiveState.Truncated : FdmTextReceiveState.HeaderNotSynchronized;
            var failed = new FdmTextChannelResult(state, false, null, null, null, null, sync.Reason)
            { UsesHamming = useHamming, ErasureRecoveryEnabled = useHamming && recoverErasures };
            return new(sync, failed, failed);
        }
        var a = useHamming ? ReadProtectedChannel(samples, sampleRate, sync.DataStartSeconds.Value, sync.ChannelA!.HeaderValid, true, token, recoverErasures)
            : ReadChannel(samples, sampleRate, sync.DataStartSeconds.Value, sync.ChannelA!.HeaderValid, true, token);
        var b = useHamming ? ReadProtectedChannel(samples, sampleRate, sync.DataStartSeconds.Value, sync.ChannelB!.HeaderValid, false, token, recoverErasures)
            : ReadChannel(samples, sampleRate, sync.DataStartSeconds.Value, sync.ChannelB!.HeaderValid, false, token);
        return new(sync, a, b);
    }

    private FdmTextChannelResult ReadChannel(ReadOnlySpan<float> samples, int rate, double start,
        bool headerValid, bool channelA, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!headerValid) return new(FdmTextReceiveState.HeaderNotSynchronized, false, null, null, null, null, "Cabecera del canal no sincronizada.");
        if (!Fits(samples.Length, rate, start, 8))
            return new(FdmTextReceiveState.Truncated, true, null, null, null, null, "Grabación insuficiente para leer la longitud.");
        var lengths = decoder.DecodeMessageBlock(samples, rate, start, 8, token);
        var lengthBits = channelA ? lengths.ChannelA : lengths.ChannelB;
        if (!lengthBits.IsComplete)
            return new(FdmTextReceiveState.InconclusiveLength, true, null, null, null, lengthBits, "Campo de longitud no concluyente.");
        int length = Convert.ToInt32(lengthBits.Bits, 2);
        if (length < 1 || length > FdmTextFrameBuilder.MaximumUtf8Bytes)
            return new(FdmTextReceiveState.InvalidLength, true, length, null, null, lengthBits, "Longitud fuera del rango acústico de 1 a 8 bytes.");
        int blockBits = 8 + length * 8 + MessageCodec.CrcBytes * 8;
        if (!Fits(samples.Length, rate, start, blockBits))
            return new(FdmTextReceiveState.Truncated, true, length, null, null, null, "Grabación truncada antes del final del bloque y su CRC.");
        var blocks = decoder.DecodeMessageBlock(samples, rate, start, blockBits, token);
        var block = channelA ? blocks.ChannelA : blocks.ChannelB;
        if (!block.IsComplete)
            return new(FdmTextReceiveState.InconclusiveBits, true, length, null, null, block, "Bloque con bits '?' no concluyentes; CRC no comprobado.");
        return ValidateMessage(block.Bits, block, length);
    }

    internal FdmTextChannelResult ValidateMessage(string bits, BfskSequenceResult block, int length)
    {
        try
        {
            string text = codec.Decode(bits);
            return new(FdmTextReceiveState.Valid, true, length, true, text, block, "Mensaje recibido con CRC válido y UTF-8 estricto.");
        }
        catch (FormatException ex) when (ex.InnerException is System.Text.DecoderFallbackException)
        {
            return new(FdmTextReceiveState.InvalidUtf8, true, length, true, null, block, "CRC válido, pero contenido UTF-8 inválido.");
        }
        catch (FormatException ex)
        {
            return new(FdmTextReceiveState.CrcIncorrect, true, length, false, null, block, ex.Message);
        }
    }

    private FdmTextChannelResult ReadProtectedChannel(ReadOnlySpan<float> samples, int rate, double start,
        bool headerValid, bool channelA, CancellationToken token, bool recoverErasures)
    {
        token.ThrowIfCancellationRequested();
        Hamming74DecodeResult? diagnostics = null;
        bool completeBlock = false;
        FdmTextChannelResult Failure(FdmTextReceiveState state, string reason, int? length = null, BfskSequenceResult? block = null) =>
            new(state, headerValid, length, null, null, block, reason) { UsesHamming = true, HammingDiagnostics = diagnostics,
                ErasureRecoveryEnabled = recoverErasures, HammingCoversCompleteBlock = completeBlock };
        if (!headerValid) return Failure(FdmTextReceiveState.HeaderNotSynchronized, "Cabecera del canal no sincronizada.");
        if (!Fits(samples.Length, rate, start, 14))
            return Failure(FdmTextReceiveState.Truncated, "Captura insuficiente para las dos palabras Hamming de longitud.");
        var lengths = decoder.DecodeProtectedMessageBlock(samples, rate, start, 14, token);
        var lengthSymbols = channelA ? lengths.ChannelA : lengths.ChannelB;
        if (!lengthSymbols.IsComplete && !recoverErasures)
            return Failure(FdmTextReceiveState.InconclusiveLength, "Símbolos '?' en la longitud protegida; no se aplica Hamming.", block: lengthSymbols);
        var hamming = new Hamming74Codec();
        Hamming74DecodeResult WithChannel(Hamming74DecodeResult result) => result with
        { Traces = Array.AsReadOnly(result.Traces.Select(t => t with { Channel = channelA ? "A" : "B" }).ToArray()) };
        var decodedLength = WithChannel(hamming.Decode(lengthSymbols.Bits, recoverErasures));
        diagnostics = decodedLength;
        if (!decodedLength.IsComplete)
            return Failure(FdmTextReceiveState.InconclusiveLength, "Longitud protegida con palabras irrecuperables; CRC no comprobado.", block: lengthSymbols);
        int length = Convert.ToInt32(decodedLength.DataBits, 2);
        if (length < 1 || length > FdmTextFrameBuilder.MaximumUtf8Bytes)
            return Failure(FdmTextReceiveState.InvalidLength, "Longitud decodificada fuera de 1–8 bytes; las inversiones no garantizan su integridad.", length, lengthSymbols);
        int originalBits = 8 + length * 8 + MessageCodec.CrcBytes * 8;
        int protectedBits = originalBits / 4 * 7;
        if (!Fits(samples.Length, rate, start, protectedBits))
            return Failure(FdmTextReceiveState.Truncated, "Captura truncada antes del final del bloque protegido. CRC no comprobado.", length);
        var blocks = decoder.DecodeProtectedMessageBlock(samples, rate, start, protectedBits, token);
        var block = channelA ? blocks.ChannelA : blocks.ChannelB;
        if (!block.IsComplete && !recoverErasures)
            return Failure(FdmTextReceiveState.InconclusiveBits, "Bloque protegido con símbolos '?'; no se inventan bits ni se verifica CRC.", length, block);
        var decoded = WithChannel(hamming.Decode(block.Bits, recoverErasures));
        diagnostics = decoded;
        completeBlock = true;
        if (!decoded.IsComplete)
            return Failure(FdmTextReceiveState.InconclusiveBits, "Palabras Hamming irrecuperables; se conservan '?' y no se comprueba CRC.", length, block);
        // Se reprocesan las mismas muestras. Las dos palabras iniciales se cuentan UNA sola vez.
        if (decoded.DataBits[..8] != decodedLength.DataBits)
            return Failure(FdmTextReceiveState.InvalidLength, "La longitud no coincide entre las dos lecturas.", length, block);
        return ValidateMessage(decoded.DataBits, block, length) with
        {
            UsesHamming = true,
            ErasureRecoveryEnabled = recoverErasures,
            HammingDiagnostics = decoded,
            HammingCoversCompleteBlock = true
        };
    }

    // Interpreta decisiones ya recibidas; no vuelve a demodular ni completa símbolos ausentes.
    internal FdmTextChannelResult InterpretPrefix(BfskSequenceResult block, bool channelA, bool useHamming, bool recoverErasures)
    {
        Hamming74DecodeResult? diagnostics = null;
        FdmTextChannelResult Pending(FdmTextReceiveState state, string reason, int? length = null) =>
            new(state, true, length, null, null, block, reason) { UsesHamming = useHamming,
                ErasureRecoveryEnabled = useHamming && recoverErasures, HammingDiagnostics = diagnostics };
        Hamming74DecodeResult Decode(string bits)
        {
            var decoded = new Hamming74Codec().Decode(bits, recoverErasures);
            return decoded with { Traces = Array.AsReadOnly(decoded.Traces.Select(t => t with { Channel = channelA ? "A" : "B" }).ToArray()) };
        }
        int lengthCount = useHamming ? 14 : 8;
        int completeWords = block.Symbols.Count / 7;
        if (useHamming && completeWords > 0)
        {
            string available = block.Bits[..(completeWords * 7)];
            if (recoverErasures || !available.Contains('?')) diagnostics = Decode(available);
        }
        if (block.Symbols.Count < lengthCount) return Pending(FdmTextReceiveState.Truncated, "Cabecera validada · recibiendo campo de longitud.");
        string lengthBits = block.Bits[..lengthCount];
        if (useHamming)
        {
            if (!recoverErasures && lengthBits.Contains('?')) return Pending(FdmTextReceiveState.InconclusiveLength, "Longitud protegida no concluyente; recuperación desactivada.");
            var decodedLength = Decode(lengthBits);
            diagnostics ??= decodedLength;
            if (!decodedLength.IsComplete) return Pending(FdmTextReceiveState.InconclusiveLength, "Longitud protegida irrecuperable; no se atribuyen campos posteriores.");
            lengthBits = decodedLength.DataBits;
        }
        if (lengthBits.Contains('?')) return Pending(FdmTextReceiveState.InconclusiveLength, "Campo de longitud no concluyente.");
        int length = Convert.ToInt32(lengthBits, 2);
        if (length is < 1 or > FdmTextFrameBuilder.MaximumUtf8Bytes) return Pending(FdmTextReceiveState.InvalidLength, "Longitud fuera de 1–8 bytes.", length);
        int originalCount = 8 + length * 8 + MessageCodec.CrcBytes * 8;
        int required = useHamming ? originalCount / 4 * 7 : originalCount;
        if (block.Symbols.Count < required) return Pending(FdmTextReceiveState.Truncated, $"Longitud disponible · recibiendo bloque: {block.Symbols.Count}/{required} símbolos · CRC no comprobado.", length);
        if (useHamming && (!recoverErasures && block.Bits.Contains('?') || diagnostics?.IsComplete != true))
            return Pending(FdmTextReceiveState.InconclusiveBits, "Bloque protegido no reconstruido completamente; CRC no comprobado.", length) with
            { HammingCoversCompleteBlock = recoverErasures && diagnostics is not null };
        if (!useHamming && !block.IsComplete) return Pending(FdmTextReceiveState.InconclusiveBits, "Bloque con símbolos no concluyentes; CRC no comprobado.", length);
        return ValidateMessage(useHamming ? diagnostics!.DataBits : block.Bits, block, length) with
        { UsesHamming = useHamming, ErasureRecoveryEnabled = useHamming && recoverErasures,
            HammingDiagnostics = diagnostics, HammingCoversCompleteBlock = useHamming };
    }

    private static bool Fits(int count, int rate, double start, int symbols) =>
        Math.Round(start * rate + symbols * FdmSequenceDecoder.SymbolDurationSeconds * rate) <= count;
}
