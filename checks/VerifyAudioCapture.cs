#:property TargetFramework=net10.0-windows
#:project ../AcousticLink.App/AcousticLink.App.csproj

using AcousticLink.App.Audio;
using NAudio.Wave;

// Datos sintéticos: valida buffer/conversión de canales y WAV sin abrir el micrófono.
foreach (int rate in new[] { 48_000, 44_100 })
{
    var recording = new CapturedAudio(rate, 2);
    var stereo = new float[rate * 2];
    for (int i = 0; i < stereo.Length; i += 2) { stereo[i] = 0.6f; stereo[i + 1] = 0.2f; }
    recording.Append(stereo, stereo.Length);
    Require(recording.GetStatus().Count == rate, "Número de muestras mono incorrecto");
    Require(recording.GetSamples().All(x => Math.Abs(x - 0.4f) < 1e-6), "Conversión estéreo incorrecta");
    Require(Math.Abs(recording.GetStatus().Peak - 0.4f) < 1e-6, "Pico incorrecto");
    string path = Path.Combine(Path.GetTempPath(), $"AcousticLink-check-{Guid.NewGuid()}.wav");
    try
    {
        recording.SaveWav(path);
        using var reader = new WaveFileReader(path);
        Require(reader.WaveFormat.SampleRate == rate && reader.WaveFormat.Channels == 1, "Formato WAV incorrecto");
        Require(reader.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat && reader.WaveFormat.BitsPerSample == 32, "Codificación incorrecta");
        Require(reader.Length == rate * 4 && Math.Abs(reader.TotalTime.TotalSeconds - 1) < 1e-6, "Duración WAV incorrecta");
        var source = reader.ToSampleProvider();
        float[] values = new float[rate];
        Require(source.Read(values.AsSpan()) == rate && values.All(x => Math.Abs(x - 0.4f) < 1e-6), "Datos WAV incorrectos");
    }
    finally { File.Delete(path); }
    Parallel.For(0, 20, _ => recording.Append(stereo, stereo.Length));
    Require(recording.GetStatus().Count == rate * 10, "Límite de 10 segundos incorrecto");
    Console.WriteLine($"{rate} Hz: muestras, mono, pico, WAV y límite concurrente de 10 s: OK");
}
await using (var service = new AudioCaptureService())
{
    await service.StopAsync();
    await service.StopAsync();
}
Console.WriteLine("Parada sin sesión y liberación: OK. Inicio/reinicio físicos pendientes de prueba manual.");
static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
