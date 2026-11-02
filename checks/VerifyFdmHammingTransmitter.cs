#:property TargetFramework=net10.0-windows
#:project ../AcousticLink.App/AcousticLink.App.csproj

using AcousticLink.App.Protocol;
using AcousticLink.App.Signals;

var builder = new FdmTextFrameBuilder();
var codec = new MessageCodec();
var hamming = new Hamming74Codec();
var generator = new FdmBitstreamGenerator();
var detector = new FdmToneDetector();
string header = BfskFrameBuilder.Preamble + BfskFrameBuilder.StartMarker;
foreach (var (a, b) in new[] { ("Hola", "Mundo"), ("ab", "cd"), ("ñ", "áé"), ("a", "🎵🎵"), ("12345678", "ññññ") })
{
    var frames = builder.Build(a, b, useHamming: true);
    CheckChannel(frames.ChannelA, a);
    CheckChannel(frames.ChannelB, b);
    Require(frames.ChannelA.TransmittedBits.Length == frames.ChannelB.TransmittedBits.Length, "Longitud conjunta");
    var samples = generator.GenerateTextFrames(a, b, useHamming: true);
    Require(samples.Length == frames.TotalSymbols * 4800, "Cantidad exacta de muestras");
    Require(samples.All(x => float.IsFinite(x) && Math.Abs(x) <= 0.15 + 1e-8 && Math.Abs(x) < 1), "Amplitud y muestras finitas");
    Require(samples[0] == 0 && samples[^1] == 0, "Extremos en cero");
    var recoveredA = new System.Text.StringBuilder();
    var recoveredB = new System.Text.StringBuilder();
    double cyclesA = 0, cyclesB = 0;
    for (int symbol = 0; symbol < frames.TotalSymbols; symbol++)
    {
        var detection = detector.Analyze(samples.AsSpan(symbol * 4800 + 960, 2880), 48000);
        Require(detection.ChannelA.Bit.HasValue && detection.ChannelB.Bit.HasValue, "Dos tonos concluyentes");
        recoveredA.Append(detection.ChannelA.Bit);
        recoveredB.Append(detection.ChannelB.Bit);
        double fa = frames.ChannelA.TransmittedBits[symbol] == '0' ? 1200 : 1900;
        double fb = frames.ChannelB.TransmittedBits[symbol] == '0' ? 3000 : 3700;
        for (int j = 0; j < 4800; j++)
        {
            int i = symbol * 4800 + j;
            double envelope = Math.Min(1, Math.Min(i, samples.Length - 1 - i) / 240.0);
            double expected = 0.075 * envelope * (Math.Sin(2 * Math.PI * (cyclesA + fa * j / 48000)) + Math.Sin(2 * Math.PI * (cyclesB + fb * j / 48000)));
            Require(Math.Abs(samples[i] - expected) < 1e-7, "Frecuencias, fases continuas, rampas globales y ausencia de recorte");
        }
        cyclesA += fa * 0.1; cyclesB += fb * 0.1;
    }
    Require(recoveredA.ToString() == frames.ChannelA.TransmittedBits && recoveredB.ToString() == frames.ChannelB.TransmittedBits,
        "La señal representa ambas tramas protegidas completas");
    foreach (var (bits, frame, text) in new[] { (recoveredA.ToString(), frames.ChannelA, a), (recoveredB.ToString(), frames.ChannelB, b) })
    {
        string protectedBlock = bits.Substring(header.Length, frame.EncodedBlockBitCount);
        var decoded = hamming.Decode(protectedBlock);
        Require(codec.Decode(decoded.DataBits) == text, "Recuperación numérica de CRC y UTF-8 sin modificar receptor");
    }

    var original = builder.Build(a, b);
    var explicitOriginal = builder.Build(a, b, useHamming: false);
    int oldLength = header.Length + Math.Max(codec.Encode(a).Length, codec.Encode(b).Length);
    Require(original.ChannelA.TransmittedBits == (header + codec.Encode(a)).PadRight(oldLength, '0') &&
        original.ChannelB.TransmittedBits == (header + codec.Encode(b)).PadRight(oldLength, '0'), "Formato anterior exacto");
    Require(original == explicitOriginal, "Modo predeterminado sin Hamming");
    Require(generator.GenerateTextFrames(a, b).SequenceEqual(generator.GenerateTextFrames(a, b, false)), "Muestras predeterminadas idénticas al modo sin Hamming");
    if (a == "Hola")
    {
        Require(frames.ChannelA.OriginalBlockBitCount == 72 && frames.ChannelB.OriginalBlockBitCount == 80, "Ejemplo: bloques originales");
        Require(frames.ChannelA.EncodedBlockBitCount == 126 && frames.ChannelB.EncodedBlockBitCount == 140, "Ejemplo: Hamming");
        Require(frames.ChannelA.RealBitCount == 150 && frames.ChannelB.RealBitCount == 164, "Ejemplo: tramas reales");
        Require(frames.ChannelA.PaddingBitCount == 14 && frames.ChannelB.PaddingBitCount == 0, "Ejemplo: relleno");
        Require(samples.Length == 787200 && samples.Length / 48000.0 == 16.4, "Ejemplo: 16,4 segundos");
        Require(generator.GenerateTextFrames(a, b).Length == 499200, "Ejemplo sin Hamming: 10,4 segundos");
    }
    Console.WriteLine($"{a}/{b}: {frames.TotalSymbols} símbolos, {samples.Length} muestras, bloques/CRC, relleno, modulación y compatibilidad: OK");
}
foreach (bool mode in new[] { false, true })
foreach (var (a, b) in new[] { ("", "a"), ("a", ""), ("123456789", "a"), ("a", "ñññññ") })
    ExpectInvalid(() => generator.GenerateTextFrames(a, b, mode));
ExpectInvalid(() => generator.Generate(new string('0', 17), new string('1', 17)));
ExpectInvalid(() => generator.GenerateFrames(new string('0', 17), new string('1', 17)));
ExpectInvalid(() => new BfskToneGenerator().GenerateSequence(new string('0', 65)));
Require(FdmTextFrameBuilder.MaximumFrameBits == 128 && FdmTextFrameBuilder.MaximumProtectedFrameBits == 206, "Límites separados por modo");
Console.WriteLine("Límites, entradas inválidas y modos anteriores: OK");

void CheckChannel(FdmTextChannelFrame frame, string text)
{
    string original = codec.Encode(text);
    Require(frame.UsesHamming && frame.OriginalBlockBits == original, "Bloque original y CRC conservados");
    Require(frame.EncodedBlockBits == hamming.Encode(original), "Hamming del bloque entero");
    var decoded = hamming.Decode(frame.EncodedBlockBits);
    Require(decoded.DataBits == original && decoded.CorrectedWordCount == 0 && codec.Decode(decoded.DataBits) == text, "Vuelta exacta y CRC válido");
    Require(frame.FrameBits == header + frame.EncodedBlockBits, "Cabecera sin codificar");
    Require(frame.TransmittedBits[..frame.RealBitCount] == frame.FrameBits && frame.TransmittedBits[frame.RealBitCount..].All(c => c == '0'), "Relleno fuera de Hamming/CRC");
}
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
