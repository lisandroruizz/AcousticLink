using AcousticLink.App.Signals;

namespace AcousticLink.App.Visualization;

// Un único escritor en segundo plano. Conserva el prefijo publicado para inspección manual.
// Acotado a la capacidad de captura (30 s); no hace falta descartar muestras con un anillo.
public sealed class IncrementalSignalMonitor
{
    private readonly float[] samples;
    private readonly IncrementalWaveform overview;
    private readonly List<CarrierEnergyWindow> windows = [];
    private readonly int rate, window, hop;
    private int nextWindow;
    private double peak;
    public int Count { get; private set; }
    public int WindowsCalculated => windows.Count;
    public IncrementalSignalMonitor(int sampleRate, int capacity)
    {
        if (sampleRate <= 7400 || capacity <= 0 || capacity > (long)sampleRate * 30) throw new ArgumentOutOfRangeException(nameof(capacity));
        rate = sampleRate; window = (int)Math.Round(rate * .02); hop = (int)Math.Round(rate * .01);
        samples = new float[capacity]; overview = new(capacity, 1600);
    }
    public void Append(ReadOnlySpan<float> added, CancellationToken token = default)
    {
        if (added.Length > samples.Length - Count) throw new ArgumentException("Se supera la capacidad del monitor.");
        foreach (float value in added) if (!float.IsFinite(value)) throw new ArgumentException("Muestra no finita.");
        token.ThrowIfCancellationRequested();
        added.CopyTo(samples.AsSpan(Count)); overview.Append(added);
        foreach (float value in added) peak = Math.Max(peak, Math.Abs((double)value));
        Count += added.Length;
        while (nextWindow + window <= Count)
        {
            token.ThrowIfCancellationRequested();
            var span = samples.AsSpan(nextWindow, window);
            double mean = 0; foreach (float value in span) mean += value; mean /= span.Length;
            windows.Add(new(nextWindow, window,
                GoertzelEnergy.Estimate(span, mean, rate, 1200), GoertzelEnergy.Estimate(span, mean, rate, 1900),
                GoertzelEnergy.Estimate(span, mean, rate, 3000), GoertzelEnergy.Estimate(span, mean, rate, 3700)));
            nextWindow += hop;
        }
    }
    public CarrierEnergyMap Snapshot(MonitorSignalOrigin origin, string description,
        ReadOnlyMemory<float> a = default, ReadOnlyMemory<float> b = default)
    {
        if (Count == 0) throw new InvalidOperationException("Aún no hay muestras.");
        var signal = new MonitorSignal(samples.AsMemory(0, Count), rate, origin, description)
        { ChannelA = a.IsEmpty ? default : a[..Count], ChannelB = b.IsEmpty ? default : b[..Count] };
        return CarrierEnergyMap.Live(signal, overview.Snapshot(), peak, windows.ToArray(), window, hop, overview.SamplesPerBucket);
    }
}
