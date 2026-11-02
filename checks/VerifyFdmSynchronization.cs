#:property TargetFramework=net10.0-windows
#:project ../AcousticLink.App/AcousticLink.App.csproj

using AcousticLink.App.Signals;

var sync = new FdmFrameSynchronizer();
var builder = new BfskFrameBuilder();
string a = builder.Build("10110010"), b = builder.Build("01001101");
foreach (int rate in new[] { 48000, 44100 })
{
    foreach (double start in new[] { 0, 0.137, 0.503, 0.509, 1.013 })
    {
        var result = sync.Find(Create(a, b, rate, start, 0), rate, 8);
        Require(result.State == FdmSynchronizationState.Complete, "Sincronización completa");
        Require(result.ChannelA!.Data.Bits == "10110010" && result.ChannelB!.Data.Bits == "01001101", "Datos independientes");
        Require(result.ChannelA.Header.Bits == a[..24] && result.ChannelB!.Header.Bits == b[..24], "Ambas cabeceras");
        Require(Math.Abs(result.StartSeconds!.Value - start) < 0.02, "Inicio estimado");
        Require(Math.Abs(result.DataStartSeconds!.Value - result.StartSeconds.Value - 2.4) < 1e-10, "Inicio de datos");
        Require(result.ChannelA.Data.ConclusiveCount == 8 && result.ChannelB!.Data.InconclusiveCount == 0, "Conteos");
    }
    var noisy = sync.Find(Create(a, b, rate, 0.753, 0.04), rate, 8);
    Require(noisy.State == FdmSynchronizationState.Complete && noisy.ChannelA!.Data.Bits == "10110010" && noisy.ChannelB!.Data.Bits == "01001101", "Ruido moderado");
    Require(sync.Find(new float[rate * 5], rate, 8).State == FdmSynchronizationState.Failed, "Silencio");
    var random = new Random(123);
    float[] noise = Enumerable.Range(0, rate * 5).Select(_ => (float)(0.04 * (2 * random.NextDouble() - 1))).ToArray();
    Require(sync.Find(noise, rate, 8).State == FdmSynchronizationState.Failed, "Ruido sin trama");
    string wrongA = a[..23] + "0" + a[24..];
    string wrongB = b[..23] + "0" + b[24..];
    Require(sync.Find(Create(wrongA, wrongB, rate, 0.2, 0), rate, 8).State == FdmSynchronizationState.Failed, "Marcadores incorrectos");
    foreach (bool damageA in new[] { false, true })
    {
        var partial = sync.Find(Create(damageA ? wrongA : a, damageA ? b : wrongB, rate, 0.2, 0), rate, 8);
        Require(partial.State == FdmSynchronizationState.Partial, "Cabecera alterada genera estado parcial");
        Require(partial.ChannelA!.HeaderValid == !damageA && partial.ChannelB!.HeaderValid == damageA, "Estados por canal");
    }
    // Un canal ausente conserva '?' en cabecera y datos, sin inventar sincronización completa.
    var missing = sync.Find(Create(a, new string('?', b.Length), rate, 0.2, 0), rate, 8);
    Require(missing.State == FdmSynchronizationState.Partial && missing.ChannelB!.Data.Bits == "????????", "Canal ausente");
    // La sincronización de cabecera no implica que todos los datos sean concluyentes.
    var damagedData = sync.Find(Create(a, b[..26] + "?" + b[27..], rate, 0.2, 0), rate, 8);
    Require(damagedData.State == FdmSynchronizationState.Complete && damagedData.ChannelB!.Data.Bits == "01?01101", "Datos '?' preservados");
    var full = Create(a, b, rate, 0.4, 0);
    Require(sync.Find(full[..(int)(3.2 * rate)], rate, 8).State == FdmSynchronizationState.Failed, "Truncación");
    Require(sync.Find(new float[rate], rate, 8).State == FdmSynchronizationState.Failed, "Grabación insuficiente");
    var multiple = Create(a, b, rate, 0.1, 0).Concat(Create(a, b, rate, 0.2, 0)).ToArray();
    var ambiguous = sync.Find(multiple, rate, 8);
    Require(ambiguous.State == FdmSynchronizationState.Failed && ambiguous.Reason.Contains("ambigua") && ambiguous.StartSeconds is null, "Ambigüedad sin elegir trama");
    Console.WriteLine($"{rate} Hz: inicios, ruido, marcadores, estados parciales, '?', truncación y múltiples tramas: OK");
}
float[] generated = new FdmBitstreamGenerator().GenerateFrames("10110010", "01001101");
Require(sync.Find(generated, 48000, 8).State == FdmSynchronizationState.Complete, "Integración con generador FDM");
ExpectInvalid(() => sync.Find(generated, 7400, 8));
ExpectInvalid(() => sync.Find(generated, 48000, 0));
ExpectInvalid(() => sync.Find(generated, 48000, 17));
ExpectInvalid(() => sync.Find(new[] { float.NaN }, 48000, 8));
using var cancellation = new CancellationTokenSource();
cancellation.Cancel();
try { sync.Find(generated, 48000, 8, cancellation.Token); }
catch (OperationCanceledException) { Console.WriteLine("Integración, validación y cancelación: OK"); return; }
throw new InvalidOperationException("No se respetó la cancelación");

static float[] Create(string a, string b, int rate, double start, double noise)
{
    int origin = (int)Math.Round(start * rate), bitLength = rate / 10;
    var samples = new float[origin + a.Length * bitLength];
    var random = new Random(123);
    for (int i = 0; i < samples.Length; i++) samples[i] = (float)(noise * (2 * random.NextDouble() - 1));
    double phaseA = 0.37, phaseB = 0.81;
    for (int i = origin; i < samples.Length; i++)
    {
        int symbol = (i - origin) / bitLength;
        if (a[symbol] != '?') samples[i] += (float)(0.075 * Math.Sin(phaseA));
        if (b[symbol] != '?') samples[i] += (float)(0.075 * Math.Sin(phaseB));
        phaseA += 2 * Math.PI * (a[symbol] == '0' ? 1200 : 1900) / rate;
        phaseB += 2 * Math.PI * (b[symbol] == '0' ? 3000 : 3700) / rate;
    }
    return samples;
}
static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
static void ExpectInvalid(Action action)
{
    try { action(); }
    catch (ArgumentException) { return; }
    throw new InvalidOperationException("Se esperaba rechazo de parámetros inválidos.");
}
