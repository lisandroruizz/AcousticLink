#:property TargetFramework=net10.0-windows
#:project ../AcousticLink.App/AcousticLink.App.csproj

using AcousticLink.App.Audio;
using AcousticLink.App.Experiments;
using AcousticLink.App.Protocol;
using AcousticLink.App.Signals;

var capture = new CapturedAudio(48000, 1, 5); capture.Append([.1f, -.2f], 2);
var before = capture.GetMetrics();
foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
{
    try { capture.Append([.3f, invalid], 2); throw new InvalidOperationException("Paquete no finito aceptado"); }
    catch (ArgumentException) { }
    Require(capture.GetMetrics() == before && capture.GetSamples().SequenceEqual(new[] { .1f, -.2f }), "Paquete inválido no contamina muestras, RMS ni pico");
}
capture.Append([.3f], 1); Require(capture.GetStatus().Count == 3, "El buffer sigue utilizable tras rechazar paquete inválido");

foreach (int rate in new[] { 44101, 44125, 48025 })
{
    var tone = Enumerable.Range(0, rate * 2).Select(i => (float)(.05 * Math.Sin(2 * Math.PI * 1200 * i / rate) + .05 * Math.Sin(2 * Math.PI * 3000 * i / rate))).ToArray();
    var decoder = new FdmSequenceDecoder(); var full = decoder.DecodeProtectedMessageBlock(tone, rate, 0, 14);
    for (int i = 0; i < 14; i++)
    {
        var single = decoder.DecodeMessageSymbol(tone, rate, 0, i);
        Require(single.ChannelA.Symbols[0] == full.ChannelA.Symbols[i] && single.ChannelB.Symbols[0] == full.ChannelB.Symbols[i], "Ventanas y energías idénticas desde origen absoluto");
    }
    bool oldDifference = Enumerable.Range(0, 14).Any(i => Math.Round((i + .8) * .1 * rate) != Math.Round(i * .1 * rate + .8 * .1 * rate));
    if (rate == 48025) Require(oldDifference, "Caso de redondeo reproducible del cálculo anterior");
    var raw = new FdmBitstreamGenerator().GenerateTextFrames("Hola", "Mundo", true);
    int count = (int)Math.Round(raw.Length * (double)rate / 48000);
    var audio = new float[count + rate / 2];
    for (int i = 0; i < count; i++)
    {
        double at = i * 48000.0 / rate; int index = (int)at;
        audio[i] = (float)(raw[index] * (1 - (at - index)) + raw[Math.Min(index + 1, raw.Length - 1)] * (at - index));
    }
    var reception = new FdmTextReceiver().Receive(audio, rate, useHamming: true);
    var measured = new FdmExperimentAnalyzer().Analyze(audio, rate, "Hola", "Mundo", true);
    Require(reception.ChannelA.CrcValid == true && reception.ChannelB.CrcValid == true && measured.ChannelA.Before!.ObservedBits == reception.ChannelA.Block!.Bits && measured.ChannelB.Before!.ObservedBits == reception.ChannelB.Block!.Bits, "Medición y receptor usan las mismas decisiones sin cabecera ni relleno");
}
Console.WriteLine("Estabilidad: rechazo atómico de no finitos, buffer íntegro y ventanas absolutas de receptor/mediciones en tasas fraccionarias: OK");
static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
