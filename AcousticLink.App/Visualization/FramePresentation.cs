using System.Text;
using AcousticLink.App.Protocol;
using AcousticLink.App.Signals;

namespace AcousticLink.App.Visualization;

public enum FramePart { Header, Length, Payload, Crc }
public sealed record FrameSection(FramePart Part, string Name, int? OriginalStart, int? EncodedStart,
    int? OriginalSize, int? EncodedSize, string Purpose);
public sealed record ValidatedCharacter(string Character, int ByteOffset, IReadOnlyList<byte> Bytes, string Bits);
public sealed record FrameBitPosition(int? EncodedIndex, int? OriginalIndex, int FrameIndex,
    int? WordIndex, int? CodePosition, int? DataPosition, int? Utf8Byte, int? Utf8Bit);

// Solo geometría del protocolo y extracción de diagnósticos existentes. No decodifica audio ni corrige bits.
public static class FramePresentation
{
    // Índices base cero; posiciones dentro de palabras/bytes base uno, MSB primero.
    public static FrameBitPosition Position(bool hamming, bool originalView, bool header, int index, int? byteLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        if (header) return new(null, null, index, null, null, null, null, null);
        int? word = null, codePosition = null, dataPosition = null;
        int? original = index; int encoded = index;
        int[] dataPositions = [3, 5, 6, 7];
        if (hamming)
        {
            word = index / (originalView ? 4 : 7);
            if (originalView)
            {
                dataPosition = index % 4 + 1; codePosition = dataPositions[dataPosition.Value - 1];
                encoded = word.Value * 7 + codePosition.Value - 1;
            }
            else
            {
                codePosition = index % 7 + 1;
                int data = Array.IndexOf(dataPositions, codePosition.Value);
                original = data < 0 ? null : word.Value * 4 + data;
                dataPosition = data < 0 ? null : data + 1;
            }
        }
        bool content = original.HasValue && byteLength is >= 1 and <= FdmTextFrameBuilder.MaximumUtf8Bytes && original >= 8 && original < 8 + byteLength * 8;
        return new(encoded, original, HeaderSize + encoded, word, codePosition, dataPosition,
            content ? (original!.Value - 8) / 8 : null, content ? (original!.Value - 8) % 8 + 1 : null);
    }
    public static int HeaderSize => BfskFrameBuilder.Preamble.Length + BfskFrameBuilder.StartMarker.Length;
    public static IReadOnlyList<FrameSection> Sections(FdmTextChannelResult channel)
    {
        bool known = channel.ByteLength is >= 1 and <= FdmTextFrameBuilder.MaximumUtf8Bytes;
        int? payloadSize = known ? channel.ByteLength!.Value * 8 : null;
        int? Encoded(int? size) => channel.UsesHamming ? size / 4 * 7 : size;
        return Array.AsReadOnly(new[]
        {
            new FrameSection(FramePart.Header, "Cabecera", 0, 0, HeaderSize, HeaderSize,
                $"Permite localizar la trama: preámbulo de {BfskFrameBuilder.Preamble.Length} bits y marcador de {BfskFrameBuilder.StartMarker.Length} bits, sin Hamming."),
            new FrameSection(FramePart.Length, "Longitud", 0, 0, 8, Encoded(8), "Indica la cantidad de bytes UTF-8 del mensaje."),
            new FrameSection(FramePart.Payload, "Datos UTF-8", known ? 8 : null, known ? Encoded(8) : null,
                payloadSize, Encoded(payloadSize), "Contienen el texto transmitido. No incluyen longitud ni CRC."),
            new FrameSection(FramePart.Crc, "CRC-32", known ? 8 + payloadSize : null, known ? Encoded(8 + payloadSize) : null,
                MessageCodec.CrcBytes * 8, Encoded(MessageCodec.CrcBytes * 8), "Verifica la integridad del byte de longitud y los bytes UTF-8.")
        });
    }

    public static IReadOnlyList<ValidatedCharacter> Characters(FdmTextChannelResult channel)
    {
        if (channel.State != FdmTextReceiveState.Valid || channel.CrcValid != true || channel.Text is null ||
            channel.ByteLength is not (>= 1 and <= FdmTextFrameBuilder.MaximumUtf8Bytes)) return Array.Empty<ValidatedCharacter>();
        string? original = channel.UsesHamming ? channel.HammingDiagnostics?.DataBits : channel.Block?.Bits;
        int length = channel.ByteLength.Value;
        if (original is null || original.Length != (1 + length + MessageCodec.CrcBytes) * 8 || original.Any(c => c is not ('0' or '1')))
            return Array.Empty<ValidatedCharacter>();
        var rows = new List<ValidatedCharacter>(); int offset = 0;
        foreach (Rune character in channel.Text.EnumerateRunes())
        {
            int count = character.Utf8SequenceLength;
            if (offset + count > length) return Array.Empty<ValidatedCharacter>();
            var bytes = new byte[count];
            for (int i = 0; i < count; i++) bytes[i] = Convert.ToByte(original.Substring(8 + (offset + i) * 8, 8), 2);
            rows.Add(new(character.ToString(), offset, Array.AsReadOnly(bytes),
                string.Join(" ", Enumerable.Range(0, count).Select(i => original.Substring(8 + (offset + i) * 8, 8)))));
            offset += count;
        }
        return offset == length ? rows.AsReadOnly() : Array.Empty<ValidatedCharacter>();
    }
}
