namespace AcousticLink.App.Signals;

public sealed record FdmDetectionResult(BfskDetectionResult ChannelA, BfskDetectionResult ChannelB);
public sealed record FdmDecisionDiagnostics(double DurationSeconds, double MinimumDurationSeconds,
    double MinimumToneEnergy, double MinimumEnergyRatio, bool DurationAccepted, bool? EnergyAccepted, bool? RatioAccepted);

public sealed class FdmToneDetector
{
    // Potencia normalizada A²/2: 9e-6 equivale a un seno de amplitud aproximada 0,00424.
    // Cada banda se evalúa sola, sin exigir una fracción de la potencia de la otra banda.
    // Umbrales iniciales de laboratorio: ajustar tras medir ruido y respuesta del hardware.
    public double MinimumToneEnergy { get; }
    public double MinimumEnergyRatio { get; }
    public double MinimumDurationSeconds { get; }

    public FdmToneDetector(double minimumToneEnergy = 0.000009, double minimumEnergyRatio = 4,
        double minimumDurationSeconds = 0.04)
    {
        if (!double.IsFinite(minimumToneEnergy) || minimumToneEnergy <= 0 ||
            !double.IsFinite(minimumEnergyRatio) || minimumEnergyRatio <= 1 ||
            !double.IsFinite(minimumDurationSeconds) || minimumDurationSeconds < 0.01)
            throw new ArgumentException("Umbrales FDM inválidos.");
        MinimumToneEnergy = minimumToneEnergy;
        MinimumEnergyRatio = minimumEnergyRatio;
        MinimumDurationSeconds = minimumDurationSeconds;
    }

    public FdmDetectionResult Analyze(ReadOnlySpan<float> samples, int sampleRate)
    {
        if (sampleRate <= 2 * FdmToneMixer.ChannelBBit1Frequency)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), "La tasa debe superar el doble de 3700 Hz.");
        double mean = 0;
        foreach (float sample in samples)
        {
            if (!float.IsFinite(sample)) throw new ArgumentException("Las muestras deben ser finitas.", nameof(samples));
            mean += sample;
        }
        if (samples.Length / (double)sampleRate < MinimumDurationSeconds)
        {
            var insufficient = new BfskDetectionResult(0, 0, null, "Fragmento insuficiente.")
            { FdmDiagnostics = new(samples.Length / (double)sampleRate, MinimumDurationSeconds, MinimumToneEnergy, MinimumEnergyRatio, false, null, null) };
            return new(insufficient, insufficient);
        }
        mean /= samples.Length;
        double a0 = GoertzelEnergy.Estimate(samples, mean, sampleRate, FdmToneMixer.ChannelABit0Frequency);
        double a1 = GoertzelEnergy.Estimate(samples, mean, sampleRate, FdmToneMixer.ChannelABit1Frequency);
        double b0 = GoertzelEnergy.Estimate(samples, mean, sampleRate, FdmToneMixer.ChannelBBit0Frequency);
        double b1 = GoertzelEnergy.Estimate(samples, mean, sampleRate, FdmToneMixer.ChannelBBit1Frequency);
        return new(Decide(a0, a1, samples.Length / (double)sampleRate), Decide(b0, b1, samples.Length / (double)sampleRate));
    }

    private BfskDetectionResult Decide(double energy0, double energy1, double duration)
    {
        double strongest = Math.Max(energy0, energy1);
        double weakest = Math.Min(energy0, energy1);
        var diagnostics = new FdmDecisionDiagnostics(duration, MinimumDurationSeconds, MinimumToneEnergy, MinimumEnergyRatio,
            true, strongest >= MinimumToneEnergy, strongest < MinimumToneEnergy ? null : strongest >= MinimumEnergyRatio * weakest);
        BfskDetectionResult Result(int? bit, string reason) => new(energy0, energy1, bit, reason) { FdmDiagnostics = diagnostics };
        if (strongest < MinimumToneEnergy)
            return Result(null, "Canal ausente o demasiado débil.");
        if (strongest < MinimumEnergyRatio * weakest)
            return Result(null, "Ambigua: energías similares dentro del canal.");
        return Result(energy0 > energy1 ? 0 : 1, "Tono identificado en este canal.");
    }
}
