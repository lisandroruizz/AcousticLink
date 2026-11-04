#:property TargetFramework=net10.0-windows
#:project ../AcousticLink.App/AcousticLink.App.csproj

using AcousticLink.App.Protocol;
using AcousticLink.App.Signals;

var sender = new FdmBitstreamGenerator();
foreach (int rate in new[] { 48000, 44100 })
foreach (bool hamming in new[] { false, true })
{
    var raw = sender.GenerateTextFrames("ñ", "Mundo", hamming, hamming ? FdmAmplitudeProfile.PrioritizeChannelA : FdmAmplitudeProfile.Original);
    var audio = Pad(raw, rate, .137);
    var random = new Random(73);
    for (int i = 0; i < audio.Length; i++) audio[i] += (float)((random.NextDouble() - .5) * .002);
    Check(audio, rate, hamming, true, expectLiveMessage: true);
    Console.WriteLine($"{rate} Hz · Hamming={hamming}: fragmentos variables, ruido, longitud, progreso y equivalencia exacta: OK");
}

var frames = new FdmTextFrameBuilder().Build("Hola", "Mundo", true);
string bitsA = frames.ChannelA.TransmittedBits, bitsB = frames.ChannelB.TransmittedBits;
foreach (bool badB in new[] { false, true })
{
    string changed = Flip(Flip(badB ? bitsB : bitsA, 24 + 14), 24 + 15);
    var result = Check(Controlled(badB ? bitsA : changed, badB ? changed : bitsB), 48000, true, true);
    Require((badB ? result.ChannelA : result.ChannelB).CrcValid == true && (badB ? result.ChannelB : result.ChannelA).CrcValid == false, "Canales independientes ante CRC incorrecto");
}
foreach (bool badB in new[] { false, true })
{
    var result = Check(Controlled(badB ? bitsA : Flip(bitsA, 16), badB ? Flip(bitsB, 16) : bitsB), 48000, true, true);
    Require((badB ? result.ChannelA : result.ChannelB).CrcValid == true && !(badB ? result.ChannelB : result.ChannelA).HeaderValid, "Canal sin cabecera no bloquea al otro");
}
var invalidLength = new Hamming74Codec().Encode("00001001");
Require(Check(Controlled(bitsA[..24] + invalidLength + bitsA[38..], bitsB), 48000, true, true).ChannelA.State == FdmTextReceiveState.InvalidLength, "Longitud corrupta no habilita lectura arbitraria");
foreach (int index in new[] { 24, 24 + 15, 24 + 100 })
foreach (bool recover in new[] { false, true })
{
    string changed = bitsA[..index] + '?' + bitsA[(index + 1)..];
    var result = Check(Controlled(changed, bitsB), 48000, true, recover);
    Require(result.ChannelB.Text == "Mundo" && (recover ? result.ChannelA.Text == "Hola" : result.ChannelA.CrcValid is null), "Borrados de longitud, contenido y CRC con modo explícito");
}
var full = Pad(sender.GenerateTextFrames("Hola", "Mundo", true), 48000, .237);
foreach (double end in new[] { 1.2, 2.5, 2.637, 2.65, 3.2, 4.0, 9.0, 15.9 })
    Check(full[..(int)(end * 48000)], 48000, true, true);
Check(new float[48000 * 5], 48000, false, false);
var noise = new float[48000 * 4]; var noiseRandom = new Random(11);
for (int i = 0; i < noise.Length; i++) noise[i] = (float)((noiseRandom.NextDouble() - .5) * .01);
Check(noise, 48000, true, true);
Check(full[..(48000 * 3)].Concat(full).ToArray(), 48000, true, true);

using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
var cancelled = new IncrementalFdmTextReceiver(48000, true);
try { cancelled.Append(full.AsSpan(0, 1000), cancellation.Token); throw new Exception("Sin cancelar"); }
catch (OperationCanceledException) { Require(cancelled.Count == 0, "Cancelación anterior al consumo"); }
using var duringSearch = new CancellationTokenSource();
var searching = new IncrementalFdmTextReceiver(48000);
var searchTask = Task.Run(() => searching.Append(new float[48000 * 30], duringSearch.Token));
await Task.Delay(5); duringSearch.Cancel();
try { await searchTask; throw new Exception("Búsqueda larga sin cancelar"); } catch (OperationCanceledException) { }
var restart = new IncrementalFdmTextReceiver(48000);
restart.Append(new float[48000]); Require(restart.Complete().Result.ChannelA.Text is null, "Reinicio sin datos de otra sesión");
try { restart.Append([0]); throw new Exception("Sesión finalizada acepta datos"); } catch (InvalidOperationException) { }
var bounded = new IncrementalFdmTextReceiver(48000, maximumSeconds: 1);
try { bounded.Append(new float[48001]); throw new Exception("Sin límite"); } catch (ArgumentException) { Require(bounded.Count == 0, "No truncar silenciosamente"); }
Console.WriteLine("Cancelación, reinicio, límites, truncación, silencio, ambigüedad, canales, borrados y CRC: OK");

