namespace AcousticLink.App.Protocol;

public static class Crc32
{
    // CRC-32/ISO-HDLC: entrada/salida reflejadas, init y xorout = FFFFFFFF.
    public static uint Compute(ReadOnlySpan<byte> bytes)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        }
        return crc ^ 0xFFFFFFFF;
    }
}
