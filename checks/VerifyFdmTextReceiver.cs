#:property TargetFramework=net10.0-windows
#:project ../AcousticLink.App/AcousticLink.App.csproj

using AcousticLink.App.Protocol;
using AcousticLink.App.Signals;
using System.Buffers.Binary;

var receiver = new FdmTextReceiver();
var sender = new FdmBitstreamGenerator();
foreach (int rate in new[] { 48000, 44100 })
foreach (var (a, b) in new[] { ("Hola", "Mundo"), ("ab", "cd"), ("ñ", "áé"), ("🎵🎵", "a"), ("a", "12345678") })
{
    float[] transmitted = sender.GenerateTextFrames(a, b);
    // Remuestreo lineal del emisor real para probar la tasa real de 44,1 kHz.
    var signal = new float[(int)Math.Round(transmitted.Length * (double)rate / 48000)];
    for (int i = 0; i < signal.Length; i++)
    {
        double source = i * 48000.0 / rate;
        int j = (int)source;
        signal[i] = (float)(transmitted[j] * (1 - (source - j)) + transmitted[Math.Min(j + 1, transmitted.Length - 1)] * (source - j));
    }
    foreach (double start in new[] { 0.137, 0.509 })
    {
        var recording = new float[(int)Math.Round(start * rate) + signal.Length + rate / 5];
        signal.CopyTo(recording, (int)Math.Round(start * rate));
        var random = new Random(123);
        for (int i = 0; i < recording.Length; i++) recording[i] += (float)(0.02 * (2 * random.NextDouble() - 1));
        var result = receiver.Receive(recording, rate);
        Require(result.ChannelA.Text == a && result.ChannelB.Text == b, "Texto exacto desde emisor real");
        Require(result.ChannelA.CrcValid == true && result.ChannelB.CrcValid == true, "CRC de ambos canales");
    }
    Console.WriteLine($"{rate} Hz: {a}/{b}, inicio desconocido, ruido, longitud y relleno: OK");
}
var frames = new FdmTextFrameBuilder().Build("Hola", "Mundo");
string goodA = frames.ChannelA.TransmittedBits, goodB = frames.ChannelB.TransmittedBits;
foreach (int bit in new[] { 32, frames.ChannelA.RealBitCount - 1 })
{
    var result = receiver.Receive(Synthesize(Flip(goodA, bit), goodB), 48000);
    Require(result.ChannelA.State == FdmTextReceiveState.CrcIncorrect && result.ChannelA.Text is null, "Rechazo CRC en contenido/CRC");
    Require(result.ChannelB.Text == "Mundo", "Canal B independiente");
}
var reversed = receiver.Receive(Synthesize(goodA, Flip(goodB, 32)), 48000);
Require(reversed.ChannelA.Text == "Hola" && reversed.ChannelB.State == FdmTextReceiveState.CrcIncorrect, "Canal A independiente");
Require(receiver.Receive(Synthesize(Flip(goodA, 24), goodB), 48000).ChannelA.State == FdmTextReceiveState.InvalidLength, "Longitud alterada >8");
Require(receiver.Receive(Synthesize(goodA[..24] + "00000000" + goodA[32..], goodB), 48000).ChannelA.State == FdmTextReceiveState.InvalidLength, "Longitud cero");
Require(receiver.Receive(Synthesize(goodA[..25] + "?" + goodA[26..], goodB), 48000).ChannelA.State == FdmTextReceiveState.InconclusiveLength, "Longitud no concluyente");
Require(receiver.Receive(Synthesize(goodA[..35] + "?" + goodA[36..], goodB), 48000).ChannelA.State == FdmTextReceiveState.InconclusiveBits, "Datos no concluyentes");
var partial = receiver.Receive(Synthesize(Flip(goodA, 23), goodB), 48000);
Require(partial.ChannelA.State == FdmTextReceiveState.HeaderNotSynchronized && partial.ChannelB.Text == "Mundo", "Cabecera parcial");
var truncated = Synthesize(goodA, goodB);
var shortResult = receiver.Receive(truncated[..(int)(48000 * (0.237 + 9.8))], 48000);
Require(shortResult.ChannelA.Text == "Hola" && shortResult.ChannelB.State == FdmTextReceiveState.Truncated, "Un bloque completo y otro truncado");
Require(receiver.Receive(new float[48000], 48000).ChannelA.State == FdmTextReceiveState.Truncated, "Grabación insuficiente");
Require(receiver.Receive(new float[48000 * 12], 48000).ChannelA.State == FdmTextReceiveState.HeaderNotSynchronized, "Silencio sin trama");
var randomNoise = new Random(42);
Require(receiver.Receive(Enumerable.Range(0, 48000 * 12).Select(_ => (float)(0.04 * (2 * randomNoise.NextDouble() - 1))).ToArray(), 48000).ChannelA.Text is null, "Ruido sin trama");
var multiple = Synthesize(goodA, goodB).Concat(Synthesize(goodA, goodB)).ToArray();
Require(receiver.Receive(multiple, 48000).ChannelA.State == FdmTextReceiveState.AmbiguousSynchronization, "Varias tramas ambiguas");
// UTF-8 inválido con CRC correcto, distinguido del CRC inválido.
byte[] invalidUtf8 = { 1, 0xFF, 0, 0, 0, 0 };
BinaryPrimitives.WriteUInt32BigEndian(invalidUtf8.AsSpan(2), Crc32.Compute(invalidUtf8.AsSpan(0, 2)));
string invalidFrame = BfskFrameBuilder.Preamble + BfskFrameBuilder.StartMarker + string.Concat(invalidUtf8.Select(x => Convert.ToString(x, 2).PadLeft(8, '0')));
var utf8Result = receiver.Receive(Synthesize(invalidFrame.PadRight(goodB.Length, '0'), goodB), 48000);
Require(utf8Result.ChannelA.State == FdmTextReceiveState.InvalidUtf8 && utf8Result.ChannelA.CrcValid == true && utf8Result.ChannelB.Text == "Mundo", "UTF-8 estricto e independencia");
using var cancellation = new CancellationTokenSource();
var search = Task.Run(() => receiver.Receive(new float[48000 * 30], 48000, cancellation.Token));
await Task.Delay(5);
cancellation.Cancel();
try { await search; throw new InvalidOperationException("No se canceló la búsqueda"); }
catch (OperationCanceledException) { }
Console.WriteLine("Corrupción controlada, longitudes, '?', cabeceras, truncación, UTF-8 inválido, ambigüedad y cancelación: OK");

static string Flip(string bits, int index) => bits[..index] + (bits[index] == '0' ? "1" : "0") + bits[(index + 1)..];
static float[] Synthesize(string a, string b)
{
    const int origin = 11376; // 0,237 s
    var samples = new float[origin + a.Length * 4800 + 9600];
    double phaseA = 0.37, phaseB = 0.81;
    for (int i = origin; i < origin + a.Length * 4800; i++)
    {
        int bit = (i - origin) / 4800;
        samples[i] = (float)((a[bit] == '?' ? 0 : 0.075 * Math.Sin(phaseA)) + (b[bit] == '?' ? 0 : 0.075 * Math.Sin(phaseB)));
        phaseA += 2 * Math.PI * (a[bit] == '0' ? 1200 : 1900) / 48000;
        phaseB += 2 * Math.PI * (b[bit] == '0' ? 3000 : 3700) / 48000;
    }
    return samples;
}
static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
