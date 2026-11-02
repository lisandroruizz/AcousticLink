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
    await Task.WhenAll(service.StopAsync(), service.StopAsync(), service.StopAsync());
    foreach (int invalid in new[] { -1, 0, 4, 31 })
    {
        try { await service.StartAsync(invalid); }
        catch (ArgumentOutOfRangeException) { continue; }
        throw new InvalidOperationException("Duración inválida aceptada por el servicio.");
    }
    await service.DisposeAsync();
    await service.DisposeAsync();
}
foreach (int rate in new[] { 48000, 44100 })
{
    Require(new CapturedAudio(rate, 1).MaximumDurationSeconds == 10, "Valor predeterminado de 10 s");
    foreach (int seconds in new[] { 5, 10, 20, 30 })
    {
        var bounded = new CapturedAudio(rate, 1, seconds);
        var chunk = Enumerable.Repeat(0.25f, rate).ToArray();
        for (int i = 0; i < seconds + 1; i++) bounded.Append(chunk, chunk.Length);
        var metrics = bounded.GetMetrics();
        Require(metrics.Count == rate * seconds && metrics.DurationSeconds == seconds, "Límite de duración/muestras");
        Require(metrics.StopReason == CaptureStopReason.DurationLimit, "Finalización automática por capacidad");
        Require(Math.Abs(metrics.Rms - 0.25) < 1e-7 && metrics.Peak == 0.25f, "RMS/pico");
        bounded.Complete(CaptureStopReason.Manual);
        Require(bounded.GetMetrics().StopReason == CaptureStopReason.DurationLimit, "Primera causa de finalización conservada");
    }
    foreach (int invalid in new[] { -1, 0, 4, 31 })
    {
        try { _ = new CapturedAudio(rate, 1, invalid); }
        catch (ArgumentOutOfRangeException) { continue; }
        throw new InvalidOperationException("Duración inválida aceptada por el buffer.");
    }
    var manual = new CapturedAudio(rate, 1, 30);
    var second = Enumerable.Range(0, rate).Select(i => i % 2 == 0 ? 0.25f : -0.25f).ToArray();
    for (int i = 0; i < 12; i++) manual.Append(second, second.Length);
    manual.Complete(CaptureStopReason.Manual);
    manual.Complete(CaptureStopReason.ApplicationClosing);
    manual.Append(second, second.Length);
    Require(manual.GetStatus().Count == rate * 12 && manual.GetMetrics().StopReason == CaptureStopReason.Manual,
        "Parada manual antes del máximo conserva las muestras y es idempotente");
    string longPath = Path.Combine(Path.GetTempPath(), $"AcousticLink-long-check-{Guid.NewGuid()}.wav");
    try
    {
        manual.SaveWav(longPath);
        using var reader = new WaveFileReader(longPath);
        Require(reader.WaveFormat.SampleRate == rate && reader.WaveFormat.Channels == 1 && reader.WaveFormat.BitsPerSample == 32, "Formato WAV largo");
        Require(reader.TotalTime.TotalSeconds == 12 && reader.Length == (long)rate * 12 * 4, "Duración/tamaño WAV >10 s");
        var source = reader.ToSampleProvider();
        var readBack = new float[rate * 12];
        Require(source.Read(readBack.AsSpan()) == readBack.Length && readBack.SequenceEqual(manual.GetSamples()), "Conservación exacta en WAV largo");
    }
    finally { File.Delete(longPath); }
    var closed = new CapturedAudio(rate, 1, 20);
    closed.Append(second, second.Length);
    closed.Complete(CaptureStopReason.ApplicationClosing);
    closed.Append(second, second.Length);
    Require(closed.GetStatus().Count == rate, "Cancelación sella el buffer");
    Console.WriteLine($"{rate} Hz: rangos 5–30 s, límites, parada manual simulada, cancelación, RMS y WAV de 12 s: OK");
}
Console.WriteLine("Parada sin sesión y liberación: OK. Inicio/reinicio físicos pendientes de prueba manual.");
static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
