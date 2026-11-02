#:property TargetFramework=net10.0-windows
#:project ../AcousticLink.App/AcousticLink.App.csproj

using AcousticLink.App.Signals;

// Verificación numérica sin abrir dispositivos de audio.
var generator = new BfskToneGenerator();
foreach (bool bit in new[] { false, true })
foreach (double duration in new[] { 0.10, 1.0 })
{
    float[] samples = generator.Generate(bit, duration);
    double expectedFrequency = bit ? 1900 : 1200;
    Require(samples.Length == (int)(duration * 48_000), "Duración incorrecta");
    Require(samples.All(float.IsFinite), "Muestras no finitas");
    double peak = samples.Max(x => Math.Abs((double)x));
    Require(Math.Abs(peak - 0.15) < 1e-6, "Amplitud incorrecta");
    Require(samples[0] == 0 && samples[^1] == 0, "Extremos sin silencio");

    // Cruces positivos interpolados; excluir las rampas de amplitud.
    var crossings = new List<double>();
    for (int i = 481; i < samples.Length - 480; i++)
        if (samples[i - 1] <= 0 && samples[i] > 0)
            crossings.Add(i - 1 - samples[i - 1] / (double)(samples[i] - samples[i - 1]));
    double measuredFrequency = (crossings.Count - 1) * 48_000 / (crossings[^1] - crossings[0]);
    Require(Math.Abs(measuredFrequency - expectedFrequency) < 0.1, "Frecuencia incorrecta");
    for (int i = 0; i < 240; i++)
    {
        Require(Math.Abs(samples[i]) <= 0.15 * i / 240 + 1e-7, "Rampa inicial incorrecta");
        Require(Math.Abs(samples[^(i + 1)]) <= 0.15 * i / 240 + 1e-7, "Rampa final incorrecta");
    }
    Console.WriteLine($"Bit {(bit ? 1 : 0)}: {samples.Length} muestras, {duration:F2} s, {measuredFrequency:F3} Hz, pico {peak:F6}: OK");
}
Require(generator.Generate(false).Length == 4800, "Duración predeterminada incorrecta");
Console.WriteLine("Todas las verificaciones pasaron.");

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
