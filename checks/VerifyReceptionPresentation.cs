#:property TargetFramework=net10.0-windows
#:project ../AcousticLink.App/AcousticLink.App.csproj

using AcousticLink.App.Protocol;
using AcousticLink.App.Signals;
using AcousticLink.App.Visualization;

var samples = new FdmBitstreamGenerator().GenerateTextFrames("Hola", "Mundo", true);
var copy = samples.ToArray();
var receiver = new FdmTextReceiver().Receive(samples, 48000, useHamming: true, recoverErasures: true);
var signal = new MonitorSignal(samples, 48000, MonitorSignalOrigin.ImportedWav, "WAV sintético de verificación");
foreach (bool channelB in new[] { false, true })
{
    var trace = ReceivedSymbols.From(receiver, channelB, 48000)[26];
    var selected = DemodulationSelection.From(signal, receiver, channelB, (trace.Symbol.AnalysisStartSample + trace.Symbol.AnalysisSampleCount / 2.0) / 48000)!;
    var measured = SymbolWindowObservation.From(signal, selected, 0, 0)!;
    Require(measured.SampleCount == 4800 && measured.AnalysisSampleCount == 2880 && measured.AnalysisStartSample - measured.StartSample == 960, "Símbolo 100 ms y detector central 60 ms / 2880 muestras");
    double square = 0; float peak = 0;
    foreach (float value in samples.AsSpan(measured.StartSample, measured.SampleCount)) { square += (double)value * value; peak = Math.Max(peak, Math.Abs(value)); }
    Require(measured.Peak == peak && measured.Rms == Math.Sqrt(square / measured.SampleCount), "RMS y pico proceden de la ventana completa real");
    var channel = channelB ? receiver.ChannelB : receiver.ChannelA;
    var activity = ReceptionChannelActivity.From(channel, false);
    Require(activity.Percent == 100 && activity.CrcValid == true && activity.ValidatedText == channel.Text && activity.UnknownBits == 0, "Resumen utiliza resultado y contadores reales");
    var noLength = channel with { ByteLength = null, CrcValid = null, Text = "Referencia que nunca debe mostrarse", State = FdmTextReceiveState.Truncated, IsReceiving = true };
    var unknown = ReceptionChannelActivity.From(noLength, true);
    Require(unknown.ExpectedBits is null && unknown.Percent is null && unknown.ValidatedText is null, "Longitud desconocida no produce porcentaje ni texto validado");
    var invalid = ReceptionChannelActivity.From(channel with { State = FdmTextReceiveState.CrcIncorrect, CrcValid = false }, false);
    Require(invalid.IsInvalid && invalid.ValidatedText is null && invalid.CrcValid == false, "CRC incorrecto no equivale a no comprobado ni valida texto");
}
var live = new IncrementalFdmTextReceiver(48000, true, true);
LiveFdmReception partial = default!;
for (int offset = 0; offset < 48000 * 8; offset += 4800)
    partial = live.Append(samples.AsSpan(offset, 4800));
var partialActivity = ReceptionChannelActivity.From(partial.Result.ChannelA, true);
Require(partialActivity.Stage == "Reconstruyendo datos" && partialActivity.HammingWords >= 2 && partialActivity.Percent is > 0 and < 100 && partialActivity.CrcValid is null, $"Estados y Hamming durante captura: {partialActivity.Stage}, palabras {partialActivity.HammingWords}, progreso {partialActivity.Percent}, CRC {partialActivity.CrcValid}");
var stopped = live.Complete();
Require(stopped.Result.ChannelA.State == FdmTextReceiveState.Truncated && stopped.Result.ChannelA.CrcValid is null && stopped.ObservedResult.ChannelA.Block!.Bits == partial.Result.ChannelA.Block!.Bits, "Cierre incompleto preserva observación y conserva rechazo canónico");
Require(ReceptionChannelActivity.From(stopped.ObservedResult.ChannelA, false).Stage == "Mensaje incompleto", "Captura detenida incompleta no se representa como error confirmado");
Require(SymbolWindowObservation.From(new MonitorSignal(Array.Empty<float>(), 48000, MonitorSignalOrigin.Microphone, "Sin audio"), null, 0, 0) is null, "Sin muestras no hay ventana ficticia");
var silent = SymbolWindowObservation.From(new MonitorSignal(new float[4800], 48000, MonitorSignalOrigin.Microphone, "Silencio"), null, 0, 2880)!;
Require(silent.Rms == 0 && silent.Peak == 0, "Silencio se mide realmente como cero");
Require(samples.SequenceEqual(copy), "Visualización no modifica muestras");
Console.WriteLine("Presentación: fases reales, progreso conocido/desconocido, CRC, palabras Hamming, ventanas 100/60 ms, RMS/pico y conservación de parciales: OK");
static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
