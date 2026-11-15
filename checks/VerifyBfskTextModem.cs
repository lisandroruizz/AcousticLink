#:property TargetFramework=net10.0-windows
#:project ../AcousticLink.App/AcousticLink.App.csproj

using AcousticLink.App.Protocol;
using AcousticLink.App.Signals;
using AcousticLink.App.Audio;
using NAudio.Wave;

var sender = new BfskTextModem();
var receiver = new BfskTextReceiver();
foreach (bool hamming in new[] { false, true })
foreach (string message in new[] { "Hola", "Mundo", "ñ", "🎵🎵" })
{
    var frame = sender.Build(message, hamming);
    var signal = sender.Generate(message, hamming);
    Require(signal.Length == frame.FrameBits.Length * 4800, "Duración de trama BFSK");
    var result = receiver.Receive(signal, 48000, useHamming: hamming);
    Require(result.IsValid && result.Channel.Text == message && result.Channel.CrcValid == true,
        $"Vuelta completa {message}, Hamming={hamming}: {result.Channel.State} {result.Channel.Reason}");
    var truncated = receiver.Receive(signal.AsSpan(0, signal.Length - 4800), 48000, useHamming: hamming);
    Require(!truncated.IsValid && truncated.Channel.Text is null, "Trama truncada no válida");
    var live = new IncrementalBfskTextReceiver(48000, hamming);
    for (int i = 0; i < signal.Length; i += 2400)
    {
        var update = live.Append(signal.AsSpan(i, Math.Min(2400, signal.Length - i)));
        if (update.Result.Channel.State != FdmTextReceiveState.Valid)
            Require(update.Result.Channel.Text is null, "Avance parcial sin texto validado");
    }
    Require(live.Complete().Result.Channel.Text == message, "Recepción incremental completa");
}

Require(!receiver.Receive(new float[48000 * 5], 48000).IsValid, "Silencio sin mensaje");
var random = new Random(13);
var noise = Enumerable.Range(0, 48000 * 5).Select(_ => (float)((random.NextDouble() - .5) * .02)).ToArray();
Require(!receiver.Receive(noise, 48000).IsValid, "Ruido sin mensaje");
var source44 = sender.Generate("Mundo", true);
var resampled44 = new float[(int)Math.Round(source44.Length * 44100.0 / 48000)];
for (int i = 0; i < resampled44.Length; i++)
{
    double source = i * 48000.0 / 44100;
    int index = (int)source;
    resampled44[i] = (float)(source44[index] * (1 - (source - index)) +
        source44[Math.Min(index + 1, source44.Length - 1)] * (source - index));
}
Require(receiver.Receive(resampled44, 44100, useHamming: true).Channel.Text == "Mundo", "Recepción a 44.100 Hz");
var firstWindow = sender.Generate("Hola").AsSpan(960, 2880);
double bandA = Math.Max(Energy(firstWindow, 1200), Energy(firstWindow, 1900));
double bandB = Math.Max(Energy(firstWindow, 3000), Energy(firstWindow, 3700));
Require(bandA > 100 * bandB, "Sin portadora FDM del canal B");
string wav = Path.Combine(Path.GetTempPath(), $"acousticlink-bfsk-{Guid.NewGuid():N}.wav");
try
{
    var wave = sender.Generate("Hola");
    using (var writer = new WaveFileWriter(wav, WaveFormat.CreateIeeeFloatWaveFormat(48000, 1)))
        writer.WriteSamples(wave, 0, wave.Length);
    var selection = new AudioRecordingSelection(); selection.ImportWav(wav);
    Require(receiver.Receive(selection.Current!.Audio.GetSamples(), selection.Current.Audio.SampleRate).Channel.Text == "Hola",
        "WAV guardado e importado mantiene mensaje BFSK");
}
finally { if (File.Exists(wav)) File.Delete(wav); }

foreach (bool hamming in new[] { false, true })
{
    var original = sender.Generate("Hola", hamming);
    var bits = sender.Build("Hola", hamming).FrameBits;
    int firstPayload = 24 + (hamming ? 14 : 8);
    var changed = (float[])original.Clone();
    Flip(changed, bits, firstPayload);
    var one = receiver.Receive(changed, 48000, useHamming: hamming);
    Require(hamming ? one.IsValid && one.Channel.Text == "Hola" && one.Channel.HammingDiagnostics?.AppliedBitFlipCount > 0
        : !one.IsValid && one.Channel.CrcValid == false, "Hamming corrige un bit; CRC rechaza sin Hamming");
    if (hamming)
    {
        Flip(changed, bits, firstPayload + 1);
        var two = receiver.Receive(changed, 48000, useHamming: true);
        Require(!two.IsValid && two.Channel.Text is null, "Doble error no publicado como válido");
    }
}

var continuous = new ContinuousBfskReceiver(48000);
var entries = new List<ContinuousReception>(); continuous.Received += entries.Add;
void Feed(float[] data)
{
    for (int i = 0; i < data.Length; i += 2400)
        continuous.Append(data.AsSpan(i, Math.Min(2400, data.Length - i)));
}
Feed(new float[4800]); Feed(sender.Generate("Hola")); Feed(new float[4800]);
Feed(sender.Generate("Mundo")); Feed(new float[4800]); Feed(sender.Generate("Hola")); Feed(new float[4800]);
Require(entries.Count == 3, $"Tres recepciones consecutivas: {entries.Count}");
Require(entries[0].Result.ChannelA.Text == "Hola" && entries[1].Result.ChannelA.Text == "Mundo" &&
    entries[2].Result.ChannelA.Text == "Hola" && entries[2].StartSample > entries[0].StartSample,
    "Texto repetido conserva identidad propia");
var recovery = new ContinuousBfskReceiver(48000);
var recovered = new List<ContinuousReception>(); recovery.Received += recovered.Add;
var incomplete = sender.Generate("Hola");
var broken = incomplete.AsSpan(0, 4800 * (24 + 8 + 16)).ToArray();
void FeedRecovery(float[] data)
{
    for (int i = 0; i < data.Length; i += 2400)
        recovery.Append(data.AsSpan(i, Math.Min(2400, data.Length - i)));
}
FeedRecovery(broken); FeedRecovery(new float[4800 * 2]); FeedRecovery(sender.Generate("Mundo"));
FeedRecovery(new float[4800]);
Require(recovered.Any(e => e.Result.ChannelA.Text == "Mundo" && e.Result.ChannelA.State == FdmTextReceiveState.Valid),
    "Trama truncada seguida de trama nueva recuperable");
Console.WriteLine("BFSK de texto: transmisión, recepción, Hamming, CRC, estados y escucha continua OK");

static void Flip(float[] samples, string frameBits, int symbol)
{
    var replacement = new BfskToneGenerator().Generate(frameBits[symbol] == '0');
    replacement.CopyTo(samples, symbol * 4800);
}
static void Require(bool condition, string label)
{
    if (!condition) throw new Exception(label);
}
static double Energy(ReadOnlySpan<float> samples, double hz)
{
    double real = 0, imaginary = 0;
    for (int i = 0; i < samples.Length; i++)
    {
        double angle = 2 * Math.PI * hz * i / 48000;
        real += samples[i] * Math.Cos(angle); imaginary += samples[i] * Math.Sin(angle);
    }
    return (real * real + imaginary * imaginary) / samples.Length;
}
