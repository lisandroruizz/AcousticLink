#:property TargetFramework=net10.0-windows
#:project ../AcousticLink.App/AcousticLink.App.csproj

using AcousticLink.App.Audio;
using AcousticLink.App.Protocol;
using AcousticLink.App.Signals;
using NAudio.Wave;

string directory = Path.Combine(Path.GetTempPath(), "AcousticLink-import-" + Guid.NewGuid());
Directory.CreateDirectory(directory);
var selection = new AudioRecordingSelection();
try
{
    foreach (int rate in new[] { 48000, 44100 })
    {
        var captured = new CapturedAudio(rate, 1, 30);
        float[] samples = Enumerable.Range(0, rate * 12).Select(i => (float)(0.25 * Math.Sin(2 * Math.PI * 1200 * i / rate))).ToArray();
        samples[0] = 1.25f; // No normalizar ni recortar valores finitos, incluso fuera de [-1,1].
        captured.Append(samples, samples.Length);
        captured.Complete(CaptureStopReason.Manual);
        string path = Path.Combine(directory, $"{rate}.wav");
        captured.SaveWav(path);
        selection.ImportWav(path);
        var imported = selection.Current!.Audio;
        Require(imported.SampleRate == rate && imported.GetMetrics().DurationSeconds == 12, "Tasa y duración exactas");
        Require(samples.Select(BitConverter.SingleToInt32Bits).SequenceEqual(imported.GetSamples().Select(BitConverter.SingleToInt32Bits)), "Muestras idénticas bit a bit");
        Require(imported.GetMetrics().Rms == captured.GetMetrics().Rms && imported.GetMetrics().Peak == captured.GetMetrics().Peak, "RMS y pico exactos");
        Console.WriteLine($"{rate} Hz: 12 segundos, muestras exactas, RMS/pico y formato: OK");
    }
    string valid = Path.Combine(directory, "48000.wav");
    byte[] validBytes = File.ReadAllBytes(valid);
    string empty = Write("empty.wav", Array.Empty<byte>());
    string truncated = Write("truncated.wav", validBytes[..^4]);
    string invalid = Write("invalid.wav", new byte[44]);
    foreach (string bad in new[] { empty, truncated, invalid }) Reject(bad);
    string emptyWav = Path.Combine(directory, "empty-data.wav");
    using (var writer = new WaveFileWriter(emptyWav, WaveFormat.CreateIeeeFloatWaveFormat(48000, 1))) { }
    Reject(emptyWav);
    foreach (var (name, format) in new[] { ("pcm.wav", new WaveFormat(48000, 16, 1)),
        ("stereo.wav", WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)) })
    {
        string path = Path.Combine(directory, name);
        using (var writer = new WaveFileWriter(path, format)) writer.Write(new byte[48000 * format.BlockAlign], 0, 48000 * format.BlockAlign);
        Reject(path);
    }
    foreach (float invalidValue in new[] { float.NaN, float.PositiveInfinity })
    {
        string path = Path.Combine(directory, $"nonfinite-{Guid.NewGuid()}.wav");
        using (var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(48000, 1))) writer.WriteSample(invalidValue);
        Reject(path);
    }
    string tooLong = Path.Combine(directory, "31-seconds.wav");
    using (var writer = new WaveFileWriter(tooLong, WaveFormat.CreateIeeeFloatWaveFormat(48000, 1)))
    {
        var second = new float[48000];
        for (int i = 0; i < 31; i++) writer.WriteSamples(second, 0, second.Length);
    }
    Reject(tooLong);
    var previous = selection.Current;
    using (var cancellation = new CancellationTokenSource())
    {
        cancellation.Cancel();
        try { selection.ImportWav(valid, cancellation.Token); throw new InvalidOperationException("Importación sin cancelar"); }
        catch (OperationCanceledException) { }
        Require(ReferenceEquals(previous, selection.Current), "Cancelación conserva selección");
    }
    float[] bits = new FdmBitstreamGenerator().Generate("10110010", "01001101");
    string bitPath = Export("fdm.wav", bits);
    selection.ImportWav(bitPath);
    var decoded = new FdmSequenceDecoder().Decode(selection.Current!.Audio.GetSamples(), 48000, 0, 8);
    Require(decoded.ChannelA.Bits == "10110010" && decoded.ChannelB.Bits == "01001101", "Decodificación FDM importada");
    foreach (bool hamming in new[] { false, true })
    {
        float[] audio = new FdmBitstreamGenerator().GenerateTextFrames("Hola", "Mundo", hamming);
        var receiver = new FdmTextReceiver();
        var direct = receiver.Receive(audio, 48000, useHamming: hamming);
        selection.ImportWav(Export($"text-{hamming}.wav", audio));
        var loaded = receiver.Receive(selection.Current!.Audio.GetSamples(), selection.Current.Audio.SampleRate, useHamming: hamming);
        Require(loaded.ChannelA.Text == "Hola" && loaded.ChannelB.Text == "Mundo" && loaded.ChannelA.CrcValid == true && loaded.ChannelB.CrcValid == true, "Mensajes y CRC desde WAV");
        Require(direct.Synchronization.StartSeconds == loaded.Synchronization.StartSeconds && direct.ChannelA.Block!.Bits == loaded.ChannelA.Block!.Bits &&
            direct.ChannelB.Block!.Bits == loaded.ChannelB.Block!.Bits, "Análisis directo/importado equivalentes");
        Require(direct.ChannelA.HammingDiagnostics?.AppliedBitFlipCount == loaded.ChannelA.HammingDiagnostics?.AppliedBitFlipCount, "Diagnóstico Hamming conservado");
    }
    var newCapture = new CapturedAudio(44100, 1);
    selection.SelectMicrophone(newCapture, "Micrófono de prueba");
    Require(ReferenceEquals(selection.Current!.Audio, newCapture) && selection.Current.Source == "Micrófono", "Volver al origen micrófono");
    Console.WriteLine("Rechazos transaccionales, cancelación, FDM, texto con/sin Hamming y selección de micrófono: OK");
}
finally
{
    foreach (string path in Directory.EnumerateFiles(directory)) File.Delete(path);
    Directory.Delete(directory);
}

string Write(string name, byte[] bytes)
{
    string path = Path.Combine(directory, name);
    File.WriteAllBytes(path, bytes);
    return path;
}
void Reject(string path)
{
    var previous = selection.Current;
    try { selection.ImportWav(path); }
    catch (Exception ex) when (ex is IOException or InvalidDataException)
    {
        Require(ReferenceEquals(previous, selection.Current), "Importación fallida conserva audio anterior");
        return;
    }
    throw new InvalidOperationException("Se aceptó WAV inválido: " + path);
}
string Export(string name, float[] samples)
{
    var captured = new CapturedAudio(48000, 1, 30);
    captured.Append(samples, samples.Length);
    captured.Complete(CaptureStopReason.Manual);
    string path = Path.Combine(directory, name);
    captured.SaveWav(path);
    return path;
}
static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
