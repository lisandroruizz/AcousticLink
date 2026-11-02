#:property TargetFramework=net10.0-windows
#:project ../AcousticLink.App/AcousticLink.App.csproj

using AcousticLink.App.Signals;

var synchronizer = new BfskFrameSynchronizer();
string frame = new BfskFrameBuilder().Build("10110010");
foreach (int rate in new[] { 48000, 44100 })
{
    foreach (double start in new[] { 0, 0.137, 0.503, 1.009, 1.014 })
    {
        float[] audio = Create(frame, rate, start, 0);
        var result = synchronizer.Find(audio, rate, 8);
        Require(result.Found && result.Data?.Bits == "10110010", $"Trama válida a {start} s");
        Require(result.Preamble == BfskFrameBuilder.Preamble && result.Marker == BfskFrameBuilder.StartMarker, "Cabecera completa");
        Require(Math.Abs(result.StartSeconds!.Value - start) < 0.02, "Precisión temporal");
        Require(Math.Abs(result.DataStartSeconds!.Value - result.StartSeconds.Value - 2.4) < 1e-10, "Inicio de datos");
    }
    Require(synchronizer.Find(Create(frame, rate, 0.753, 0.08), rate, 8).Data?.Bits == "10110010", "Ruido moderado previo y durante trama");
    Require(!synchronizer.Find(new float[rate * 5], rate, 8).Found, "Silencio sin falsa sincronización");
    string wrong = BfskFrameBuilder.Preamble + "00101100" + "10110010";
    Require(!synchronizer.Find(Create(wrong, rate, 0.2, 0), rate, 8).Found, "Marcador incorrecto rechazado");
    string oneError = BfskFrameBuilder.Preamble + "11010010" + "10110010";
    Require(!synchronizer.Find(Create(oneError, rate, 0.2, 0), rate, 8).Found, "Un solo bit erróneo del marcador se rechaza");
    var uncertain = Create(frame, rate, 0.2, 0);
    Array.Clear(uncertain, (int)Math.Round(0.7 * rate), rate / 10);
    Require(!synchronizer.Find(uncertain, rate, 8).Found, "Símbolo no concluyente de cabecera rechazado");
    float[] truncated = Create(frame, rate, 0.4, 0);
    Require(!synchronizer.Find(truncated[..(int)(rate * 3.2)], rate, 8).Found, "Trama truncada");
    Require(!synchronizer.Find(new float[rate], rate, 8).Found, "Grabación corta");
    var first = Create(frame, rate, 0.1, 0);
    var second = Create(frame, rate, 0.2, 0);
    var multiple = first.Concat(second).ToArray();
    var ambiguous = synchronizer.Find(multiple, rate, 8);
    Require(!ambiguous.Found && ambiguous.Reason.Contains("ambigua"), "Varias tramas no se eligen arbitrariamente");
    Console.WriteLine($"{rate} Hz: inicios variables, ruido, silencio, marcador incorrecto, truncación y múltiples tramas: OK");
}
foreach (int count in new[] { 0, 17 })
{
    try { synchronizer.Find(new float[48000], 48000, count); }
    catch (ArgumentOutOfRangeException) { continue; }
    throw new InvalidOperationException("Cantidad inválida aceptada");
}
using var cancellation = new CancellationTokenSource();
cancellation.Cancel();
try { synchronizer.Find(new float[48000 * 5], 48000, 8, cancellation.Token); }
catch (OperationCanceledException) { Console.WriteLine("Cancelación: OK"); return; }
throw new InvalidOperationException("La búsqueda no respetó la cancelación");

static float[] Create(string bits, int rate, double start, double noise)
{
    int origin = (int)Math.Round(start * rate);
    int perBit = rate / 10;
    var random = new Random(123);
    var samples = new float[origin + bits.Length * perBit];
    for (int i = 0; i < samples.Length; i++) samples[i] = (float)(noise * (2 * random.NextDouble() - 1));
    double phase = 0.37;
    for (int i = origin; i < samples.Length; i++)
    {
        double frequency = bits[(i - origin) / perBit] == '0' ? 1200 : 1900;
        samples[i] += (float)(0.15 * Math.Sin(phase));
        phase += 2 * Math.PI * frequency / rate;
    }
    return samples;
}
static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
