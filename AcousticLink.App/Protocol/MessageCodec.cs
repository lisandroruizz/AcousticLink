using System.Buffers.Binary;
using System.Text;

namespace AcousticLink.App.Protocol;

// Bloque sin cabecera acústica ni Hamming: longitud (1 byte) + UTF-8 + CRC (4 bytes).
public sealed class MessageCodec
{
    public const int MaximumMessageBytes = 255;
    public const int CrcBytes = 4;
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public byte[] EncodeBytes(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        int length = StrictUtf8.GetByteCount(text);
        if (length < 1 || length > MaximumMessageBytes)
            throw new ArgumentException("El mensaje debe ocupar entre 1 y 255 bytes UTF-8.", nameof(text));
        var block = new byte[1 + length + CrcBytes];
        block[0] = (byte)length;
        StrictUtf8.GetBytes(text.AsSpan(), block.AsSpan(1, length));
        uint crc = Crc32.Compute(block.AsSpan(0, 1 + length));
        BinaryPrimitives.WriteUInt32BigEndian(block.AsSpan(1 + length, CrcBytes), crc);
        return block;
    }

    public string Encode(string text)
    {
        byte[] block = EncodeBytes(text);
        var bits = new char[block.Length * 8];
        for (int i = 0; i < block.Length; i++)
            for (int bit = 0; bit < 8; bit++)
                bits[i * 8 + bit] = (block[i] & (1 << (7 - bit))) != 0 ? '1' : '0';
        return new string(bits);
    }

    public string Decode(string bits)
    {
        ArgumentNullException.ThrowIfNull(bits);
        if (bits.Length < 48 || bits.Length > (1 + MaximumMessageBytes + CrcBytes) * 8 || bits.Length % 8 != 0)
            throw new FormatException("El bloque binario está truncado o tiene un tamaño inválido.");
        var block = new byte[bits.Length / 8];
        for (int i = 0; i < bits.Length; i++)
        {
            if (bits[i] != '0' && bits[i] != '1')
                throw new FormatException("El bloque solo puede contener caracteres 0 y 1.");
            block[i / 8] = (byte)((block[i / 8] << 1) | (bits[i] - '0'));
        }
        return DecodeBytes(block);
    }

    public string DecodeBytes(ReadOnlySpan<byte> block)
    {
        if (block.Length < 1 + 1 + CrcBytes)
            throw new FormatException("El bloque está truncado.");
        int length = block[0];
        if (length == 0 || block.Length != 1 + length + CrcBytes)
            throw new FormatException("La longitud declarada no coincide con el bloque recibido.");
        uint receivedCrc = BinaryPrimitives.ReadUInt32BigEndian(block.Slice(1 + length, CrcBytes));
        uint calculatedCrc = Crc32.Compute(block[..(1 + length)]);
        if (receivedCrc != calculatedCrc)
            throw new FormatException("CRC-32 incorrecto: el mensaje fue alterado.");
        try
        {
            return StrictUtf8.GetString(block.Slice(1, length));
        }
        catch (DecoderFallbackException ex)
        {
            throw new FormatException("Los bytes del mensaje no representan UTF-8 válido.", ex);
        }
    }
}
