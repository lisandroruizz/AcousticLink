#:property TargetFramework=net10.0-windows
#:project ../AcousticLink.App/AcousticLink.App.csproj

using AcousticLink.App.Signals;

var generator = new FdmBitstreamGenerator();
var decoder = new FdmSequenceDecoder();
var builder = new BfskFrameBuilder();
foreach (var (a, b) in new[] { ("10110010", "01001101"), ("0", "1"),
    ("0000000000000000", "1111111111111111"), ("1010101010101010", "0101010101010101") })
{
    var frames = FdmBitstreamGenerator.BuildFrames(a, b);
    Require(frames.ChannelA == builder.Build(a) && frames.ChannelB == builder.Build(b), "Construcción independiente");
    int header = FdmBitstreamGenerator.HeaderBits;
    Require(header == 24 && frames.ChannelA[header..] == a && frames.ChannelB[header..] == b, "Datos intactos");
    Require(frames.ChannelA[..16] == BfskFrameBuilder.Preamble && frames.ChannelB[..16] == BfskFrameBuilder.Preamble, "Preámbulos");
    Require(frames.ChannelA[16..24] == BfskFrameBuilder.StartMarker && frames.ChannelB[16..24] == BfskFrameBuilder.StartMarker, "Marcadores");
    float[] samples = generator.GenerateFrames(a, b);
    Require(samples.Length == (header + a.Length) * 4800, "Longitud/duración conjunta");
    Require(samples.All(x => float.IsFinite(x) && Math.Abs(x) <= 0.15 + 1e-8 && Math.Abs(x) < 1), "Amplitud sin recorte");
    Require(samples[0] == 0 && samples[^1] == 0, "Rampas de extremos");
    var recovered = decoder.DecodeFrameSymbols(samples, 48000, 0, a.Length);
    Require(recovered.ChannelA.Bits == frames.ChannelA && recovered.ChannelB.Bits == frames.ChannelB, "Tramas completas recuperadas");
    Require(recovered.ChannelA.IsComplete && recovered.ChannelB.IsComplete, "Todos los símbolos concluyentes");
    Require(recovered.ChannelA.Symbols[0].StartSample == 0 && recovered.ChannelB.Symbols[0].StartSample == 0, "Comienzo simultáneo");
    var data = decoder.Decode(samples, 48000, 2.4, a.Length);
    Require(data.ChannelA.Bits == a && data.ChannelB.Bits == b, "Datos independientes desde 2,4 s");
    double cyclesA = 0, cyclesB = 0;
    for (int symbol = 0; symbol < frames.ChannelA.Length; symbol++)
    {
        double fa = frames.ChannelA[symbol] == '0' ? 1200 : 1900;
        double fb = frames.ChannelB[symbol] == '0' ? 3000 : 3700;
        for (int j = 0; j < 4800; j++)
        {
            int i = symbol * 4800 + j;
            double envelope = Math.Min(1, Math.Min(i, samples.Length - 1 - i) / 240.0);
            double expected = 0.075 * envelope * (Math.Sin(2 * Math.PI * (cyclesA + fa * j / 48000)) + Math.Sin(2 * Math.PI * (cyclesB + fb * j / 48000)));
            Require(Math.Abs(samples[i] - expected) < 1e-7, "Frecuencias, fases y envolvente sin silencios insertados");
        }
        cyclesA += fa * 0.1;
        cyclesB += fb * 0.1;
    }
    Console.WriteLine($"{a.Length} bits de datos: {frames.ChannelA.Length} símbolos, {samples.Length} muestras, {samples.Length / 48000.0:0.0} s: OK");
}
foreach (var (a, b) in new (string?, string?)[] { (null, "0"), ("0", null), ("", ""), ("01", "0"),
    ("x", "0"), ("0", "2"), ("1 ", "01"), (new string('0', 17), new string('1', 17)) })
    ExpectInvalid(() => generator.GenerateFrames(a!, b!));
ExpectInvalid(() => generator.Generate(new string('0', 17), new string('1', 17)));
ExpectInvalid(() => generator.Generate(new string('0', 40), new string('1', 40)));
ExpectInvalid(() => decoder.DecodeFrameSymbols(new float[192000], 48000, 0, 17));
ExpectInvalid(() => decoder.DecodeFrameSymbols(new float[192000], 48000, 0, 0));
Require(FdmBitstreamGenerator.MaximumBits == 16 && FdmBitstreamGenerator.MaximumFrameBits == 40, "Límites separados");
Console.WriteLine("Validaciones, máximo de trama 40 y modo directo limitado a 16: OK");
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
