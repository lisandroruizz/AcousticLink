namespace AcousticLink.App.Visualization;

// Resumen de presentación acotado; no conserva otra copia de la grabación completa.
public sealed class IncrementalWaveform
{
    private readonly WaveformBucket[] buckets;
    public int SampleCapacity { get; }
    public int SamplesPerBucket { get; }
    public int SampleCount { get; private set; }
    public int BucketCount => (SampleCount + SamplesPerBucket - 1) / SamplesPerBucket;
    public double DisplayFraction => Math.Min(1, BucketCount * (double)SamplesPerBucket / SampleCapacity);

    public IncrementalWaveform(int sampleCapacity, int maximumBuckets = 1200)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBuckets);
        SampleCapacity = sampleCapacity;
        SamplesPerBucket = (int)Math.Ceiling(sampleCapacity / (double)maximumBuckets);
        buckets = new WaveformBucket[(int)Math.Ceiling(sampleCapacity / (double)SamplesPerBucket)];
    }

    public void Append(ReadOnlySpan<float> samples)
    {
        if (samples.Length > SampleCapacity - SampleCount) throw new ArgumentException("Se supera la duración configurada.");
        foreach (float value in samples)
        {
            if (!float.IsFinite(value)) throw new ArgumentException("Muestras no finitas.");
            int index = SampleCount / SamplesPerBucket;
            buckets[index] = SampleCount % SamplesPerBucket == 0 ? new(value, value)
                : new(Math.Min(buckets[index].Minimum, value), Math.Max(buckets[index].Maximum, value));
            SampleCount++;
        }
    }

    public WaveformBucket[] Snapshot() => buckets[..BucketCount];
}
