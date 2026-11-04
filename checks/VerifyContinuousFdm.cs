#:property TargetFramework=net10.0-windows
#:project ../AcousticLink.App/AcousticLink.App.csproj
using AcousticLink.App.Protocol;
using AcousticLink.App.Signals;

foreach (bool hamming in new[] { false, true })
{
    var receiver = new ContinuousFdmReceiver(48000, hamming, true);
    var entries = new List<ContinuousReception>(); receiver.Received += entries.Add;
    Feed(new float[48000 * 40]); Require(entries.Count == 0 && receiver.BufferedSamples < receiver.Capacity, "Silencio largo acotado sin recepciones falsas");
    var noiseRandom = new Random(23); Feed(Enumerable.Range(0, 48000 * 3).Select(_ => (float)((noiseRandom.NextDouble() - .5) * .02)).ToArray()); Require(entries.Count == 0, "Ruido sin cabecera no genera historial");
    var sender = new FdmBitstreamGenerator();
    Feed(new float[6571]); Feed(sender.GenerateTextFrames("Hola", "Mundo", hamming)); Feed(new float[48000 * 10]);
    Feed(sender.GenerateTextFrames("Prueba", "Datos", hamming)); Feed(new float[9000]);
    Feed(sender.GenerateTextFrames("Hola", "Mundo", hamming)); Feed(new float[48000]);
    Require(entries.Count == 3, $"Tres transmisiones sin reiniciar (Hamming={hamming}): {entries.Count}");
    Require(entries.Select(e => e.Id).Distinct().Count() == 3 && entries[2].StartSample > entries[1].StartSample, "Identidad temporal, no deduplicación por texto");
    Require(entries[0].Result.ChannelA.Text == "Hola" && entries[1].Result.ChannelA.Text == "Prueba" && entries[2].Result.ChannelB.Text == "Mundo", "Textos independientes consecutivos");
    foreach (var entry in entries)
    {
        var manual = new FdmTextReceiver().Receive(entry.Samples, 48000, useHamming: hamming, recoverErasures: true);
        Require(entry.Result.Synchronization.StartSeconds == manual.Synchronization.StartSeconds, "Inicio manual idéntico");
        foreach (var (actual, expected) in new[] { (entry.Result.ChannelA, manual.ChannelA), (entry.Result.ChannelB, manual.ChannelB) })
            Require(actual.State == expected.State && actual.Text == expected.Text && actual.CrcValid == expected.CrcValid
                && actual.HeaderValid == expected.HeaderValid && actual.ByteLength == expected.ByteLength
                && (actual.Block is null ? expected.Block is null : expected.Block is not null && actual.Block.Symbols.SequenceEqual(expected.Block.Symbols))
                && actual.HammingDiagnostics?.DataBits == expected.HammingDiagnostics?.DataBits
                && actual.HammingDiagnostics?.AppliedBitFlipCount == expected.HammingDiagnostics?.AppliedBitFlipCount
                && actual.HammingDiagnostics?.RecoveredErasureCount == expected.HammingDiagnostics?.RecoveredErasureCount,
                "Equivalencia exacta manual: estados, ventanas, energías, símbolos, Hamming, longitud y CRC");
    }
    Feed(sender.GenerateTextFrames("Hola", "Mundo", hamming).AsSpan(0, 48000 * 5).ToArray());
    Feed(sender.GenerateTextFrames("Prueba", "Datos", hamming)); Feed(new float[48000]);
    Require(entries.Any(e => e.Result.ChannelA.Text == "Prueba" && e.Id > 3), "Truncación seguida de cabecera nueva no bloquea escucha");
    for (int i = 0; i < 12; i++) { Feed(sender.GenerateTextFrames("a", "b", hamming)); Feed(new float[8000]); }
    Require(receiver.History.Count == ContinuousFdmReceiver.HistoryLimit && receiver.BufferedSamples <= receiver.Capacity, "Historial y buffer acotados tras muchas recepciones");
    receiver.Complete(); int stopped = entries.Count; receiver.Complete(); Require(stopped == entries.Count, "Finalizar no duplica");
    using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
    try { receiver.Append(new float[1000], cancellation.Token); throw new Exception("No canceló"); } catch (OperationCanceledException) { }
    Console.WriteLine($"Continuo Hamming={hamming}: silencio, tiempos arbitrarios, fragmentos, consecutivas/repetidas, truncación, manual, límites y cancelación: OK");
    void Feed(float[] audio)
    {
        int[] chunks = [13, 4093, 117, 8192]; int offset = 0, i = 0;
        while (offset < audio.Length) { int n = Math.Min(chunks[i++ % chunks.Length], audio.Length - offset); receiver.Append(audio.AsSpan(offset, n)); offset += n; }
    }
}
var frames = new FdmTextFrameBuilder().Build("Hola", "Mundo", true);
string originalA = frames.ChannelA.TransmittedBits, originalB = frames.ChannelB.TransmittedBits;
foreach (bool badB in new[] { false, true })
{
    var c = new ContinuousFdmReceiver(48000, true, true); var results = new List<ContinuousReception>(); c.Received += results.Add;
    string Flip(string x, int i) => x[..i] + (x[i] == '0' ? '1' : '0') + x[(i + 1)..];
    string damaged = Flip(Flip(badB ? originalB : originalA, 38), 39);
    c.Append(Controlled(badB ? originalA : damaged, badB ? damaged : originalB)); c.Append(new float[48000]);
    Require(results.Count == 1 && (badB ? results[0].Result.ChannelA : results[0].Result.ChannelB).CrcValid == true && (badB ? results[0].Result.ChannelB : results[0].Result.ChannelA).CrcValid == false, "CRC incorrecto independiente en A/B");
    c.Append(new FdmBitstreamGenerator().GenerateTextFrames("a", "b", true)); c.Append(new float[48000]); Require(results.Last().Result.ChannelA.Text == "a", "Rearmado después de CRC incorrecto");
}
foreach (bool absentB in new[] { false, true })
{
    var c = new ContinuousFdmReceiver(48000, true);
    c.Append(Controlled(absentB ? originalA : new string('?', 24) + originalA[24..], absentB ? new string('?', 24) + originalB[24..] : originalB));
    c.Append(new float[48000]);
    Require(c.History.Count == 1 && (absentB ? c.History[0].Result.ChannelA : c.History[0].Result.ChannelB).CrcValid == true
        && !(absentB ? c.History[0].Result.ChannelB : c.History[0].Result.ChannelA).HeaderValid, "Un canal sin cabecera no impide al otro completar");
}
foreach (bool recover in new[] { false, true })
{
    var c = new ContinuousFdmReceiver(48000, true, recover); var results = new List<ContinuousReception>(); c.Received += results.Add;
    c.Append(Controlled(originalA[..24] + "?" + originalA[25..], originalB)); c.Append(new float[48000]);
    Require(results.Count == 1 && results[0].Result.ChannelB.Text == "Mundo" && (recover ? results[0].Result.ChannelA.Text == "Hola" : results[0].Result.ChannelA.CrcValid is null), "Borrado de longitud con recuperación explícita");
}
var maxSignal = new FdmBitstreamGenerator().GenerateTextFrames("12345678", "ññññ", true);
var at44100 = new float[(int)Math.Round(maxSignal.Length * 44100.0 / 48000)];
for (int i = 0; i < at44100.Length; i++) { double pos = i * 48000.0 / 44100; int index = (int)pos; at44100[i] = (float)(maxSignal[index] * (1 - (pos-index)) + maxSignal[Math.Min(index+1,maxSignal.Length-1)] * (pos-index)); }
var maxReceiver = new ContinuousFdmReceiver(44100, true); maxReceiver.Append(at44100); maxReceiver.Append(at44100); maxReceiver.Append(new float[44100]);
Require(maxReceiver.History.Count == 2 && maxReceiver.History.All(x => x.Result.ChannelA.Text == "12345678" && x.Result.ChannelB.Text == "ññññ"), "Tramas máximas consecutivas a 44.100 Hz, sin truncar 20,6 s");
Console.WriteLine("Continuo: CRC A/B, borrados, rearmado y máximos multibyte a 44.100 Hz: OK");
float[] Controlled(string a, string b)
{
    var audio = new FdmBitstreamGenerator().GenerateTextFrames("Hola", "Mundo", true);
    for (int i = 0; i < a.Length; i++)
    {
        if(a[i] == originalA[i] && b[i] == originalB[i]) continue;
        var symbol = new FdmToneMixer().Generate(a[i]=='1', b[i]=='1', .1);
        if(a[i]=='?') for(int j=0;j<symbol.Length;j++) symbol[j]=(float)(.075*Math.Sin(2*Math.PI*(b[i]=='1'?3700:3000)*j/48000));
        if(b[i]=='?') for(int j=0;j<symbol.Length;j++) symbol[j]=(float)(.075*Math.Sin(2*Math.PI*(a[i]=='1'?1900:1200)*j/48000));
        symbol.CopyTo(audio,i*4800);
    }
    return audio;
}
static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
