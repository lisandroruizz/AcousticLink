#:property TargetFramework=net10.0-windows
#:project ../AcousticLink.App/AcousticLink.App.csproj

using AcousticLink.App.Signals;

var detector = new BfskToneDetector();
foreach (int rate in new[] { 48_000, 44_100 })
{
    foreach (int bit in new[] { 0, 1 })
    {
        double frequency = bit == 0 ? 1200 : 1900;
        float[] tone = Create(rate, frequency, 0.15, 0);
        var result = detector.Analyze(tone, rate);
        Require(result.Bit == bit, "Clasificación de tono puro");
        double expectedPower = 0.15 * 0.15 / 2;
        double measuredPower = bit == 0 ? result.Energy0 : result.Energy1;
        Require(Math.Abs(measuredPower - expectedPower) < 1e-7, "Normalización de energía");
        // Comparación independiente con proyección seno/coseno directa.
        double real = 0, imaginary = 0;
        for (int i = 0; i < tone.Length; i++)
        {
            real += tone[i] * Math.Cos(2 * Math.PI * frequency * i / rate);
            imaginary += tone[i] * Math.Sin(2 * Math.PI * frequency * i / rate);
        }
        Require(Math.Abs(measuredPower - 2 * (real * real + imaginary * imaginary) / tone.Length / tone.Length) < 1e-9,
            "Goertzel difiere de la proyección directa");
        Require(detector.Analyze(Create(rate, frequency, 0.15, 0.08), rate).Bit == bit, "Tono con ruido moderado");
        Require(detector.Analyze(Create(rate, frequency, 0.0001, 0), rate).Bit is null, "Tono demasiado débil");
        Require(double.IsFinite(result.Energy0) && result.Energy0 >= 0 && double.IsFinite(result.Energy1) && result.Energy1 >= 0,
            "Energías finitas y no negativas");
    }
    Require(detector.Analyze(new float[rate / 10], rate).Bit is null, "Silencio");
    Require(detector.Analyze(new float[rate / 100], rate).Reason.Contains("insuficiente"), "Fragmento corto");
    Require(detector.Analyze(Array.Empty<float>(), rate).Bit is null, "Ventana vacía");
    var mixture = Create(rate, 1200, 0.15, 0);
    var other = Create(rate, 1900, 0.15, 0);
    for (int i = 0; i < mixture.Length; i++) mixture[i] += other[i];
    Require(detector.Analyze(mixture, rate).Reason.Contains("Ambigua"), "Mezcla de ambos tonos");
    Require(detector.Analyze(Create(rate, 0, 0, 0.001), rate).Bit is null, "Ruido débil");
    Require(detector.Analyze(Create(rate, 0, 0, 0.08), rate).Bit is null, "Ruido sin tono");
    Require(detector.Analyze(Create(rate, 1500, 0.15, 0), rate).Bit is null, "Frecuencia ajena");
    Console.WriteLine($"{rate} Hz: tonos 0/1, ruido, silencio, debilidad, mezcla, ventana corta y energía: OK");
}
ExpectInvalid(() => detector.Analyze(new float[4800], 0));
ExpectInvalid(() => detector.Analyze(new float[4800], 3800));
ExpectInvalid(() => detector.Analyze(new[] { float.NaN }, 48_000));
ExpectInvalid(() => detector.Analyze(new[] { float.PositiveInfinity }, 48_000));
ExpectInvalid(() => new BfskToneDetector(minimumEnergyRatio: 1));
// Integración numérica con el generador existente, sin audio físico.
var generator = new BfskToneGenerator();
Require(detector.Analyze(generator.Generate(false), 48_000).Bit == 0, "Integración generador bit 0");
Require(detector.Analyze(generator.Generate(true), 48_000).Bit == 1, "Integración generador bit 1");
Console.WriteLine("Parámetros inválidos e integración con generador: OK");

static float[] Create(int rate, double frequency, double amplitude, double noise)
{
    var random = new Random(1234);
    return Enumerable.Range(0, rate / 10).Select(i =>
        (float)(amplitude * Math.Sin(2 * Math.PI * frequency * i / rate + 0.37) + noise * (2 * random.NextDouble() - 1))).ToArray();
}
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
