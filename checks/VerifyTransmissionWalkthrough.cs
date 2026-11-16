#:property TargetFramework=net10.0-windows
#:project ../AcousticLink.App/AcousticLink.App.csproj

using AcousticLink.App.Protocol;
using AcousticLink.App.Signals;
using AcousticLink.App.Visualization;

foreach (bool hamming in new[] { false, true })
foreach (string message in new[] { "Hola", "ñ", "🎵🎵" })
{
    var modem = new BfskTextModem();
    var frame = modem.Build(message, hamming);
    var samples = modem.Generate(message, hamming);
    var presentation = TransmissionWalkthrough.Transmitter(message, null, hamming, samples);
    Require(presentation.Mode == "BFSK" && presentation.Steps.Count == 8, "Etapas BFSK");
    Require(presentation.Steps[1].Items.Count == message.EnumerateRunes().Count(), "Unidades Unicode");
    Require(presentation.Steps[2].Items.Single(i => i.Label.Contains("CRC-32")).Value == frame.OriginalBlockBits[^32..], "CRC y bloque real");
    Require(presentation.Steps[4].Items[0].Value == frame.FrameBits, "Trama transmitida");
    Require(presentation.Steps[5].Items.Count == frame.TransmittedBits.Length, "Un símbolo por bit");
    Require(presentation.Steps[5].Items.All((item) => item.Value.EndsWith("Hz")) && presentation.Duration == samples.Length / 48000.0, "Frecuencia y duración digital");
    var received = new BfskTextReceiver().Receive(samples, 48000, useHamming: hamming);
    var receiverData = TransmissionWalkthrough.Receiver(BfskPresentation.AsSingleChannel(received), true, "WAV sintético", 48000, samples.Length, WaveformView.Reduce(samples));
    Require(receiverData.Steps[^1].Items.Any(i => i.Value == message), "Texto validado del receptor");
    var failed = received.Channel with { State = FdmTextReceiveState.CrcIncorrect, CrcValid = false, Text = null, Reason = "CRC incorrecto" };
    var rejected = TransmissionWalkthrough.Receiver(BfskPresentation.AsSingleChannel(received) with { ChannelA = failed }, true, "WAV", 48000, samples.Length, []);
    Require(!rejected.Steps[^1].Items.Any(i => i.Value == message), "CRC incorrecto no publica texto");
}
var fdmFrames = new FdmTextFrameBuilder().Build("Hola", "ñ", true);
var fdmSamples = new FdmBitstreamGenerator().GenerateTextFrames("Hola", "ñ", true);
var fdm = TransmissionWalkthrough.Transmitter("Hola", "ñ", true, fdmSamples);
Require(fdm.Mode == "FDM" && fdmFrames.ChannelB.PaddingBitCount > 0, "FDM desigual");
Require(fdm.Steps[4].Items[3].Value.Length == fdmFrames.ChannelB.PaddingBitCount, "Relleno fuera de trama");
Require(fdm.Steps[5].Items.Count == 2 * fdmFrames.TotalSymbols, "Ambos canales simbolizados");
Require(fdm.Steps[5].Items.First(i => i.Label.StartsWith("B")).Value.Contains("3000 Hz") ||
    fdm.Steps[5].Items.First(i => i.Label.StartsWith("B")).Value.Contains("3700 Hz"), "Banda B");
try { TransmissionWalkthrough.Transmitter("", null, false, []); throw new Exception("Se aceptó mensaje vacío"); }
catch (ArgumentException) { }
Console.WriteLine("Recorrido: bytes, campos, Hamming, relleno, símbolos, frecuencias, CRC y rechazo: OK");
static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
