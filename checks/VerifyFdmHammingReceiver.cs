#:property TargetFramework=net10.0-windows
#:project ../AcousticLink.App/AcousticLink.App.csproj

using AcousticLink.App.Protocol;
using AcousticLink.App.Signals;

var receiver = new FdmTextReceiver();
var builder = new FdmTextFrameBuilder();
var sender = new FdmBitstreamGenerator();
foreach (int rate in new[] { 48000, 44100 })
foreach (var (a, b) in new[] { ("Hola", "Mundo"), ("ab", "cd"), ("ñ", "áé"), ("a", "🎵🎵") })
foreach (double start in new[] { 0.137, 0.509 })
{
    var audio = WithPadding(sender.GenerateTextFrames(a, b, true), rate, start);
    var result = receiver.Receive(audio, rate, useHamming: true);
    Require(result.ChannelA.Text == a && result.ChannelB.Text == b, "Textos exactos");
    var frames = builder.Build(a, b, true);
    foreach (var (channel, frame) in new[] { (result.ChannelA, frames.ChannelA), (result.ChannelB, frames.ChannelB) })
    {
        var h = channel.HammingDiagnostics!;
        Require(channel.State == FdmTextReceiveState.Valid && channel.CrcValid == true && channel.UsesHamming, "Mensaje validado");
        Require(channel.HammingCoversCompleteBlock && h.WordCount == frame.EncodedBlockBitCount / 7, "Palabras exactas sin relleno");
        Require(h.AppliedBitFlipCount == 0 && h.ZeroSyndromeWordCount == h.WordCount, "Cero inversiones sin errores");
    }
    Console.WriteLine($"{rate} Hz / {start} s: {a}/{b}, CRC y estadísticas sin errores: OK");
}
var example = builder.Build("Hola", "Mundo", true);
string originalA = example.ChannelA.TransmittedBits, originalB = example.ChannelB.TransmittedBits;
// Remodular deliberadamente un símbolo usando FdmToneMixer, sin perturbar arbitrariamente el audio.
foreach (int offset in new[] { 0, 14, 126 - 7 })
{
    int position = 3;
    string changed = Flip(originalA, 24 + offset + position - 1);
    var result = receiver.Receive(Controlled(changed, originalB), 48000, useHamming: true);
    Require(result.ChannelA.Text == "Hola" && result.ChannelB.Text == "Mundo", "Corrección en longitud/contenido/CRC");
    var h = result.ChannelA.HammingDiagnostics!;
    Require(h.WordCount == 18 && h.AppliedBitFlipCount == 1 && h.CorrectedWordCount == 1 && h.ZeroSyndromeWordCount == 17, "Sin doble conteo de longitud");
    Require(h.Words[offset / 7].Syndrome == position && h.Words[offset / 7].FlippedPosition == position, "Diagnóstico de posición");
    Require(result.ChannelA.ByteLength == 4, "Longitud corregida");
}
string doubleA = Flip(Flip(originalA, 24 + 14), 24 + 15);
var doubleError = receiver.Receive(Controlled(doubleA, originalB), 48000, useHamming: true);
Require(doubleError.ChannelA.State == FdmTextReceiveState.CrcIncorrect && doubleError.ChannelA.Text is null && doubleError.ChannelB.Text == "Mundo", "Doble error: corrupción residual rechazada");
Require(doubleError.ChannelA.HammingDiagnostics!.AppliedBitFlipCount == 1, "Inversión intentada no equivale a corrección garantizada");
var reverse = receiver.Receive(Controlled(originalA, Flip(Flip(originalB, 24 + 14), 24 + 15)), 48000, useHamming: true);
Require(reverse.ChannelA.Text == "Hola" && reverse.ChannelB.State == FdmTextReceiveState.CrcIncorrect, "Independencia inversa");
foreach (int unknown in new[] { 25, 40 })
{
    string damaged = originalA[..unknown] + "?" + originalA[(unknown + 1)..];
    var result = receiver.Receive(Controlled(damaged, originalB), 48000, useHamming: true);
    Require(result.ChannelA.State == (unknown < 38 ? FdmTextReceiveState.InconclusiveLength : FdmTextReceiveState.InconclusiveBits), "Símbolo no concluyente");
    Require(result.ChannelB.Text == "Mundo", "Canal válido preservado ante '?'");
}
var invalidLength = new Hamming74Codec().Encode("00001001");
var invalid = receiver.Receive(Controlled(originalA[..24] + invalidLength + originalA[38..], originalB), 48000, useHamming: true);
Require(invalid.ChannelA.State == FdmTextReceiveState.InvalidLength && invalid.ChannelA.ByteLength == 9 && invalid.ChannelB.Text == "Mundo", "Longitud fuera del rango");
Require(invalid.ChannelA.HammingDiagnostics!.WordCount == 2 && !invalid.ChannelA.HammingCoversCompleteBlock, "Diagnóstico de longitud solamente");
var full = Controlled(originalA, originalB);
var truncated = receiver.Receive(full[..(int)((0.237 + 15.2) * 48000)], 48000, useHamming: true);
Require(truncated.ChannelA.Text == "Hola" && truncated.ChannelB.State == FdmTextReceiveState.Truncated, "Un canal completo y otro truncado");
var shortLength = receiver.Receive(full[..(int)(4 * 48000)], 48000, useHamming: true);
Require(shortLength.ChannelA.State == FdmTextReceiveState.Truncated, "Bloque protegido insuficiente");
var silence = receiver.Receive(new float[48000 * 25], 48000, useHamming: true);
Require(silence.ChannelA.State == FdmTextReceiveState.HeaderNotSynchronized, "Silencio sin falsas cabeceras");
var random = new Random(123);
var noise = Enumerable.Range(0, 48000 * 25).Select(_ => (float)(0.04 * (2 * random.NextDouble() - 1))).ToArray();
Require(receiver.Receive(noise, 48000, useHamming: true).ChannelA.Text is null, "Ruido sin trama");
Require(receiver.Receive(full.Concat(full).ToArray(), 48000, useHamming: true).ChannelA.State == FdmTextReceiveState.AmbiguousSynchronization, "Dos transmisiones ambiguas");
float[] plain = sender.GenerateTextFrames("Hola", "Mundo");
Require(plain.SequenceEqual(sender.GenerateTextFrames("Hola", "Mundo", false)), "Emisor predeterminado intacto");
var defaultResult = receiver.Receive(plain, 48000);
var explicitPlain = receiver.Receive(plain, 48000, useHamming: false);
Require(defaultResult.ChannelA.Text == explicitPlain.ChannelA.Text && defaultResult.ChannelB.Text == explicitPlain.ChannelB.Text &&
    defaultResult.ChannelA.State == explicitPlain.ChannelA.State && defaultResult.ChannelB.CrcValid == explicitPlain.ChannelB.CrcValid,
    "Recepción predeterminada equivalente al modo explícito sin Hamming");
