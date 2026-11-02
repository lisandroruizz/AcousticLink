using System.Buffers.Binary;
using System.IO;
using NAudio.Wave;

namespace AcousticLink.App.Audio;

public sealed record SelectedAudioRecording(CapturedAudio Audio, string Source, string FormatDescription);

// Una única selección compartida por todos los análisis. Se reemplaza solo tras validar el WAV entero.
public sealed class AudioRecordingSelection
{
    private SelectedAudioRecording? current;
    public SelectedAudioRecording? Current => Volatile.Read(ref current);

    public void SelectMicrophone(CapturedAudio audio, string formatDescription) =>
        Volatile.Write(ref current, new(audio, "Micrófono", formatDescription));

    public void ImportWav(string path, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        long dataBytes = ValidateRiff(file);
        file.Position = 0;
        using var reader = new WaveFileReader(file);
        var format = reader.WaveFormat;
        if (format.Encoding != WaveFormatEncoding.IeeeFloat || format.BitsPerSample != 32 || format.Channels != 1)
            throw new InvalidDataException("Se requiere WAV mono IEEE float32, como el exportado por AcousticLink.");
        if (format.SampleRate < 8000 || format.SampleRate > 192000 || format.BlockAlign != 4 ||
            format.AverageBytesPerSecond != (long)format.SampleRate * 4)
            throw new InvalidDataException("Formato WAV incoherente o tasa fuera del rango admitido de 8–192 kHz.");
        if (dataBytes == 0 || dataBytes % 4 != 0 || reader.Length != dataBytes)
            throw new InvalidDataException("El WAV no contiene una cantidad válida de muestras float32 completas.");
        if (dataBytes / 4 > (long)format.SampleRate * CapturedAudio.MaximumSeconds)
            throw new InvalidDataException("La grabación supera 30 segundos; no se importará parcialmente.");
        var samples = new float[checked((int)(dataBytes / 4))];
        var bytes = new byte[16384];
        int position = 0;
        while (position < samples.Length)
        {
            token.ThrowIfCancellationRequested();
            int needed = Math.Min(bytes.Length, (samples.Length - position) * 4);
            int read = 0;
            while (read < needed)
            {
                int received = reader.Read(bytes, read, needed - read);
                if (received == 0) throw new InvalidDataException("El WAV está truncado durante la lectura de muestras.");
                read += received;
            }
            for (int i = 0; i < needed; i += 4)
            {
                float value = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(i, 4)));
                if (!float.IsFinite(value)) throw new InvalidDataException("El WAV contiene muestras NaN o infinitas.");
                samples[position++] = value;
            }
        }
        var audio = new CapturedAudio(samples, format.SampleRate);
        token.ThrowIfCancellationRequested();
        Volatile.Write(ref current, new(audio, $"WAV importado: {Path.GetFullPath(path)}",
            $"WAV mono IEEE float32 · {format.SampleRate} Hz · sin conversión ni remuestreo"));
    }

    private static long ValidateRiff(Stream stream)
    {
        if (stream.Length < 12) throw new InvalidDataException("Archivo vacío o cabecera WAV truncada.");
        using var reader = new BinaryReader(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        if (reader.ReadUInt32() != 0x46464952) throw new InvalidDataException("Se requiere un archivo WAV RIFF.");
        long end = reader.ReadUInt32() + 8L;
        if (end != stream.Length || reader.ReadUInt32() != 0x45564157)
            throw new InvalidDataException("Cabecera RIFF/WAVE inválida o archivo truncado.");
        int formats = 0, dataChunks = 0;
        long dataBytes = 0;
        while (stream.Position < end)
        {
            if (end - stream.Position < 8) throw new InvalidDataException("Cabecera de bloque WAV truncada.");
            uint id = reader.ReadUInt32();
            long length = reader.ReadUInt32();
            long next = stream.Position + length + (length & 1);
            if (next > end) throw new InvalidDataException("Un bloque WAV declara más bytes que los disponibles.");
            if (id == 0x20746D66) formats++;
            if (id == 0x61746164) { dataChunks++; dataBytes = length; }
            stream.Position = next;
        }
        if (formats != 1 || dataChunks != 1) throw new InvalidDataException("Se requiere un único bloque fmt y un único bloque data.");
        return dataBytes;
    }
}
