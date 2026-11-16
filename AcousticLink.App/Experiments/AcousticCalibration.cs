using AcousticLink.App.Signals;

namespace AcousticLink.App.Experiments;

public enum CalibrationMode { Bfsk, Fdm }
public sealed record CalibrationLevel(double Rms, double Dbfs, double Peak, double DurationSeconds, int SampleRate, bool PossibleClipping);
public sealed record CalibrationNoise(CalibrationMode Mode, string DeviceDescription, string Source, DateTimeOffset AtUtc,
    CalibrationLevel Level, IReadOnlyList<double> BandPowers);
public sealed record CalibrationTone(double Frequency, double NoisePower, double ReceivedPower, double? SnrDb,
    string Status);
public sealed record CalibrationResult(CalibrationMode Mode, string Source, CalibrationLevel Level, double? StartSeconds,
    IReadOnlyList<CalibrationTone> Tones, string Status, IReadOnlyList<string> Recommendations);

public static class AcousticCalibration
{
    public const double ToneSeconds = 0.8;
    public const double GapSeconds = 0.2;
    public const double TestAmplitude = 0.15;
    private const double WindowSeconds = 0.1;
    public static double[] Frequencies(CalibrationMode mode) => mode == CalibrationMode.Bfsk
        ? [BfskToneGenerator.Bit0Frequency, BfskToneGenerator.Bit1Frequency]
        : [FdmToneMixer.ChannelABit0Frequency, FdmToneMixer.ChannelABit1Frequency,
           FdmToneMixer.ChannelBBit0Frequency, FdmToneMixer.ChannelBBit1Frequency];

    public static float[] Generate(CalibrationMode mode)
    {
        var frequencies = Frequencies(mode);
        int toneLength = (int)(ToneSeconds * BfskToneGenerator.SampleRate);
        int gapLength = (int)(GapSeconds * BfskToneGenerator.SampleRate);
        var samples = new float[frequencies.Length * (toneLength + gapLength)];
        var generator = new BfskToneGenerator();
        for (int i = 0; i < frequencies.Length; i++)
            generator.GenerateTone(frequencies[i], ToneSeconds, TestAmplitude).CopyTo(samples, i * (toneLength + gapLength));
        return samples;
    }

    public static CalibrationLevel Level(ReadOnlySpan<float> samples, int rate)
    {
        if (rate <= 7400 || samples.Length == 0) throw new ArgumentException("Audio vacío o tasa insuficiente.");
        double sum = 0, peak = 0; int clipped = 0;
        foreach (float sample in samples)
        {
            if (!float.IsFinite(sample)) throw new ArgumentException("El audio contiene muestras no finitas.");
            sum += (double)sample * sample;
            peak = Math.Max(peak, Math.Abs((double)sample));
            if (Math.Abs(sample) >= 0.999) clipped++;
        }
        double rms = Math.Sqrt(sum / samples.Length);
        return new(rms, rms > 0 ? 20 * Math.Log10(rms) : double.NegativeInfinity, peak,
            samples.Length / (double)rate, rate, clipped >= Math.Max(2, samples.Length / 1000));
    }

    private static double Power(ReadOnlySpan<float> samples, int rate, double frequency)
    {
        double mean = 0;
        foreach (float sample in samples) mean += sample;
        mean /= samples.Length;
        return GoertzelEnergy.Estimate(samples, mean, rate, frequency);
    }

    public static CalibrationNoise MeasureNoise(float[] samples, int rate, CalibrationMode mode,
        string device, string source, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var level = Level(samples, rate);
        if (level.DurationSeconds < 1) throw new ArgumentException("La referencia requiere al menos un segundo de muestras reales.");
        int window = (int)Math.Round(rate * WindowSeconds);
        var frequencies = Frequencies(mode);
        var powers = new double[frequencies.Length];
        int count = samples.Length / window;
        for (int w = 0; w < count; w++)
        {
            token.ThrowIfCancellationRequested();
            var span = samples.AsSpan(w * window, window);
            for (int f = 0; f < powers.Length; f++) powers[f] += Power(span, rate, frequencies[f]);
        }
        for (int f = 0; f < powers.Length; f++) powers[f] /= count;
        return new(mode, device, source, DateTimeOffset.UtcNow, level, powers);
    }