Require(defaultResult.ChannelA.Text == "Hola" && defaultResult.ChannelB.Text == "Mundo" && defaultResult.ChannelA.HammingDiagnostics is null && !defaultResult.ChannelA.UsesHamming, "Sin estadísticas Hamming en modo anterior");
using var cancellation = new CancellationTokenSource();
var search = Task.Run(() => receiver.Receive(new float[48000 * 30], 48000, cancellation.Token, true));
await Task.Delay(5);
cancellation.Cancel();
try { await search; throw new InvalidOperationException("Búsqueda sin cancelar"); }
catch (OperationCanceledException) { }
Console.WriteLine("Errores deliberados, diagnósticos, independencia, truncación, ambigüedad, compatibilidad y cancelación: OK");

float[] Controlled(string changedA, string changedB)
{
    var audio = sender.GenerateTextFrames("Hola", "Mundo", true);
    for (int i = 0; i < changedA.Length; i++)
    {
        if (changedA[i] == originalA[i] && changedB[i] == originalB[i]) continue;
        float[] symbol = new FdmToneMixer().Generate(changedA[i] == '1', changedB[i] == '1', 0.1);
        if (changedA[i] == '?')
            for (int j = 0; j < symbol.Length; j++) symbol[j] = (float)(0.075 * Math.Sin(2 * Math.PI * (changedB[i] == '0' ? 3000 : 3700) * j / 48000));
        Array.Copy(symbol, 0, audio, i * 4800, 4800);
    }
    return WithPadding(audio, 48000, 0.237);
}
static float[] WithPadding(float[] audio, int rate, double start)
{
    int offset = (int)Math.Round(start * rate);
    int count = (int)Math.Round(audio.Length * (double)rate / 48000);
    var result = new float[offset + count + rate / 5];
    for (int i = 0; i < count; i++)
    {
        double source = i * 48000.0 / rate;
        int j = (int)source;
        result[offset + i] = (float)(audio[j] * (1 - (source - j)) + audio[Math.Min(j + 1, audio.Length - 1)] * (source - j));
    }
    return result;
}
static string Flip(string bits, int index) => bits[..index] + (bits[index] == '0' ? "1" : "0") + bits[(index + 1)..];
static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
