#:property TargetFramework=net10.0-windows
#:project ../AcousticLink.App/AcousticLink.App.csproj

using AcousticLink.App.Signals;

var decoder = new FdmSequenceDecoder();
foreach (int rate in new[] { 48000, 44100 })
{
    foreach (var (a, b) in new[] { ("10110010", "01001101"), ("00000000", "00000000"),
        ("11111111", "11111111"), ("00000000", "11111111"), ("11111111", "00000000"), ("10101010", "01010101") })
    foreach (double noise in new[] { 0, 0.04 })
    {
        const double start = 0.137;
        float[] audio = Create(a, b, rate, start, noise);
        var result = decoder.Decode(audio, rate, start, a.Length);
        Require(result.ChannelA.Bits == a && result.ChannelB.Bits == b, "Recuperación de ambas secuencias");
        Require(result.ChannelA.IsComplete && result.ChannelB.IsComplete, "Recuperación completa");
        Require(result.ChannelA.ConclusiveCount == 8 && result.ChannelB.InconclusiveCount == 0, "Conteos");
        foreach (var symbol in result.ChannelA.Symbols)
            Require(symbol.AnalysisSampleCount == rate * 0.06, "Ventana central de 60 ms");
        CheckFinite(result);
    }
    foreach (bool damageB in new[] { false, true })
    {
        var result = decoder.Decode(Create("10110010", "01001101", rate, 0.137, 0, damageB ? 1 : 0), rate, 0.137, 8);
        var good = damageB ? result.ChannelA : result.ChannelB;
        var damaged = damageB ? result.ChannelB : result.ChannelA;
        Require(good.Bits == (damageB ? "10110010" : "01001101") && good.IsComplete, "Canal independiente intacto");
        Require(damaged.Bits == (damageB ? "0??01101" : "1??10010"), "Posiciones '?' preservadas");
        Require(!damaged.IsComplete && damaged.ConclusiveCount == 6 && damaged.InconclusiveCount == 2, "Estado parcial");
        CheckFinite(result);
    }
    var silence = decoder.Decode(new float[rate], rate, 0, 8);
    Require(silence.ChannelA.Bits == "????????" && silence.ChannelB.Bits == "????????", "Silencio");
    Console.WriteLine($"{rate} Hz: ejemplo, ceros/unos/alternados, silencio previo/posterior, ruido e independencia ante símbolos inválidos: OK");
}
// Tasa con pasos fraccionarios por símbolo: comparar los 16 límites contra tiempos absolutos.
const int fractionalRate = 44101;
const double fractionalStart = 0.0137;
string longA = "1011001010110010", longB = "0100110101001101";
var fractional = decoder.Decode(Create(longA, longB, fractionalRate, fractionalStart, 0), fractionalRate, fractionalStart, 16);
Require(fractional.ChannelA.Bits == longA && fractional.ChannelB.Bits == longB, "16 símbolos sin deriva");
foreach (var symbol in fractional.ChannelA.Symbols)
{
    Require(symbol.StartSample == (int)Math.Round((fractionalStart + symbol.Index * 0.1) * fractionalRate), "Inicio absoluto");
    Require(symbol.EndSample == (int)Math.Round((fractionalStart + (symbol.Index + 1) * 0.1) * fractionalRate), "Fin absoluto");
    Require(symbol.AnalysisStartSample == (int)Math.Round((fractionalStart + (symbol.Index + 0.2) * 0.1) * fractionalRate), "Ventana absoluta");
}
var generated = new FdmBitstreamGenerator().Generate("10110010", "01001101");
var integrated = decoder.Decode(generated, 48000, 0, 8);
Require(integrated.ChannelA.Bits == "10110010" && integrated.ChannelB.Bits == "01001101", "Integración generador real");
foreach (double invalid in new[] { -0.01, double.NaN, double.PositiveInfinity, double.MaxValue })
    ExpectInvalid(() => decoder.Decode(generated, 48000, invalid, 8));
foreach (int invalid in new[] { 0, -1, 17 }) ExpectInvalid(() => decoder.Decode(generated, 48000, 0, invalid));
ExpectInvalid(() => decoder.Decode(generated, 48000, 0.01, 8));
ExpectInvalid(() => decoder.Decode(Array.Empty<float>(), 48000, 0, 1));
ExpectInvalid(() => decoder.Decode(generated, 7400, 0, 8));
Console.WriteLine("Límites fraccionarios, integración y validación de parámetros/rangos: OK");

static float[] Create(string a, string b, int rate, double start, double noise, int damagedChannel = -1)
{
    int endSample = (int)Math.Round((start + a.Length * 0.1) * rate);
    var samples = new float[endSample + rate / 5];
    var random = new Random(123);
    for (int i = 0; i < samples.Length; i++) samples[i] = (float)(noise * (2 * random.NextDouble() - 1));
    double phaseA = 0.37, phaseB = 0.81;
    for (int symbol = 0; symbol < a.Length; symbol++)
    {
        double fa = a[symbol] == '0' ? 1200 : 1900, fb = b[symbol] == '0' ? 3000 : 3700;
        int begin = (int)Math.Round((start + symbol * 0.1) * rate);
        int end = (int)Math.Round((start + (symbol + 1) * 0.1) * rate);
        for (int i = begin; i < end; i++)
        {
            double sa = 0.075 * Math.Sin(phaseA), sb = 0.075 * Math.Sin(phaseB);
            if (symbol == 1) { if (damagedChannel == 0) sa = 0; if (damagedChannel == 1) sb = 0; }
            if (symbol == 2)
            {
                if (damagedChannel == 0) sa = 0.0375 * (Math.Sin(2 * Math.PI * 1200 * i / rate) + Math.Sin(2 * Math.PI * 1900 * i / rate));
                if (damagedChannel == 1) sb = 0.0375 * (Math.Sin(2 * Math.PI * 3000 * i / rate) + Math.Sin(2 * Math.PI * 3700 * i / rate));
            }
            samples[i] += (float)(sa + sb);
            phaseA += 2 * Math.PI * fa / rate;
            phaseB += 2 * Math.PI * fb / rate;
        }
    }
    return samples;
}
static void CheckFinite(FdmSequenceResult result)
{
    foreach (var symbol in result.ChannelA.Symbols.Concat(result.ChannelB.Symbols))
        Require(double.IsFinite(symbol.Detection.Energy0) && symbol.Detection.Energy0 >= 0 &&
            double.IsFinite(symbol.Detection.Energy1) && symbol.Detection.Energy1 >= 0, "Energías finitas no negativas");
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