    public static CalibrationResult Analyze(float[] samples, int rate, CalibrationMode mode, CalibrationNoise noise,
        string source, CancellationToken token = default, bool comparableReference = true)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(noise);
        var level = Level(samples, rate);
        if (mode != noise.Mode || rate != noise.Level.SampleRate)
            throw new ArgumentException("La modalidad y la tasa deben coincidir con la referencia de ruido.");
        double[] frequencies = Frequencies(mode);
        int window = (int)Math.Round(rate * WindowSeconds);
        int toneWindows = (int)Math.Round(ToneSeconds / WindowSeconds);
        int stride = (int)Math.Round((ToneSeconds + GapSeconds) / WindowSeconds);
        int total = samples.Length / window;
        if (total < (frequencies.Length - 1) * stride + toneWindows)
            return new(mode, source, level, null, [], "Medición inconclusa: captura demasiado corta para la secuencia.",
                ["Capture la secuencia completa y repita el ensayo."]);
        var powers = new double[frequencies.Length, total];
        for (int w = 0; w < total; w++)
        {
            token.ThrowIfCancellationRequested();
            var span = samples.AsSpan(w * window, window);
            for (int f = 0; f < frequencies.Length; f++) powers[f, w] = Power(span, rate, frequencies[f]);
        }
        // Se omiten los extremos de cada tono: la envolvente, los silencios y la transición no entran en la estimación.
        int gapWindows = stride - toneWindows;
        int latest = total - frequencies.Length * stride;
        if (latest < 0)
            return new(mode, source, level, null, [], "Medición inconclusa: falta el silencio final de la secuencia.",
                ["Capture la secuencia completa y repita el ensayo."]);
        double bestScore = double.NegativeInfinity; int bestStart = -1;
        for (int start = 1; start <= latest; start++)
        {
            token.ThrowIfCancellationRequested();
            double score = double.PositiveInfinity;
            double firstTone = SegmentPower(powers, 0, start + 1, toneWindows - 2);
            score = Math.Min(score, firstTone / Math.Max(powers[0, start - 1], 1e-12));
            for (int f = 0; f < frequencies.Length; f++)
            {
                double target = SegmentPower(powers, f, start + f * stride + 1, toneWindows - 2);
                double rival = 0;
                for (int other = 0; other < frequencies.Length; other++)
                    if (other != f) rival = Math.Max(rival, SegmentPower(powers, other, start + f * stride + 1, toneWindows - 2));
                double floor = Math.Max(noise.BandPowers[f], 1e-12);
                score = Math.Min(score, Math.Min(target / floor, target / Math.Max(rival, 1e-12)));
                double gap = SegmentPower(powers, f, start + f * stride + toneWindows, gapWindows);
                score = Math.Min(score, target / Math.Max(gap, 1e-12));
            }
            if (score > bestScore) { bestScore = score; bestStart = start; }
        }
        if (bestStart < 0 || bestScore < 4)
            return new(mode, source, level, null, [], "Medición inconclusa: no se identificó la secuencia completa con contraste suficiente.",
                ["Revise el volumen, acerque y oriente los equipos; reduzca el ruido y repita."]);
        var tones = new List<CalibrationTone>();
        for (int f = 0; f < frequencies.Length; f++)
        {
            double received = SegmentPower(powers, f, bestStart + f * stride + 1, toneWindows - 2);
            double background = noise.BandPowers[f];
            double signal = received - background;
            double? snr = comparableReference && !noise.Level.PossibleClipping && !level.PossibleClipping &&
                background > 0 && signal > 0 ? 10 * Math.Log10(signal / background) : null;
            string reason = !comparableReference ? "SNR no disponible: no se comprobó que el WAV use el mismo micrófono y ganancia"
                : noise.Level.PossibleClipping || level.PossibleClipping ? "SNR no disponible: posible clipping"
                : "SNR no disponible: referencia nula o sin exceso atribuible a la señal";
            tones.Add(new(frequencies[f], background, received, snr, snr.HasValue ? "Estimación disponible" : reason));
        }
        var advice = new List<string>();
        if (noise.Level.PossibleClipping || level.PossibleClipping) advice.Add("Posible saturación: reduzca la ganancia del micrófono o el volumen de salida.");
        if (level.Rms < 0.003) advice.Add("Nivel digital bajo: acerque los dispositivos, oriente el micrófono y revise el volumen.");
        double min = tones.Min(t => t.ReceivedPower), max = tones.Max(t => t.ReceivedPower);
        if (min > 0 && 10 * Math.Log10(max / min) >= 10)
            advice.Add("Diferencia orientativa de 10 dB o más entre tonos: revise orientación y respuesta de parlante/micrófono.");
        if (tones.Any(t => t.SnrDb is < 6)) advice.Add("Contraste bajo en alguna banda: reduzca fuentes de ruido y repita la referencia.");
        if (advice.Count == 0) advice.Add("No se observan advertencias iniciales; compruebe la recepción con una trama y CRC.");
        return new(mode, source, level, bestStart * WindowSeconds, tones, "Secuencia conocida localizada; evaluación orientativa, sin validar mensajes.", advice);
    }

    private static double SegmentPower(double[,] powers, int frequency, int first, int count)
    {
        double sum = 0;
        for (int i = first; i < first + count; i++) sum += powers[frequency, i];
        return sum / count;
    }
}
