using AcousticLink.App.Audio;
using AcousticLink.App.Signals;

namespace AcousticLink.App.Visualization;

public enum MonitorSignalOrigin { Generated, Microphone, ImportedWav }

// El llamador entrega un buffer finalizado que no volverá a escribir. El monitor solo lo lee.
public sealed record MonitorSignal(ReadOnlyMemory<float> Samples, int SampleRate, MonitorSignalOrigin Origin, string Description)
{
    public double Duration => Samples.Length / (double)SampleRate;
    public string OriginLabel => Origin switch
    { MonitorSignalOrigin.Generated => "Señal digital generada · no es recepción acústica", MonitorSignalOrigin.Microphone => "Captura de micrófono finalizada", _ => "WAV importado · muestras originales" };
}

public sealed record CarrierEnergyWindow(int StartSample, int SampleCount, double A0, double A1, double B0, double B1)
{
    public double Energy(int carrier) => carrier switch { 0 => A0, 1 => A1, 2 => B0, 3 => B1, _ => throw new ArgumentOutOfRangeException(nameof(carrier)) };
}
public readonly record struct MonitorInterval(int Start, int End)
{
    public int Count => End - Start;
}

public sealed class CarrierEnergyMap
{
    public static IReadOnlyList<double> Frequencies { get; } = Array.AsReadOnly(new[]
    { FdmToneMixer.ChannelABit0Frequency, FdmToneMixer.ChannelABit1Frequency, FdmToneMixer.ChannelBBit0Frequency, FdmToneMixer.ChannelBBit1Frequency });
    public MonitorSignal Signal { get; }
    public IReadOnlyList<CarrierEnergyWindow> Windows { get; }
    public IReadOnlyList<WaveformBucket> Overview { get; }
    public int WindowSamples { get; }
    public int HopSamples { get; }
    public double Peak { get; }
    private CarrierEnergyMap(MonitorSignal signal, CarrierEnergyWindow[] windows, WaveformBucket[] overview, int window, int hop, double peak)
    { Signal = signal; Windows = Array.AsReadOnly(windows); Overview = Array.AsReadOnly(overview); WindowSamples = window; HopSamples = hop; Peak = peak; }

    public static CarrierEnergyMap Analyze(MonitorSignal signal, CancellationToken token = default)
    {
        if (signal.SampleRate <= Frequencies[^1] * 2) throw new ArgumentException("La tasa debe superar el doble de 3700 Hz.");
        if (signal.Samples.IsEmpty || signal.Samples.Length > (long)signal.SampleRate * CapturedAudio.MaximumSeconds)
            throw new ArgumentException("El monitor admite audio no vacío de hasta 30 segundos.");
        var samples = signal.Samples.Span;
        double peak = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            if (i % 4096 == 0) token.ThrowIfCancellationRequested();
            if (!float.IsFinite(samples[i])) throw new ArgumentException("Las muestras del monitor deben ser finitas.");
            peak = Math.Max(peak, Math.Abs((double)samples[i]));
        }
        int window = Math.Max(1, (int)Math.Round(signal.SampleRate * 0.02));
        int hop = Math.Max(1, (int)Math.Round(signal.SampleRate * 0.01));
        int last = Math.Max(0, samples.Length - window);
        var frames = new List<CarrierEnergyWindow>();
        void Add(int start)
        {
            token.ThrowIfCancellationRequested();
            var values = signal.Samples.Span.Slice(start, Math.Min(window, signal.Samples.Length - start));
            double mean = 0; foreach (float v in values) mean += v; mean /= values.Length;
            // Misma estimación matemática que el receptor, sin sus criterios de decisión.
            frames.Add(new(start, values.Length,
                GoertzelEnergy.Estimate(values, mean, signal.SampleRate, Frequencies[0]),
                GoertzelEnergy.Estimate(values, mean, signal.SampleRate, Frequencies[1]),
                GoertzelEnergy.Estimate(values, mean, signal.SampleRate, Frequencies[2]),
                GoertzelEnergy.Estimate(values, mean, signal.SampleRate, Frequencies[3])));
        }
        for (int start = 0; start <= last; start += hop) Add(start);
        if (frames[^1].StartSample != last) Add(last); // Última ventana anclada al final, sin inventar una cola.
        token.ThrowIfCancellationRequested();
        return new(signal, frames.ToArray(), WaveformView.Reduce(samples, 1600), window, hop, peak);
    }

    public static double Decibels(double energy) => energy > 0 ? 10 * Math.Log10(energy) : double.NegativeInfinity;
    public static MonitorInterval Interval(double start, double end, int rate, int sampleCount)
    {
        if (rate <= 0 || sampleCount <= 0 || !double.IsFinite(start) || !double.IsFinite(end) || start < 0 || end <= start || end > sampleCount / (double)rate + 1e-9)
            throw new ArgumentException("Intervalo temporal fuera de la señal.");
        int first = Math.Clamp((int)Math.Floor(start * rate), 0, sampleCount - 1);
        int last = Math.Clamp((int)Math.Ceiling(end * rate), first + 1, sampleCount);
        return new(first, last);
    }
    public int WindowAt(double seconds)
    {
        double target = seconds * Signal.SampleRate;
        int low = 0, high = Windows.Count - 1;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (Windows[middle].StartSample + Windows[middle].SampleCount / 2.0 < target) low = middle + 1; else high = middle;
        }
        if (low > 0)
        {
            double Distance(int i) => Math.Abs(Windows[i].StartSample + Windows[i].SampleCount / 2.0 - target);
            if (Distance(low - 1) < Distance(low)) low--;
        }
        return low;
    }
}
