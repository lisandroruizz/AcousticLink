#:property TargetFramework=net10.0-windows
#:project ../AcousticLink.App/AcousticLink.App.csproj

using AcousticLink.App.Signals;

var mixer = new FdmToneMixer();
foreach (bool a in new[] { false, true })
foreach (bool b in new[] { false, true })
{
    float[] samples = mixer.Generate(a, b);
    Require(samples.Length == 48000, "Un segundo a 48 kHz");
    Require(samples.All(float.IsFinite), "Muestras finitas");
    Require(samples.All(x => Math.Abs(x) <= 0.15 + 1e-8 && Math.Abs(x) < 1), "Amplitud segura sin saturación");
    Require(samples[0] == 0 && samples[^1] == 0, "Extremos en cero");
    double fa = a ? 1900 : 1200;
    double fb = b ? 3700 : 3000;
    // Referencia analítica independiente: suma continua de senoides con una envolvente común.
    for (int i = 0; i < samples.Length; i++)
    {
        double envelope = Math.Min(1, Math.Min(i, samples.Length - 1 - i) / 240.0);
        double expected = 0.075 * envelope * (Math.Sin(2 * Math.PI * fa * i / 48000) + Math.Sin(2 * Math.PI * fb * i / 48000));
        Require(Math.Abs(samples[i] - expected) < 1e-7, "Suma sin recorte, fases y rampas");
    }
    // Proyección directa solo en el tramo estable (0,1 a 0,9 s), sin modificar el receptor.
    foreach (double frequency in new[] { 1200.0, 1900, 3000, 3700 })
    {
        double real = 0, imaginary = 0;
        for (int i = 4800; i < 43200; i++)
        {
            real += samples[i] * Math.Cos(2 * Math.PI * frequency * i / 48000);
            imaginary += samples[i] * Math.Sin(2 * Math.PI * frequency * i / 48000);
        }
        double amplitude = 2 * Math.Sqrt(real * real + imaginary * imaginary) / 38400;
        double expected = frequency == fa || frequency == fb ? 0.075 : 0;
        Require(Math.Abs(amplitude - expected) < 1e-6, "Dos frecuencias presentes y tonos no seleccionados ausentes");
    }
    foreach (double duration in new[] { 0.1, 0.237 })
    {
        var shorter = mixer.Generate(a, b, duration, 0.1);
        Require(shorter.Length == (int)Math.Round(duration * 48000), "Duración configurable");
        Require(shorter.All(x => float.IsFinite(x) && Math.Abs(x) <= 0.1 + 1e-8), "Amplitud configurable");
    }
    Console.WriteLine($"A={(a ? 1 : 0)}, B={(b ? 1 : 0)}: {fa}/{fb} Hz, 48000 muestras, amplitud, fases y rampas: OK");
}
foreach (double invalid in new[] { 0, -1, double.NaN, double.PositiveInfinity })
    ExpectInvalid(() => mixer.Generate(false, false, invalid));
foreach (double invalid in new[] { -0.01, 0.16, double.NaN, double.PositiveInfinity })
    ExpectInvalid(() => mixer.Generate(false, false, amplitude: invalid));
Require(mixer.Generate(false, false, amplitude: 0).All(x => x == 0), "Amplitud cero");
Console.WriteLine("Duración/amplitud configurables y validaciones: OK");

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
static void ExpectInvalid(Action action)
{
    try { action(); }
    catch (ArgumentException) { return; }
    throw new InvalidOperationException("Se esperaba rechazo de parámetros inválidos.");
}
