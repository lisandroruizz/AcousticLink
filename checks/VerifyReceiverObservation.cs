#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property PublishAot=false
#:property PublishTrimmed=false
#:project ../AcousticLink.App/AcousticLink.App.csproj

using AcousticLink.App.Signals;
using AcousticLink.App.Protocol;
using AcousticLink.App.Visualization;

foreach (int rate in new[] { 48000, 44100 })
foreach (int bitA in new[] { 0, 1 })
foreach (int bitB in new[] { 0, 1 })
{
    var samples = Enumerable.Range(0, rate).Select(i => (float)(.075 * Math.Sin(2 * Math.PI * (bitA == 0 ? 1200 : 1900) * i / rate) + .075 * Math.Sin(2 * Math.PI * (bitB == 0 ? 3000 : 3700) * i / rate))).ToArray();
    var original = samples.ToArray(); var signal = new MonitorSignal(samples, rate, MonitorSignalOrigin.ImportedWav, "Ensayo controlado");
    var window = ReceiverObservation.Analyze(signal, .5);
    int first = (int)Math.Round(window.Start * rate), end = (int)Math.Round(window.End * rate);
    Require(window.Detection == new FdmToneDetector().Analyze(samples.AsSpan(first, end - first), rate), "Reutiliza exactamente las decisiones existentes");
    Require(window.Detection.ChannelA.Bit == bitA && window.Detection.ChannelB.Bit == bitB && window.End <= .5, "Hipótesis independientes y soporte temporal");
    Require(samples.SequenceEqual(original), "Sin alterar muestras");
}
var silent = ReceiverObservation.Analyze(new(new float[48000], 48000, MonitorSignalOrigin.Microphone, "Silencio"), .5);
Require(silent.Detection.ChannelA.Bit is null && silent.Detection.ChannelB.Bit is null, "Sin onda inventada para ?");
var shortWindow = ReceiverObservation.Analyze(new(new float[480], 48000, MonitorSignalOrigin.Microphone, "Prefijo corto"), .01);
Require(shortWindow.Detection.ChannelA.Bit is null && shortWindow.End == .01, "No lee muestras futuras");
foreach (bool hamming in new[] { false, true })
{
    var audio = new FdmBitstreamGenerator().GenerateTextFrames("a", "ab", hamming);
    var received = new FdmTextReceiver().Receive(audio, 48000, useHamming: hamming);
    var trace = ReceivedSymbols.From(received, false, 48000);
    Require(trace.Count >= 24 && trace[0].Number == 1 && ReferenceEquals(trace[0].Symbol, received.Synchronization.ChannelA!.Header.Symbols[0]), "Traza reutiliza los símbolos reales de cabecera");
    if (received.ChannelA.Block is { } block)
    {
        Require(trace.Count == 24 + block.Symbols.Count && ReferenceEquals(trace[24].Symbol, block.Symbols[0]), "Bloque recibido sin inventar relleno");
        var selected = ReceivedSymbols.At(trace, (trace[24].Symbol.AnalysisStartSample + trace[24].Symbol.AnalysisSampleCount / 2.0) / 48000, 48000);
        Require(selected == trace[24] && selected.Number == 25, "Posición de símbolo seleccionada desde sus límites reales");
    }
    var fields = ReceptionTimeline.Fields(received, false);
    Require(fields.Count == 6 && fields[0].Name == "Preámbulo" && fields[1].Name == "Marcador", "Campos de cabecera recibida");
    Require(fields[2].Encoded == hamming && !fields[0].Encoded && !fields[^1].Encoded, "Protección de campos correcta");
    Require(fields[^1].Name.Contains("no verificado"), "Relleno esperado no se presenta como observado");
    var noLength = received with { ChannelA = received.ChannelA with { ByteLength = null, CrcValid = null } };
    Require(ReceptionTimeline.Fields(noLength, false).Count == 3, "Sin inventar datos/CRC antes de leer longitud");
    var partial = received with { ChannelA = received.ChannelA with { HeaderValid = false } };
    Require(ReceptionTimeline.Fields(partial, false).Count == 0 && ReceptionTimeline.Fields(partial, true).Count > 0, "Canales independientes");
    var ambiguous = received with { Synchronization = received.Synchronization with { IsAmbiguous = true } };
    Require(ReceivedSymbols.From(ambiguous, false, 48000).Count == 0 && ReceptionTimeline.Fields(ambiguous, false).Count == 0, "No atribuye campos a sincronización ambigua");
}
Require(ReceptionTimeline.Fields(null, false).Count == 0, "Sin datos disponibles");
Console.WriteLine("Interpretación del receptor: detector reutilizado, ventanas, ?, decisiones reales, trama parcial, Hamming e independencia: OK");
static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