FdmTextReceiveResult Check(float[] audio, int rate, bool hamming, bool recover, bool expectLiveMessage = false)
{
    var original = audio.ToArray();
    var manual = new FdmTextReceiver().Receive(audio, rate, useHamming: hamming, recoverErasures: recover);
    var session = new IncrementalFdmTextReceiver(rate, hamming, recover);
    int[] sizes = [1, 31, 511, 4093, 8192, 1321]; int offset = 0, index = 0; bool liveValid = false, partialLength = false, headerBeforeEnd = false, symbolsBeforeEnd = false, hammingBeforeEnd = false, independentCompletion = false;
    while (offset < audio.Length)
    {
        int count = Math.Min(sizes[index++ % sizes.Length], audio.Length - offset);
        var snapshot = session.Append(audio.AsSpan(offset, count)); offset += count;
        Require(snapshot.SampleCount == offset && !snapshot.IsFinal, "Solo muestras ya recibidas");
        if (offset < Math.Round(2.4 * rate)) Require(!snapshot.Result.ChannelA.HeaderValid && !snapshot.Result.ChannelB.HeaderValid, "No validar cabecera con menos de 2,4 segundos");
        if (offset < audio.Length && snapshot.Result.ChannelA.HeaderValid) headerBeforeEnd = true;
        if (offset < audio.Length && snapshot.Result.ChannelA.Block?.Symbols.Count > 0) symbolsBeforeEnd = true;
        if (snapshot.Result.ChannelA.State == FdmTextReceiveState.Valid && snapshot.Result.ChannelB.IsReceiving) independentCompletion = true;
        foreach (var channel in new[] { snapshot.Result.ChannelA, snapshot.Result.ChannelB })
        {
            if (channel.Block is { } block) Require(block.Symbols.All(s => s.EndSample <= offset), "No se inventan ventanas futuras ni símbolos incompletos");
            if (channel.ByteLength is not null) Require(channel.Block?.Symbols.Count >= (hamming ? 14 : 8), "Longitud solo después de reunir sus bits");
            if (channel.HammingDiagnostics is { } h) { Require(channel.Block?.Symbols.Count >= h.WordCount * 7, "Hamming solo usa palabras recibidas completas"); if (channel.CrcValid is null && h.WordCount > 0 && offset < audio.Length) hammingBeforeEnd = true; }
            if (channel.CrcValid == true) Require(channel.State is FdmTextReceiveState.Valid or FdmTextReceiveState.InvalidUtf8, "CRC no anticipado");
            if (channel.Text is not null) Require(channel.State == FdmTextReceiveState.Valid && channel.CrcValid == true, "Texto solo después de validación");
            if (channel.ByteLength.HasValue && channel.CrcValid is null && channel.IsReceiving) partialLength = true;
        }
        if (snapshot.Result.ChannelA.State == FdmTextReceiveState.Valid && offset < audio.Length - rate / 5) liveValid = true;
    }
    // Comparar también el resultado ya publicado ANTES del cierre canónico.
    if (manual.ChannelA.State == FdmTextReceiveState.Valid && manual.ChannelB.State == FdmTextReceiveState.Valid)
    {
        Require(session.Current!.Result.Synchronization.StartSeconds == manual.Synchronization.StartSeconds, "Inicio en vivo igual al manual antes de finalizar");
        foreach (var (actual, expected) in new[] { (session.Current.Result.ChannelA, manual.ChannelA), (session.Current.Result.ChannelB, manual.ChannelB) })
            Require(actual.State == expected.State && actual.Text == expected.Text && actual.Block!.Symbols.SequenceEqual(expected.Block!.Symbols)
                && actual.HammingDiagnostics?.DataBits == expected.HammingDiagnostics?.DataBits && actual.HammingDiagnostics?.RecoveredErasureCount == expected.HammingDiagnostics?.RecoveredErasureCount,
                "Decisiones y reconstrucción en vivo idénticas, sin depender de Complete");
    }
    foreach (var (actual, expected) in new[] { (session.Current!.Result.ChannelA, manual.ChannelA), (session.Current.Result.ChannelB, manual.ChannelB) })
        if (expected.Block is not null)
            Require(actual.State == expected.State && actual.Block?.Bits == expected.Block.Bits && actual.HammingDiagnostics?.WordCount == expected.HammingDiagnostics?.WordCount
                && actual.HammingCoversCompleteBlock == expected.HammingCoversCompleteBlock, "Resultados fallidos y alcance Hamming en vivo coinciden antes del cierre");
    var beforeClosing = session.Current!;
    var final = session.Complete();
    foreach (var (observed, before) in new[] { (final.ObservedResult.ChannelA, beforeClosing.ObservedResult.ChannelA), (final.ObservedResult.ChannelB, beforeClosing.ObservedResult.ChannelB) })
        if (observed.State == FdmTextReceiveState.Truncated && observed.HeaderValid && final.Result.Synchronization.StartSeconds == beforeClosing.Result.Synchronization.StartSeconds)
            Require(observed.Block?.Bits == before.Block?.Bits, "Al detener se conservan las decisiones parciales, sin validar mensajes inventados");
    Require(final.IsFinal && final.Result.Synchronization.StartSeconds == manual.Synchronization.StartSeconds &&
        final.Result.Synchronization.State == manual.Synchronization.State && final.Result.Synchronization.IsAmbiguous == manual.Synchronization.IsAmbiguous, "Sincronización final idéntica al receptor manual");
    foreach (var (actual, expected) in new[] { (final.Result.ChannelA, manual.ChannelA), (final.Result.ChannelB, manual.ChannelB) })
    {
        Require(actual.State == expected.State && actual.Text == expected.Text && actual.CrcValid == expected.CrcValid && actual.ByteLength == expected.ByteLength && actual.HeaderValid == expected.HeaderValid && !actual.IsReceiving, "Mensaje, longitud, CRC y estado idénticos");
        Require(actual.Block?.Bits == expected.Block?.Bits, "Símbolos 0/1/? idénticos");
        if (actual.Block is { } a && expected.Block is { } b) Require(a.Symbols.SequenceEqual(b.Symbols), "Ventanas absolutas, energías y decisiones idénticas");
        Require(actual.HammingDiagnostics?.DataBits == expected.HammingDiagnostics?.DataBits && actual.HammingDiagnostics?.AppliedBitFlipCount == expected.HammingDiagnostics?.AppliedBitFlipCount && actual.HammingDiagnostics?.RecoveredErasureCount == expected.HammingDiagnostics?.RecoveredErasureCount, "Hamming idéntico");
    }
    Require(session.EvaluatedHeaderStarts <= audio.Length / Math.Max(1, (int)Math.Round(.02 * rate)) + 2, "La búsqueda no recorre nuevamente el prefijo completo");
    Require(audio.SequenceEqual(original), "Muestras originales preservadas");
    if (expectLiveMessage) Require(liveValid && partialLength && headerBeforeEnd && symbolsBeforeEnd && (!hamming || hammingBeforeEnd) && independentCompletion && session.DetectedSymbolPairs <= 182, "Longitud y mensaje aparecen durante captura, sin repetir símbolos");
    return final.Result;
}
float[] Controlled(string a, string b)
{
    var audio = sender.GenerateTextFrames("Hola", "Mundo", true);
    for (int i = 0; i < a.Length; i++)
    {
        if (a[i] == bitsA[i] && b[i] == bitsB[i]) continue;
        var symbol = new FdmToneMixer().Generate(a[i] == '1', b[i] == '1', .1);
        if (a[i] == '?') for (int j = 0; j < symbol.Length; j++) symbol[j] = (float)(.075 * Math.Sin(2 * Math.PI * (b[i] == '0' ? 3000 : 3700) * j / 48000));
        symbol.CopyTo(audio, i * 4800);
    }
    return Pad(audio, 48000, .237);
}
static float[] Pad(float[] input, int rate, double start)
{
    int offset = (int)Math.Round(start * rate), length = (int)Math.Round(input.Length * (double)rate / 48000.0);
    var output = new float[offset + length + rate / 2];
    for (int i = 0; i < length; i++)
    {
        double source = i * 48000.0 / rate; int at = (int)source;
        output[offset + i] = (float)(input[at] * (1 - (source - at)) + input[Math.Min(at + 1, input.Length - 1)] * (source - at));
    }
    return output;
}
static string Flip(string value, int at) => value[..at] + (value[at] == '0' ? '1' : '0') + value[(at + 1)..];
static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
