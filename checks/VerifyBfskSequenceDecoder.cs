#:property TargetFramework=net10.0-windows
#:project ../AcousticLink.App/AcousticLink.App.csproj

using AcousticLink.App.Signals;

var decoder = new BfskSequenceDecoder();
foreach (int rate in new[] { 48_000, 44_100 })
{
    foreach (string bits in new[] { "10110010", "00000000", "11111111" })
    {
        const double start = 0.137;
        var audio = Create(bits, rate, start);
        var result = decoder.Decode(audio, rate, start, bits.Length);
        Require(result.Bits == bits && result.IsComplete && result.ConclusiveCount == 8, "Recuperación limpia");
        Require(result.Symbols[0].StartSample == (int)Math.Round(start * rate), "Inicio con precisión subcentésima");
        int span = result.Symbols[^1].EndSample - result.Symbols[0].StartSample;
        Require(span == (int)(0.8 * rate), "Cantidad de muestras/duración de la secuencia");
        Require(result.Symbols.All(s => s.AnalysisSampleCount == (int)(0.06 * rate)), "Ventanas centrales de 60 ms");
        Require(decoder.Decode(Create(bits, rate, start, noise: 0.08), rate, start, bits.Length).Bits == bits, "Ruido moderado");
    }

    Require(decoder.Decode(new float[rate], rate, 0, 8).Bits == "????????", "Silencio no concluyente");
    var mixture = Create("10110010", rate, 0);
    int bitLength = rate / 10;
    // Segundo símbolo: mezcla de tonos a igual amplitud. Tercero: silencio.
    for (int i = bitLength; i < 2 * bitLength; i++)
        mixture[i] = (float)(0.15 * (Math.Sin(2 * Math.PI * 1200 * i / rate) + Math.Sin(2 * Math.PI * 1900 * i / rate)));
    Array.Clear(mixture, 2 * bitLength, bitLength);
    var partial = decoder.Decode(mixture, rate, 0, 8);
    Require(partial.Bits == "1??10010" && partial.InconclusiveCount == 2 && !partial.IsComplete, "Símbolos ambiguos y silenciosos preservados");
    var random = new Random(123);
    var noise = Enumerable.Range(0, rate).Select(_ => (float)(0.08 * (2 * random.NextDouble() - 1))).ToArray();
    Require(decoder.Decode(noise, rate, 0, 8).Bits == "????????", "Ruido sin tonos");
    ExpectInvalid(() => decoder.Decode(mixture, rate, -0.01, 8));
    ExpectInvalid(() => decoder.Decode(mixture, rate, 0.01, 8));
    ExpectInvalid(() => decoder.Decode(mixture, rate, double.NaN, 8));
    ExpectInvalid(() => decoder.Decode(mixture, rate, double.PositiveInfinity, 8));
    ExpectInvalid(() => decoder.Decode(mixture, rate, double.MaxValue, 8));
    ExpectInvalid(() => decoder.Decode(mixture, rate, 0, 0));
    ExpectInvalid(() => decoder.Decode(mixture, rate, 0, 65));
    ExpectInvalid(() => decoder.Decode(mixture, 3800, 0, 8));
    Console.WriteLine($"{rate} Hz: tres secuencias, inicio 0,137 s, duración, ventanas centrales, ruido, '?' y validaciones: OK");
}

// 44101 Hz fuerza pasos fraccionarios: comprobar 64 límites sin deriva de redondeo.
const int fractionalRate = 44101;
const double fractionalStart = 0.0137;
string longBits = string.Concat(Enumerable.Repeat("10", 32));
var longResult = decoder.Decode(Create(longBits, fractionalRate, fractionalStart), fractionalRate, fractionalStart, 64);
Require(longResult.Bits == longBits, "64 símbolos a tasa fraccionaria por bit");
foreach (var symbol in longResult.Symbols)
{
    Require(symbol.StartSample == (int)Math.Round((fractionalStart + symbol.Index * 0.1) * fractionalRate), "Deriva en inicio");
    Require(symbol.EndSample == (int)Math.Round((fractionalStart + (symbol.Index + 1) * 0.1) * fractionalRate), "Deriva en fin");
}
var generator = new BfskToneGenerator();
Require(decoder.Decode(generator.GenerateSequence("10110010"), 48000, 0, 8).Bits == "10110010", "Integración con emisor");
Console.WriteLine("64 bits, límites absolutos a 44101 Hz e integración con emisor: OK");

static float[] Create(string bits, int rate, double start, double noise = 0)
{
    var samples = new float[(int)Math.Round((start + bits.Length * 0.1) * rate)];
    var random = new Random(123);
    double phase = 0.37;
    for (int symbol = 0; symbol < bits.Length; symbol++)
    {
        int begin = (int)Math.Round((start + symbol * 0.1) * rate);
        int end = (int)Math.Round((start + (symbol + 1) * 0.1) * rate);
        double frequency = bits[symbol] == '0' ? 1200 : 1900;
        for (int i = begin; i < end; i++)
        {
            samples[i] = (float)(0.15 * Math.Sin(phase) + noise * (2 * random.NextDouble() - 1));
            phase += 2 * Math.PI * frequency / rate;
        }
    }
    return samples;
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
