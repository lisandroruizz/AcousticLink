#:property TargetFramework=net10.0-windows
#:project ../AcousticLink.App/AcousticLink.App.csproj

using AcousticLink.App.Audio;
using AcousticLink.App.Experiments;
using AcousticLink.App.Protocol;
using AcousticLink.App.Signals;

var codec = new Hamming74Codec();
for (int value = 0; value < 16; value++)
{
    string data = Convert.ToString(value, 2).PadLeft(4, '0');
    string encoded = codec.Encode(data);
    Require(codec.Decode(encoded, true).DataBits == data, "Palabra sin borrados");
    for (int first = 0; first < 7; first++)
    {
        var flipped = codec.Decode(Flip(encoded, first), true);
        Require(flipped.DataBits == data && flipped.AppliedBitFlipCount == 1 &&
            flipped.Words[0].Syndrome == first + 1 && flipped.RecoveredErasureCount == 0, "Síndrome y corrección conservados");
        CheckRecovered(Erase(encoded, first), data, 1);
        for (int second = first + 1; second < 7; second++)
        {
            CheckRecovered(Erase(encoded, first, second), data, 2);
            for (int third = second + 1; third < 7; third++)
            {
                var result = codec.Decode(Erase(encoded, first, second, third), true);
                Require(result.DataBits == "????" && !result.IsComplete && result.UnrecoverableWordCount == 1 &&
                    result.RecoveredErasureCount == 0 && result.AppliedBitFlipCount == 0, "Tres borrados rechazados");
            }
        }
    }
}
try { codec.Decode("?000000"); throw new Exception("El modo anterior aceptó '?'"); }
catch (ArgumentException) { }
foreach (string invalidBits in new[] { "", "000000", "00000000", "00000x?" })
{
    try { codec.Decode(invalidBits, true); throw new Exception("Entrada inválida aceptada"); }
    catch (ArgumentException) { }
}
Require(!codec.Decode(Flip(Erase("0000000", 0), 1), true).IsComplete,
    "Un borrado y un bit conocido incorrecto: sin candidata, no se usa el síndrome");
Console.WriteLine("16 palabras: 112 borrados simples, 336 dobles, 560 triples rechazados y 112 inversiones: OK");

