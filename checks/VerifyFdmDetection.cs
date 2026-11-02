#:property TargetFramework=net10.0-windows
#:project ../AcousticLink.App/AcousticLink.App.csproj

using AcousticLink.App.Signals;

var detector = new FdmToneDetector();
var mixer = new FdmToneMixer();
foreach (int rate in new[] { 48000, 44100 })
foreach (double duration in new[] { 0.1, 0.4 })
{
    foreach (bool a in new[] { false, true })
    foreach (bool b in new[] { false, true })
    {
        // A 48 kHz usar exactamente la salida del mezclador; a 44,1 kHz síntesis equivalente.
        float[] samples = rate == 48000 ? mixer.Generate(a, b, duration)
            : Create(rate, duration, (a ? 1900 : 1200, 0.075), (b ? 3700 : 3000, 0.075));
        Require(samples.All(x => float.IsFinite(x) && Math.Abs(x) <= 0.15 + 1e-8), "Amplitudes válidas");
        Check(detector.Analyze(samples, rate), a ? 1 : 0, b ? 1 : 0);
        var random = new Random(123);
        for (int i = 0; i < samples.Length; i++) samples[i] += (float)(0.04 * (2 * random.NextDouble() - 1));
        Check(detector.Analyze(samples, rate), a ? 1 : 0, b ? 1 : 0);
    }
    Check(detector.Analyze(new float[(int)(rate * duration)], rate), null, null);
    foreach (var (frequency, a, b) in new (double, int?, int?)[] { (1200, 0, null), (1900, 1, null), (3000, null, 0), (3700, null, 1) })
        Check(detector.Analyze(Create(rate, duration, (frequency, 0.075)), rate), a, b);
    Check(detector.Analyze(Create(rate, duration, (1200, 0.0001), (3700, 0.0001)), rate), null, null);
    Check(detector.Analyze(Create(rate, duration, (1200, 0.04), (1900, 0.04), (3000, 0.06)), rate), null, 0);
    Check(detector.Analyze(Create(rate, duration, (1900, 0.06), (3000, 0.04), (3700, 0.04)), rate), 1, null);
    Check(detector.Analyze(Create(rate, duration, (1200, 0.1), (3700, 0.01)), rate), 0, 1);
    Check(detector.Analyze(Create(rate, duration, (1200, 0.01), (3700, 0.1)), rate), 0, 1);
    Check(detector.Analyze(Create(rate, duration, (1500, 0.075)), rate), null, null);
    Check(detector.Analyze(new float[rate / 100], rate), null, null);
    Check(detector.Analyze(Array.Empty<float>(), rate), null, null);
    Console.WriteLine($"{rate} Hz / {duration:0.0} s: 00/01/10/11, ruido, canal ausente, debilidad, ambigüedad y bandas desbalanceadas: OK");
}
ExpectInvalid(() => detector.Analyze(new float[4800], 7400));
ExpectInvalid(() => detector.Analyze(new[] { float.NaN }, 48000));
ExpectInvalid(() => detector.Analyze(new[] { float.NegativeInfinity }, 48000));
ExpectInvalid(() => new FdmToneDetector(minimumToneEnergy: 0));
ExpectInvalid(() => new FdmToneDetector(minimumEnergyRatio: 1));
var strict = new FdmToneDetector(minimumToneEnergy: 0.01);
Check(strict.Analyze(mixer.Generate(false, true, 0.1), 48000), null, null);
Console.WriteLine("Validación y umbrales configurables: OK");

static float[] Create(int rate, double duration, params (double Frequency, double Amplitude)[] tones)
{
    int count = (int)Math.Round(rate * duration);
    var samples = new float[count];
    for (int i = 0; i < count; i++)
    {
        double envelope = Math.Min(1, Math.Min(i, count - 1 - i) / (rate * 0.005));
        samples[i] = (float)(envelope * tones.Sum(t => t.Amplitude * Math.Sin(2 * Math.PI * t.Frequency * i / rate)));
    }
    return samples;
}
static void Check(FdmDetectionResult result, int? a, int? b)
{
    Require(result.ChannelA.Bit == a && result.ChannelB.Bit == b, $"Esperado {a}/{b}, obtenido {result.ChannelA.Bit}/{result.ChannelB.Bit}");
    foreach (double energy in new[] { result.ChannelA.Energy0, result.ChannelA.Energy1, result.ChannelB.Energy0, result.ChannelB.Energy1 })
        Require(double.IsFinite(energy) && energy >= 0, "Energía finita no negativa");
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
