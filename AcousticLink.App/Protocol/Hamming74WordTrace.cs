namespace AcousticLink.App.Protocol;

public enum Hamming74TraceState { Unchanged, BitFlipped, ErasuresRecovered, Unrecoverable, Incomplete }

// Diagnóstico inmutable: posiciones 1–7, índice/offset base cero dentro del bloque SIN cabecera.
// No entra en la toma de decisiones ni equivale a una garantía de integridad.
public sealed record Hamming74WordTrace(int WordIndex, string ReceivedBits, string? ResultBits,
    string? DataBits, int? Syndrome, int? FlippedPosition, IReadOnlyList<int> RecoveredPositions,
    Hamming74TraceState State, string Reason)
{
    public string? Channel { get; init; }
    public int EncodedBitOffset => WordIndex * 7;
}