var sender = new FdmBitstreamGenerator();
var builder = new FdmTextFrameBuilder();
var receiver = new FdmTextReceiver();
var reference = builder.Build("Hola", "Mundo", true);
string originalA = reference.ChannelA.TransmittedBits, originalB = reference.ChannelB.TransmittedBits;
// Posiciones absolutas de símbolo: cabecera intacta, longitud [24,38), contenido y CRC después.
int[][] cases = [[25], [38, 39], [38, 45], [40], [145], [25, 40, 145]];
foreach (var profile in new[] { FdmAmplitudeProfile.Original, FdmAmplitudeProfile.PrioritizeChannelA })
foreach (var positions in cases)
{
    var samples = Controlled(Erase(originalA, positions), originalB, profile);
    var snapshot = samples.ToArray();
    var off = receiver.Receive(samples, 48000, useHamming: true);
    var on = receiver.Receive(samples, 48000, useHamming: true, recoverErasures: true);
    Require(off.ChannelA.Text is null && off.ChannelB.Text == "Mundo" && !off.ChannelA.ErasureRecoveryEnabled, "Modo anterior y canal independiente");
    Require(on.ChannelA.Text == "Hola" && on.ChannelB.Text == "Mundo" && on.ChannelA.CrcValid == true && on.ChannelA.ByteLength == 4, "Mensaje y longitud recuperados con CRC");
    var h = on.ChannelA.HammingDiagnostics!;
    Require(h.WordCount == 18 && h.RecoveredErasureCount == positions.Length && h.AppliedBitFlipCount == 0 &&
        h.ErasureRecoveredWordCount == positions.Select(p => (p - 24) / 7).Distinct().Count() &&
        h.ZeroSyndromeWordCount + h.ErasureRecoveredWordCount == h.WordCount, "Diagnósticos sin duplicar longitud ni incluir relleno");
    Require(samples.SequenceEqual(snapshot), "El análisis no modifica el audio");
}
foreach (var (changedA, changedB, expectedState) in new[]
{
    (Erase(originalA, 38, 39, 40), originalB, FdmTextReceiveState.InconclusiveBits),
    (Erase(originalA, 24, 25, 26), originalB, FdmTextReceiveState.InconclusiveLength),
    (Flip(Erase(originalA, 38), 39), originalB, FdmTextReceiveState.InconclusiveBits),
    // Dos borrados y un bit conocido incorrecto pueden coincidir con OTRA palabra válida.
    // Se acepta la candidata única bajo la hipótesis indicada, pero este CRC rechaza el daño residual.
    (Flip(Erase(originalA, 38, 39), 40), originalB, FdmTextReceiveState.CrcIncorrect),
    (Flip(Flip(Erase(originalA, 45), 38), 39), originalB, FdmTextReceiveState.CrcIncorrect)
})
{
    var result = receiver.Receive(Controlled(changedA, changedB), 48000, useHamming: true, recoverErasures: true);
    Require(result.ChannelA.State == expectedState && result.ChannelA.Text is null && result.ChannelB.Text == "Mundo", "Rechazo y CRC, sin ocultar B: " + expectedState);
}
var reverse = receiver.Receive(Controlled(originalA, Erase(originalB, 40)), 48000, useHamming: true, recoverErasures: true);
Require(reverse.ChannelA.Text == "Hola" && reverse.ChannelB.Text == "Mundo" && reverse.ChannelB.HammingDiagnostics!.RecoveredErasureCount == 1, "Recuperación independiente de B");
var invalidLength = codec.Encode("00001001");
var invalid = receiver.Receive(Controlled(originalA[..24] + invalidLength + originalA[38..], originalB), 48000, useHamming: true, recoverErasures: true);
Require(invalid.ChannelA.State == FdmTextReceiveState.InvalidLength && invalid.ChannelB.Text == "Mundo", "Longitud inválida limitada antes de reservar/leer");
var full = Controlled(Erase(originalA, 25), originalB);
var truncated = receiver.Receive(full[..(48000 * 5)], 48000, useHamming: true, recoverErasures: true);
Require(truncated.ChannelA.State == FdmTextReceiveState.Truncated, "Truncación tras recuperar longitud");
var plain = sender.GenerateTextFrames("Hola", "Mundo");
var plainResult = receiver.Receive(plain, 48000, recoverErasures: true);
Require(plainResult.ChannelA.Text == "Hola" && plainResult.ChannelB.Text == "Mundo" &&
    !plainResult.ChannelA.ErasureRecoveryEnabled && plainResult.ChannelA.HammingDiagnostics is null, "Sin Hamming no se aplica recuperación");

var analyzer = new FdmExperimentAnalyzer();
var measuredAudio = Controlled(Erase(originalA, 25, 38, 39, 145), originalB);
var before = analyzer.Analyze(measuredAudio, 48000, "Hola", "Mundo", true);
var after = analyzer.Analyze(measuredAudio, 48000, "Hola", "Mundo", true, recoverErasures: true);
Require(before.ChannelA.Before == after.ChannelA.Before && after.ChannelA.Before!.InconclusiveBits == 4 &&
    after.ChannelA.Before.Ber == 0 && after.ChannelA.Before.Coverage < 1, "BER/cobertura previa idénticas y '?' no contados como errores");
Require(before.ChannelA.After!.Coverage < 1 && after.ChannelA.After!.Coverage == 1 && after.ChannelA.After.Ber == 0 &&
    after.ChannelA.Hamming!.RecoveredErasures == 4 && after.ChannelA.Hamming.ErasureRecoveredWords == 3 &&
    after.ChannelA.Hamming.UnrecoverableWords == 0 && after.ChannelA.Hamming.NonzeroSyndromes == 0 &&
    after.ChannelA.Hamming.AppliedBitFlips == 0 && after.ChannelA.Reception.CrcValid == true, "Cobertura posterior y contadores separados");
