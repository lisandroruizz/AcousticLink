#:property TargetFramework=net10.0-windows
#:project ../AcousticLink.App/AcousticLink.App.csproj

using AcousticLink.App.Signals;

var builder = new BfskFrameBuilder();
var generator = new BfskToneGenerator();
var decoder = new BfskSequenceDecoder();
Require(BfskFrameBuilder.Preamble == "1010101010101010", "Patrón de preámbulo");
Require(BfskFrameBuilder.StartMarker == "11010011", "Patrón de marcador");
Require(BfskFrameBuilder.Preamble.Length == 16 && BfskFrameBuilder.StartMarker.Length == 8, "Longitudes de sincronización");
foreach (string data in new[] { "0", "1", "10110010", "0000000000000000", "1111111111111111" })
{
    string original = data;
    string frame = builder.Build(data);
    Require(frame == "101010101010101011010011" + data, "Orden y concatenación");
    Require(data == original && frame[24..] == original, "Datos intactos");
    Require(frame.Length == 24 + data.Length && frame.Length <= 64, "Longitud de trama");
    float[] audio = generator.GenerateSequence(frame);
    Require(audio.Length == frame.Length * 4800, "Cantidad exacta de muestras");
    Require(Math.Abs(audio.Length / 48000.0 - frame.Length * 0.1) < 1e-12, "Duración");
    Require(decoder.Decode(audio, 48000, 0, frame.Length).Bits == frame, "Decodificación completa con inicio conocido");
    Require(decoder.Decode(audio, 48000, 2.4, data.Length).Bits == data, "Decodificación de datos tras 24 bits");
    Console.WriteLine($"{data.Length} bits de datos: {frame.Length} bits de trama, {audio.Length} muestras: OK");
}
foreach (string? invalid in new string?[] { null, "", "2", "10 1", "10\n", new string('1', 17) })
{
    try { builder.Build(invalid!); }
    catch (ArgumentException) { continue; }
    throw new InvalidOperationException("Se aceptaron datos inválidos.");
}
Require(BfskToneGenerator.MaximumSequenceLength == 64, "Límite global intacto");
Console.WriteLine("Patrones, validación y límite global: OK. Ejemplo: 32 bits, 153600 muestras, 3,2 s.");
static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
