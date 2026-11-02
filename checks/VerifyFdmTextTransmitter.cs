#:property TargetFramework=net10.0-windows
#:project ../AcousticLink.App/AcousticLink.App.csproj

using AcousticLink.App.Protocol;
using AcousticLink.App.Signals;

var builder = new FdmTextFrameBuilder();
var codec = new MessageCodec();
var generator = new FdmBitstreamGenerator();
var detector = new FdmToneDetector();
foreach (var (a, b) in new[] { ("Hola", "Mundo"), ("ñ", "áé"), ("🎵🎵", "ññññ"), ("a", "12345678") })
{
    var frames = builder.Build(a, b);
    string header = BfskFrameBuilder.Preamble + BfskFrameBuilder.StartMarker;
    CheckFrame(frames.ChannelA, a);
    CheckFrame(frames.ChannelB, b);
    Require(frames.ChannelA.TransmittedBits.Length == frames.ChannelB.TransmittedBits.Length, "Misma línea temporal");
    float[] samples = generator.GenerateTextFrames(a, b);
    Require(samples.Length == frames.TotalSymbols * 4800, "Muestras y duración");
    Require(samples.All(x => float.IsFinite(x) && Math.Abs(x) <= 0.15 + 1e-8 && Math.Abs(x) < 1), "Amplitud y ausencia de recorte");
    Require(samples[0] == 0 && samples[^1] == 0, "Extremos de rampas");
    var recoveredA = new System.Text.StringBuilder();
    var recoveredB = new System.Text.StringBuilder();
    double cyclesA = 0, cyclesB = 0;
    // Integración numérica con inicio conocido, solo en este check: no agrega receptor de texto.
    for (int symbol = 0; symbol < frames.TotalSymbols; symbol++)
    {
        var result = detector.Analyze(samples.AsSpan(symbol * 4800 + 960, 2880), 48000);
        Require(result.ChannelA.Bit.HasValue && result.ChannelB.Bit.HasValue, "Frecuencias por símbolo concluyentes");
        recoveredA.Append(result.ChannelA.Bit);
        recoveredB.Append(result.ChannelB.Bit);
        double fa = frames.ChannelA.TransmittedBits[symbol] == '0' ? 1200 : 1900;
        double fb = frames.ChannelB.TransmittedBits[symbol] == '0' ? 3000 : 3700;
        for (int j = 0; j < 4800; j++)
        {
            int i = symbol * 4800 + j;
            double envelope = Math.Min(1, Math.Min(i, samples.Length - 1 - i) / 240.0);
            double expected = 0.075 * envelope * (Math.Sin(2 * Math.PI * (cyclesA + fa * j / 48000)) + Math.Sin(2 * Math.PI * (cyclesB + fb * j / 48000)));
            Require(Math.Abs(samples[i] - expected) < 1e-7, "Fases, frecuencias y rampas globales sin silencios");
        }
        cyclesA += fa * 0.1; cyclesB += fb * 0.1;
    }
    Require(recoveredA.ToString() == frames.ChannelA.TransmittedBits && recoveredB.ToString() == frames.ChannelB.TransmittedBits, "Recuperación de ambas cadenas completas");
    CheckRecovered(recoveredA.ToString(), a);
    CheckRecovered(recoveredB.ToString(), b);
    if (a == "Hola")
    {
        Require(frames.ChannelA.RealBitCount == 96 && frames.ChannelB.RealBitCount == 104, "Ejemplo: longitudes reales");
        Require(frames.ChannelA.PaddingBitCount == 8 && frames.ChannelB.PaddingBitCount == 0, "Ejemplo: relleno");
        Require(samples.Length == 499200 && samples.Length / 48000.0 == 10.4, "Ejemplo: 499200 muestras, 10,4 s");
    }
    Console.WriteLine($"{a}/{b}: {frames.TotalSymbols} símbolos, {samples.Length} muestras, campos/CRC, relleno y recuperación: OK");

    void CheckFrame(FdmTextChannelFrame frame, string text)
    {
        byte[] block = codec.EncodeBytes(text);
        Require(frame.Utf8ByteCount == block[0], "Longitud en bytes UTF-8");
        Require(frame.FrameBits == header + codec.Encode(text), "Cabecera, bloque y CRC intactos");
        Require(frame.TransmittedBits[..frame.RealBitCount] == frame.FrameBits, "Relleno no altera trama");
        Require(frame.TransmittedBits[frame.RealBitCount..].All(c => c == '0'), "Relleno posterior al CRC");
    }
    void CheckRecovered(string bits, string expected)
    {
        Require(bits[..header.Length] == header, "Cabecera recuperada");
        int bytes = Convert.ToInt32(bits.Substring(header.Length, 8), 2);
        int blockLength = (1 + bytes + MessageCodec.CrcBytes) * 8;
        string blockBits = bits.Substring(header.Length, blockLength);
        Require(codec.Decode(blockBits) == expected, "Longitud leída de la señal, CRC validado y texto exacto");
    }
}
foreach (var (a, b) in new (string?, string?)[] { (null, "a"), ("a", null), ("", "a"), ("a", ""),
    ("123456789", "a"), ("a", "ñññññ"), ("\uD800", "a") })
    ExpectInvalid(() => generator.GenerateTextFrames(a!, b!));
ExpectInvalid(() => generator.Generate(new string('0', 17), new string('1', 17)));
ExpectInvalid(() => generator.GenerateFrames(new string('0', 17), new string('1', 17)));
ExpectInvalid(() => new BfskToneGenerator().GenerateSequence(new string('0', 65)));
Require(FdmTextFrameBuilder.MaximumFrameBits == 128 && codec.Decode(codec.Encode(new string('a', 255))).Length == 255,
    "Límite acústico separado del códec general");
Console.WriteLine("UTF-8 estricto, límites de 8/255 bytes y modos anteriores: OK");
static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
static void ExpectInvalid(Action action)
{
    try { action(); }
    catch (ArgumentException) { return; }
    throw new InvalidOperationException("Se esperaba rechazo de entrada inválida.");
}