var unrecoverable = analyzer.Analyze(Controlled(Erase(originalA, 38, 39, 40), originalB), 48000, "Hola", "Mundo", true, recoverErasures: true);
Require(unrecoverable.ChannelA.Hamming!.UnrecoverableWords == 1 && unrecoverable.ChannelA.After!.InconclusiveBits == 4, "Palabra irrecuperable conserva cuatro posiciones");
var wrongReferences = analyzer.Analyze(measuredAudio, 48000, "Otro", "Texto", true, recoverErasures: true);
Require(wrongReferences.ChannelA.Reception.Text == "Hola" && wrongReferences.ChannelB.Reception.Text == "Mundo", "Las referencias nunca reconstruyen el mensaje");
string csv = FdmExperimentCsv.Serialize(after);
string oldHeader = FdmExperimentCsv.Serialize(before).Split('\n')[0];
Require(csv.Contains("\"recuperacion_borrados\";\"borrados_recuperados\";\"palabras_recuperadas_por_borrados\";\"palabras_irrecuperables\"") &&
    oldHeader == csv.Split('\n')[0], "Columnas CSV nuevas al final, esquema estable entre modalidades");

string wav = Path.Combine(Path.GetTempPath(), "AcousticLink-erasures-" + Guid.NewGuid() + ".wav");
try
{
    var captured = new CapturedAudio(48000, 1, 30);
    captured.Append(measuredAudio, measuredAudio.Length);
    captured.Complete(CaptureStopReason.Manual);
    captured.SaveWav(wav);
    var selection = new AudioRecordingSelection();
    selection.ImportWav(wav);
    var imported = selection.Current!.Audio.GetSamples();
    Require(imported.SequenceEqual(measuredAudio), "WAV sin alterar muestras");
    Require(receiver.Receive(imported, 48000, useHamming: true).ChannelA.Text is null &&
        receiver.Receive(imported, 48000, useHamming: true, recoverErasures: true).ChannelA.Text == "Hola", "Mismo WAV: comparación apagado/encendido");
}
finally { File.Delete(wav); }
Console.WriteLine("Integración de longitud/datos/CRC, perfiles, rechazo, independencia, métricas y WAV: OK");

void CheckRecovered(string word, string expected, int count)
{
    var result = codec.Decode(word, true);
    Require(result.IsComplete && result.DataBits == expected && result.RecoveredErasureCount == count &&
        result.ErasureRecoveredWordCount == 1 && result.UnrecoverableWordCount == 0 &&
        result.AppliedBitFlipCount == 0 && result.ZeroSyndromeWordCount == 0 && !result.Words[0].HasSyndrome, "Borrados no son inversiones ni síndromes cero");
}
float[] Controlled(string changedA, string changedB, FdmAmplitudeProfile profile = FdmAmplitudeProfile.Original)
{
    var audio = sender.GenerateTextFrames("Hola", "Mundo", true, profile);
    var gains = FdmAmplitudeProfiles.Get(profile);
    for (int i = 0; i < changedA.Length; i++)
    {
        if (changedA[i] == originalA[i] && changedB[i] == originalB[i]) continue;
        var symbol = new FdmToneMixer().Generate(changedA[i] == '1', changedB[i] == '1', 0.1, profile: profile);
        if (changedA[i] == '?' || changedB[i] == '?')
            for (int j = 0; j < symbol.Length; j++)
                symbol[j] = (float)((changedA[i] == '?' ? 0 : gains.ChannelA * Math.Sin(2 * Math.PI * (changedA[i] == '0' ? 1200 : 1900) * j / 48000)) +
                    (changedB[i] == '?' ? 0 : gains.ChannelB * Math.Sin(2 * Math.PI * (changedB[i] == '0' ? 3000 : 3700) * j / 48000)));
        symbol.CopyTo(audio, i * 4800);
    }
    var padded = new float[11376 + audio.Length + 9600];
    audio.CopyTo(padded, 11376);
    return padded;
}
static string Erase(string bits, params int[] positions)
{
    var chars = bits.ToCharArray();
    foreach (int position in positions) chars[position] = '?';
    return new string(chars);
}
static string Flip(string bits, int index) => bits[..index] + (bits[index] == '0' ? "1" : "0") + bits[(index + 1)..];
static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
