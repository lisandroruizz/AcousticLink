using AcousticLink.App.Protocol;

namespace AcousticLink.App.Visualization;

// Proyección de solo lectura. No clasifica audio ni participa en la recepción.
public sealed record ReceptionChannelActivity(string Stage, bool HeaderValid, int AvailableBits,
    int ConclusiveBits, int UnknownBits, int? ExpectedBits, bool UsesHamming, int HammingWords,
    int Inversions, int RecoveredErasures, bool? CrcValid, string? ValidatedText, bool IsInvalid)
{
    public int? ByteLength { get; init; }
    public double? Percent => ExpectedBits is > 0 ? Math.Min(100, AvailableBits * 100.0 / ExpectedBits.Value) : null;
    public static ReceptionChannelActivity From(FdmTextChannelResult? channel, bool processing)
    {
        int count = channel?.Block?.Symbols.Count ?? 0;
        bool invalid = channel?.State is FdmTextReceiveState.CrcIncorrect or FdmTextReceiveState.InvalidLength or FdmTextReceiveState.InvalidUtf8;
        int? expected = channel?.ByteLength is int length && length is >= 1 and <= 8
            ? channel.UsesHamming ? (8 + length * 8 + 32) / 4 * 7 : 8 + length * 8 + 32 : null;
        string stage = channel is null ? processing ? "Escuchando" : "Sin analizar"
            : channel.State == FdmTextReceiveState.Valid ? "Mensaje validado"
            : invalid ? "Mensaje inválido"
            : channel.State == FdmTextReceiveState.AmbiguousSynchronization ? "Sincronización ambigua"
            : !channel.HeaderValid ? processing ? "Buscando cabecera" : "Sin cabecera validada"
            : channel.IsReceiving && !processing ? "Procesamiento detenido"
            : !channel.IsReceiving ? "Mensaje incompleto"
            : count == 0 ? "Cabecera validada"
            : channel.ByteLength is null ? "Recibiendo símbolos · longitud pendiente"
            : channel.UsesHamming && channel.HammingDiagnostics?.WordCount > 2 ? "Reconstruyendo datos"
            : "Longitud disponible · recibiendo bloque";
        var h = channel?.HammingDiagnostics;
        return new(stage, channel?.HeaderValid == true, count, channel?.Block?.ConclusiveCount ?? 0,
            channel?.Block?.InconclusiveCount ?? 0, expected, channel?.UsesHamming == true, h?.WordCount ?? 0,
            h?.AppliedBitFlipCount ?? 0, h?.RecoveredErasureCount ?? 0, channel?.CrcValid,
            channel?.State == FdmTextReceiveState.Valid && channel.CrcValid == true ? channel.Text : null, invalid) { ByteLength = channel?.ByteLength };
    }
}

public sealed record ReceptionActivity(string Mode, string Source, int SampleRate, int ReceivedSamples,
    int ProcessedSamples, ReceptionChannelActivity ChannelA, ReceptionChannelActivity ChannelB);
