#:property TargetFramework=net10.0-windows
#:project ../AcousticLink.App/AcousticLink.App.csproj
using System.Diagnostics;
using AcousticLink.App.Protocol;
using AcousticLink.App.Signals;
using AcousticLink.App.Visualization;

static long Allocated(Action action)
{
    long before = GC.GetAllocatedBytesForCurrentThread();
    action();
    return GC.GetAllocatedBytesForCurrentThread() - before;
}

long receiverBytes = Allocated(() => _ = new ContinuousFdmReceiver(48000));
long monitorBytes = Allocated(() => _ = new IncrementalSignalMonitor(48000, 48000 * 30));
Console.WriteLine($"Inicialización FDM: receptor {receiverBytes:N0} bytes, monitor {monitorBytes:N0} bytes; ruta actual crea dos monitores en el primer bloque ({receiverBytes + 2 * monitorBytes:N0} bytes aproximados)");

static void Probe(string name, IContinuousTextReceiver receiver, float[] signal)
{
    long maxTicks = 0;
    int maxAt = -1;
    long[] largest = new long[5];
    for (int at = 0; at < signal.Length; at += 240)
    {
        long before = Stopwatch.GetTimestamp();
        receiver.Append(signal.AsSpan(at, Math.Min(240, signal.Length - at)));
        long elapsed = Stopwatch.GetTimestamp() - before;
        if (elapsed > maxTicks) { maxTicks = elapsed; maxAt = at; }
        if (elapsed > largest[^1]) { largest[^1] = elapsed; Array.Sort(largest); Array.Reverse(largest); }
    }
    Console.WriteLine($"{name}: pico Append {maxTicks * 1000.0 / Stopwatch.Frequency:0.000} ms en {maxAt / 48000.0:0.000} s; cinco mayores {string.Join(", ", largest.Select(t => (t * 1000.0 / Stopwatch.Frequency).ToString("0.000")))} ms; inicios {receiver.EvaluatedHeaderStarts}, recepciones {receiver.ReceptionCount}");
    if (receiver.ReceptionCount == 0) throw new InvalidOperationException($"{name}: no completó trama válida");
}

var bfsk = new BfskTextModem().Generate("Hola");
var fdm = new FdmBitstreamGenerator().GenerateTextFrames("Hola", "Mundo");
Probe("BFSK", new ContinuousBfskReceiver(48000), bfsk);
Probe("FDM", new ContinuousFdmReceiver(48000), fdm);
Probe("BFSK tras rearme", new ContinuousBfskReceiver(48000), new float[48000 * 5].Concat(bfsk).ToArray());
Probe("FDM tras rearme", new ContinuousFdmReceiver(48000), new float[48000 * 5].Concat(fdm).ToArray());
