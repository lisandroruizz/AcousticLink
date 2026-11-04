#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property PublishAot=false
#:property PublishTrimmed=false
#:project ../AcousticLink.App/AcousticLink.App.csproj

using AcousticLink.App.Visualization;

foreach (int rate in new[] { 44100, 48000 })
{
    var raw = Enumerable.Range(0, rate * 2).Select(i => (float)(.075 * Math.Sin(2 * Math.PI * 1200 * i / rate) + .075 * Math.Sin(2 * Math.PI * 3700 * i / rate))).ToArray();
    var original = raw.ToArray();
    var monitor = new IncrementalSignalMonitor(rate, raw.Length);
    int offset = 0;
    CarrierEnergyMap? previous = null;
    while (offset < raw.Length)
    {
        int length = Math.Min(1379, raw.Length - offset);
        monitor.Append(raw.AsSpan(offset, length)); offset += length;
        var current = monitor.Snapshot(MonitorSignalOrigin.Microphone, "Captura incremental sintética");
        Require(current.Signal.Samples.Length == offset && current.Signal.Samples.Span.SequenceEqual(raw.AsSpan(0, offset)), "Solo prefijo real publicado");
        Require(current.Windows.All(w => w.StartSample + w.SampleCount <= offset), "Sin ventanas futuras");
        if (previous is not null) Require(previous.Signal.Samples.Span.SequenceEqual(raw.AsSpan(0, previous.Signal.Samples.Length)), "Un snapshot anterior no cambia al agregar muestras");
        previous = current;
    }
    var full = CarrierEnergyMap.Analyze(new(raw, rate, MonitorSignalOrigin.Microphone, "Referencia completa"));
    Require(previous!.Windows.SequenceEqual(full.Windows), "Ventanas incrementales idénticas al cálculo completo");
    int calculated = monitor.WindowsCalculated; monitor.Append([]);
    Require(monitor.WindowsCalculated == calculated, "Sin trabajo duplicado si no llegan muestras");
    Require(raw.SequenceEqual(original), "Preservación de muestras");
    try { monitor.Append([0]); throw new Exception("Falta límite"); } catch (ArgumentException) { }
    using var canceled = new CancellationTokenSource(); canceled.Cancel();
    var restarted = new IncrementalSignalMonitor(rate, raw.Length);
    try { restarted.Append(raw.AsSpan(0, 100), canceled.Token); throw new Exception("Falta cancelación"); } catch (OperationCanceledException) { }
    Require(restarted.Count == 0, "Cancelación antes de publicar");
    restarted.Append(raw.AsSpan(0, rate)); Require(restarted.Count == rate, "Reinicio independiente");
    var silent = new IncrementalSignalMonitor(rate, rate); silent.Append(new float[rate]);
    Require(silent.Snapshot(MonitorSignalOrigin.Generated, "Salida silenciosa").Windows.All(w => w.A0 == 0 && w.A1 == 0 && w.B0 == 0 && w.B1 == 0), "Silencio sin energías inventadas");
    Console.WriteLine($"{rate} Hz: incremental, ventanas nuevas, snapshots estables, capacidad acotada, cancelación, reinicio y silencio: OK");
}
static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
