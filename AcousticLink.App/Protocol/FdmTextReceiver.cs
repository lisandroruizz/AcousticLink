using AcousticLink.App.Signals;

namespace AcousticLink.App.Protocol;

public enum FdmTextReceiveState
{
    Valid, HeaderNotSynchronized, AmbiguousSynchronization, InvalidLength, InconclusiveLength,
    Truncated, InconclusiveBits, CrcIncorrect, InvalidUtf8
}

public sealed record FdmTextChannelResult(FdmTextReceiveState State, bool HeaderValid, int? ByteLength,
    bool? CrcValid, string? Text, BfskSequenceResult? Block, string Reason);

public sealed record FdmTextReceiveResult(FdmSynchronizationResult Synchronization,
    FdmTextChannelResult ChannelA, FdmTextChannelResult ChannelB);

public sealed class FdmTextReceiver
{
    private readonly FdmFrameSynchronizer synchronizer = new();
    private readonly FdmSequenceDecoder decoder = new();
    private readonly MessageCodec codec = new();

    public FdmTextReceiveResult Receive(ReadOnlySpan<float> samples, int sampleRate, CancellationToken token = default)
    {
        var sync = synchronizer.FindHeaders(samples, sampleRate, token);
        if (!sync.DataStartSeconds.HasValue)
        {
            var state = sync.IsAmbiguous ? FdmTextReceiveState.AmbiguousSynchronization
                : samples.Length / (double)sampleRate < FdmBitstreamGenerator.HeaderBits * FdmSequenceDecoder.SymbolDurationSeconds
                    ? FdmTextReceiveState.Truncated : FdmTextReceiveState.HeaderNotSynchronized;
            var failed = new FdmTextChannelResult(state, false, null, null, null, null, sync.Reason);
            return new(sync, failed, failed);
        }
        var a = ReadChannel(samples, sampleRate, sync.DataStartSeconds.Value, sync.ChannelA!.HeaderValid, true, token);
        var b = ReadChannel(samples, sampleRate, sync.DataStartSeconds.Value, sync.ChannelB!.HeaderValid, false, token);
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
        try
        {
            string text = codec.Decode(block.Bits);
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

    private static bool Fits(int count, int rate, double start, int symbols) =>
        Math.Round(start * rate + symbols * FdmSequenceDecoder.SymbolDurationSeconds * rate) <= count;
}
