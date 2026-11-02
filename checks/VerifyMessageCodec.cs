#:property TargetFramework=net10.0-windows
#:project ../AcousticLink.App/AcousticLink.App.csproj

using System.Buffers.Binary;
using System.Text;
using AcousticLink.App.Protocol;

var codec = new MessageCodec();
Require(Crc32.Compute(Encoding.ASCII.GetBytes("123456789")) == 0xCBF43926, "Vector ISO-HDLC");
Require(Crc32.Compute(Array.Empty<byte>()) == 0, "CRC de entrada vacía");
foreach (string text in new[] { "Hola", "ñ", "áéíóú", "¡Hola, 世界! 🎵", "e\u0301", "línea\n\0fin", new string('a', 255), new string('ñ', 127) + "a" })
{
    byte[] utf8 = new UTF8Encoding(false, true).GetBytes(text);
    byte[] block = codec.EncodeBytes(text);
    string bits = codec.Encode(text);
    Require(block[0] == utf8.Length && block.Length == 1 + utf8.Length + 4, "Longitud UTF-8 en bytes");
    Require(block.AsSpan(1, utf8.Length).SequenceEqual(utf8), "Bytes exactos, sin BOM añadido");
    Require(bits == string.Concat(block.Select(x => Convert.ToString(x, 2).PadLeft(8, '0'))), "Bits MSB-first");
    byte[] parsed = Enumerable.Range(0, bits.Length / 8).Select(i => Convert.ToByte(bits.Substring(i * 8, 8), 2)).ToArray();
    Require(parsed.SequenceEqual(block), "Bits a bytes");
    Require(codec.Decode(bits) == text && codec.DecodeBytes(parsed) == text, "Texto exacto sin normalización");
    uint expected = ReferenceCrc(block[..(1 + utf8.Length)]);
    Require(BinaryPrimitives.ReadUInt32BigEndian(block.AsSpan(1 + utf8.Length)) == expected, "CRC big-endian sobre longitud + texto");
    string expectedCrcBits = Convert.ToString((long)expected, 2).PadLeft(32, '0');
    Require(bits[^32..] == expectedCrcBits, "Orden MSB-first del CRC");
    Console.WriteLine($"UTF-8: {utf8.Length} bytes, {bits.Length} bits, CRC y vuelta exacta: OK");
}
Require(codec.Encode("Hola").StartsWith("0000010001001000011011110110110001100001"), "Vector longitud + ASCII Hola");
Require(codec.EncodeBytes("ñ")[0] == 2, "ñ ocupa dos bytes");
byte[] valid = codec.EncodeBytes("Hola");
// Todos los cambios de un bit del bloque, incluidos longitud, datos y CRC, se rechazan.
for (int i = 0; i < valid.Length; i++)
for (int bit = 0; bit < 8; bit++)
{
    byte[] damaged = (byte[])valid.Clone();
    damaged[i] ^= (byte)(1 << bit);
    ExpectFormat(() => codec.DecodeBytes(damaged));
}
string encoded = codec.Encode("Hola");
char[] corrupted = encoded.ToCharArray();
corrupted[10] = corrupted[10] == '0' ? '1' : '0';
ExpectFormat(() => codec.Decode(new string(corrupted)));
foreach (string invalid in new[] { "", encoded[..^1], encoded[..^8], encoded + "00000000", encoded[..^1] + "?", encoded[..^1] + " " })
    ExpectFormat(() => codec.Decode(invalid));
for (int size = 0; size < valid.Length; size++)
{
    int length = size;
    ExpectFormat(() => codec.DecodeBytes(valid[..length]));
}
ExpectFormat(() => codec.DecodeBytes(valid.Concat(new byte[] { 0 }).ToArray()));
// CRC correcto sobre UTF-8 inválido: asegurar rechazo por decodificación estricta.
foreach (byte[] payload in new[] { new byte[] { 0xFF }, new byte[] { 0xC0, 0xAF }, new byte[] { 0xED, 0xA0, 0x80 }, new byte[] { 0xE2, 0x82 } })
{
    byte[] block = new byte[1 + payload.Length + 4];
    block[0] = (byte)payload.Length;
    payload.CopyTo(block, 1);
    BinaryPrimitives.WriteUInt32BigEndian(block.AsSpan(1 + payload.Length), ReferenceCrc(block[..(1 + payload.Length)]));
    ExpectFormat(() => codec.DecodeBytes(block));
}
foreach (string invalid in new[] { "", new string('a', 256), new string('ñ', 128), "\uD800" })
{
    try { codec.Encode(invalid); }
    catch (ArgumentException) { continue; }
    throw new InvalidOperationException("Se aceptó texto inválido o fuera del límite.");
}
ExpectFormat(() => codec.DecodeBytes(new byte[6]));
Console.WriteLine("Corrupción de cada bit, CRC, longitud, truncación, UTF-8 inválido y límites 1–255: OK");

// Referencia independiente con polinomio normal y registro que avanza hacia la izquierda.
static uint ReferenceCrc(ReadOnlySpan<byte> bytes)
{
    uint crc = 0xFFFFFFFF;
    foreach (byte value in bytes)
        for (int bit = 0; bit < 8; bit++)
        {
            bool feedback = ((crc >> 31) ^ (uint)((value >> bit) & 1)) != 0;
            crc <<= 1;
            if (feedback) crc ^= 0x04C11DB7;
        }
    uint reflected = 0;
    for (int bit = 0; bit < 32; bit++) { reflected = (reflected << 1) | (crc & 1); crc >>= 1; }
    return reflected ^ 0xFFFFFFFF;
}
static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
static void ExpectFormat(Action action)
{
    try { action(); }
    catch (FormatException) { return; }
    throw new InvalidOperationException("Se esperaba rechazo de un bloque alterado.");
}
