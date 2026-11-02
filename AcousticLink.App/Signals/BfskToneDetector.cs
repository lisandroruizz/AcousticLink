namespace AcousticLink.App.Signals;

public sealed record BfskDetectionResult(double Energy0, double Energy1, int? Bit, string Reason);

public sealed class BfskToneDetector
{
    // Umbrales de partida para muestras normalizadas; requieren calibración física.
    public double MinimumRms { get; }
    public double MinimumEnergyRatio { get; }
    public double MinimumToneFraction { get; }
    public double MinimumDurationSeconds { get; }

    public BfskToneDetector(double minimumRms = 0.003, double minimumEnergyRatio = 4,
        double minimumToneFraction = 0.5, double minimumDurationSeconds = 0.04)
    {
        if (!double.IsFinite(minimumRms) || minimumRms <= 0 ||
            !double.IsFinite(minimumEnergyRatio) || minimumEnergyRatio <= 1 ||
            !double.IsFinite(minimumToneFraction) || minimumToneFraction <= 0 || minimumToneFraction > 1 ||
            !double.IsFinite(minimumDurationSeconds) || minimumDurationSeconds < 0.01)
            throw new ArgumentException("Umbrales del detector inválidos.");
        MinimumRms = minimumRms;
        MinimumEnergyRatio = minimumEnergyRatio;
        MinimumToneFraction = minimumToneFraction;
        MinimumDurationSeconds = minimumDurationSeconds;
    }

    public BfskDetectionResult Analyze(ReadOnlySpan<float> samples, int sampleRate)
    {
        if (sampleRate <= 2 * BfskToneGenerator.Bit1Frequency)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), "La tasa debe superar el doble de 1900 Hz.");
        double mean = 0;
        foreach (float value in samples)
        {
            if (!float.IsFinite(value)) throw new ArgumentException("Las muestras deben ser finitas.", nameof(samples));
            mean += value;
        }
        if (samples.Length / (double)sampleRate < MinimumDurationSeconds)
            return new(0, 0, null, "Fragmento insuficiente.");
        mean /= samples.Length;
        double power = 0;
        foreach (float value in samples) power += (value - mean) * (value - mean);
        power /= samples.Length;

        double energy0 = Goertzel(samples, mean, sampleRate, BfskToneGenerator.Bit0Frequency);
        double energy1 = Goertzel(samples, mean, sampleRate, BfskToneGenerator.Bit1Frequency);
        if (Math.Sqrt(power) < MinimumRms)
            return new(energy0, energy1, null, "Señal demasiado débil o silencio.");
        double strongest = Math.Max(energy0, energy1);
        double weakest = Math.Min(energy0, energy1);
        if (strongest < MinimumEnergyRatio * weakest)
            return new(energy0, energy1, null, "Ambigua: energías similares en ambos tonos.");
        if (strongest < MinimumToneFraction * power)
            return new(energy0, energy1, null, "No hay suficiente energía concentrada en un tono BFSK.");
        return new(energy0, energy1, energy0 > energy1 ? 0 : 1, "Tono BFSK identificado.");
    }

    private static double Goertzel(ReadOnlySpan<float> samples, double mean, int sampleRate, double frequency)
    {
        // Frecuencia nominal exacta, sin redondearla a un bin de FFT.
        double coefficient = 2 * Math.Cos(2 * Math.PI * frequency / sampleRate);
        double previous = 0, previous2 = 0;
        foreach (float sample in samples)
        {
            double current = sample - mean + coefficient * previous - previous2;
            previous2 = previous;
            previous = current;
        }
        double magnitudeSquared = Math.Max(0, previous * previous + previous2 * previous2 - coefficient * previous * previous2);
        // Potencia media estimada: un seno de amplitud A produce aproximadamente A²/2.
        return 2 * magnitudeSquared / ((double)samples.Length * samples.Length);
    }
}
