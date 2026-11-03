using AcousticLink.App.Protocol;

namespace AcousticLink.App.Visualization;

// Adaptador didáctico de solo lectura: no toma decisiones de decodificación.
public static class HammingTeaching
{
    private static readonly IReadOnlyList<int> P1 = Array.AsReadOnly(new[] { 1, 3, 5, 7 });
    private static readonly IReadOnlyList<int> P2 = Array.AsReadOnly(new[] { 2, 3, 6, 7 });
    private static readonly IReadOnlyList<int> P4 = Array.AsReadOnly(new[] { 4, 5, 6, 7 });
    public static IReadOnlyList<int> Coverage(int parity) => parity switch
    { 1 => P1, 2 => P2, 4 => P4, _ => Array.Empty<int>() };

    public static FrameSection? Section(FdmTextChannelResult reception, Hamming74WordTrace trace) =>
        FramePresentation.Sections(reception).FirstOrDefault(s => s.Part != FramePart.Header && s.EncodedStart.HasValue &&
            trace.EncodedBitOffset >= s.EncodedStart && trace.EncodedBitOffset < s.EncodedStart + s.EncodedSize);

    public static string Association(FdmTextChannelResult reception, Hamming74WordTrace trace)
    {
        var section = Section(reception, trace);
        if (section?.Part != FramePart.Payload) return section?.Name ?? "Sección no disponible";
        int byteIndex = (trace.WordIndex * 4 - 8) / 8;
        var character = FramePresentation.Characters(reception).FirstOrDefault(c => byteIndex >= c.ByteOffset && byteIndex < c.ByteOffset + c.Bytes.Count);
        return character is null ? $"Datos UTF-8 · byte {byteIndex + 1} según longitud leída; valor validado no disponible"
            : $"Datos UTF-8 · byte {byteIndex + 1}: 0x{character.Bytes[byteIndex - character.ByteOffset]:X2} · carácter {character.Character}";
    }

    public static string Explain(Hamming74WordTrace t) => t.State switch
    {
        Hamming74TraceState.Unchanged => "Síndrome cero: no se modificó la palabra. No garantiza ausencia de errores múltiples.",
        Hamming74TraceState.BitFlipped => $"El síndrome {t.Syndrome} señaló la posición {t.FlippedPosition}; se invirtió ese bit. El CRC debe comprobar el mensaje.",
        Hamming74TraceState.ErasuresRecovered => $"Había {t.ReceivedBits.Count(c => c == '?')} borrados. Solo una de las 16 palabras válidas coincidió con todos los bits conocidos; se recuperaron las posiciones {string.Join(", ", t.RecoveredPositions)}.",
        _ => t.Reason
    };
}
