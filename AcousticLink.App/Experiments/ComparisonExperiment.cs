using System.Diagnostics;
using System.Text;
using AcousticLink.App.Protocol;
using AcousticLink.App.Signals;

namespace AcousticLink.App.Experiments;

public enum ExperimentMode { Bfsk, Fdm }
public enum ExperimentSource { Generated, ImportedWav, Microphone }

public sealed record SpectrumPoint(double Hertz, double RelativeDb);

public sealed record ChannelMeasurement(string Channel, string ExpectedMessage, string? ReceivedMessage,
    FdmTextReceiveState State, bool HeaderDetected, bool? CrcValid, int UsefulBits, int HeaderBits,
    int ControlBits, int HammingBits, int PaddingBits, int TransmittedBits,
    int ComparedBits, int IncorrectBits, int InconclusiveBits, int MissingBits, double? BerBefore,
    double? BerAfter, int? HammingSyndromes, int? HammingFlips, int? HammingRecoveredErasures,
    string BerReason)
{
    public bool Delivered => State == FdmTextReceiveState.Valid && CrcValid == true;
    public bool MatchesReference => Delivered && ExpectedMessage.Length > 0 && ReceivedMessage == ExpectedMessage;
}

public sealed record ComparisonTrial(string Id, string? PairId, DateTimeOffset AtUtc, ExperimentMode Mode,
    ExperimentSource Source, string SourceDescription, bool UsesHamming, bool RecoversErasures,
    int SampleRate, double? TransmissionSeconds, double AnalyzedAudioSeconds, double ProcessingMilliseconds,
    double? NoiseSnrDb, int? NoiseSeed, double? CleanRms, double ObservedRms,
    string AmplitudeDescription, IReadOnlyList<ChannelMeasurement> Channels,
    IReadOnlyList<SpectrumPoint> Spectrum)
{
    public int ExpectedFrames => Channels.Count(c => c.ExpectedMessage.Length > 0);
    public int DetectedFrames => Channels.Count(c => c.HeaderDetected);
    public int ValidFrames => Channels.Count(c => c.Delivered);
    public int CrcRejectedFrames => Channels.Count(c => c.CrcValid == false);
    public int TruncatedFrames => Channels.Count(c => c.HeaderDetected && c.State == FdmTextReceiveState.Truncated);
    public int DeliveredUsefulBits => Channels.Where(c => c.MatchesReference).Sum(c => c.UsefulBits);
    public int IntendedUsefulBits => Channels.Sum(c => c.UsefulBits);
    public int TotalTransmittedBits => Channels.Sum(c => c.TransmittedBits);
    public double NominalAggregateBitsPerSecond => Mode == ExperimentMode.Bfsk ? 10 : 20;
    public double? EffectiveBitsPerSecond => TransmissionSeconds is > 0
        ? DeliveredUsefulBits / TransmissionSeconds.Value : null;
    public double? AcousticLatencyMilliseconds => null; // No hay marcas temporales comunes de emisión y recepción.
}

public sealed record ComparisonSummary(int Count, int ExpectedFrames, int DetectedFrames, int ValidFrames,
    int CrcRejectedFrames, int TruncatedFrames, double? MeanDurationSeconds, double? MeanEffectiveBitsPerSecond,
    double? AggregateBerBefore)
{
    public static ComparisonSummary From(IEnumerable<ComparisonTrial> trials)
    {
        var all = trials.ToArray();
        var durations = all.Where(t => t.TransmissionSeconds.HasValue).Select(t => t.TransmissionSeconds!.Value).ToArray();
        var rates = all.Where(t => t.EffectiveBitsPerSecond.HasValue).Select(t => t.EffectiveBitsPerSecond!.Value).ToArray();
        var compared = all.SelectMany(t => t.Channels).Where(c => c.BerBefore.HasValue).ToArray();
        int comparedBits = compared.Sum(c => c.ComparedBits);
        return new(all.Length, all.Sum(t => t.ExpectedFrames), all.Sum(t => t.DetectedFrames), all.Sum(t => t.ValidFrames),
            all.Sum(t => t.CrcRejectedFrames), all.Sum(t => t.TruncatedFrames),
            durations.Length > 0 ? durations.Average() : null, rates.Length > 0 ? rates.Average() : null,
            comparedBits > 0 ? compared.Sum(c => c.IncorrectBits) / (double)comparedBits : null);
    }
}

public static class ComparisonRules
{
    public static string Compatibility(ComparisonTrial a, ComparisonTrial b)
    {
        if (a.Mode == b.Mode) return "Seleccioná un ensayo BFSK y uno FDM.";
        if (a.Source != b.Source) return "Fuentes diferentes: no son directamente comparables.";
        if (a.Source != ExperimentSource.Generated) return "La equivalencia de señales WAV o capturas no está acreditada automáticamente.";
        if (a.PairId is null || a.PairId != b.PairId) return "Los ensayos no pertenecen al mismo par generado.";
        if (a.SampleRate != b.SampleRate || a.UsesHamming != b.UsesHamming || a.RecoversErasures != b.RecoversErasures)
            return "Tasa de muestreo o protección diferentes.";
        if (a.IntendedUsefulBits != b.IntendedUsefulBits ||
            !a.Channels.Select(c => c.ExpectedMessage).SequenceEqual(b.Channels.Select(c => c.ExpectedMessage)))
            return "Contenido útil o cantidad de bytes diferentes.";
        if (a.NoiseSnrDb != b.NoiseSnrDb || a.CleanRms != b.CleanRms)
            return "Condiciones de ruido o potencia diferentes.";
        return "Comparables: contenido, protección, tasa, potencia RMS objetivo y SNR configurados iguales. " +
            "Las señales conservan distinta estructura espectral y el ruido tiene realizaciones independientes.";
    }
}

public sealed class ComparisonExperiment
{
    public const double TargetRms = 0.05;

    public (ComparisonTrial Bfsk, ComparisonTrial Fdm) RunPair(string id, string a, string b,
        bool hamming, double? snrDb, int seed, FdmAmplitudeProfile profile, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Indicá un identificador.");
        if (snrDb is < 0 or > 60) throw new ArgumentOutOfRangeException(nameof(snrDb), "SNR entre 0 y 60 dB.");
        var bfskModem = new BfskTextModem();
        var fdmGenerator = new FdmBitstreamGenerator();
        var bfskFrames = new[] { bfskModem.Build(a, hamming), bfskModem.Build(b, hamming) };
        var fdmFrames = new FdmTextFrameBuilder().Build(a, b, hamming);
        // La señal BFSK se procesa por trama. Su duración agregada es la suma de ambas emisiones.
        var bfskSignals = new[] { bfskModem.Generate(a, hamming), bfskModem.Generate(b, hamming) };
        var fdmSignal = fdmGenerator.GenerateTextFrames(a, b, hamming, profile);
        for (int i = 0; i < 2; i++) Normalize(bfskSignals[i], TargetRms);
        Normalize(fdmSignal, TargetRms);
        var bfskObserved = bfskSignals.Select((s, i) => AddNoise(s, snrDb, seed + i, token)).ToArray();
        var fdmObserved = AddNoise(fdmSignal, snrDb, seed + 2, token);
        var watch = Stopwatch.StartNew();
        var channels = new List<ChannelMeasurement>();
        for (int i = 0; i < 2; i++)
        {
            token.ThrowIfCancellationRequested();
            channels.Add(AnalyzeBfskChannel(bfskObserved[i], BfskToneGenerator.SampleRate,
                i == 0 ? a : b, i == 0 ? "A" : "B (secuencial)", hamming, false, token));
        }
        watch.Stop();
        var combinedBfsk = bfskObserved.SelectMany(s => s).ToArray();
        var bfsk = new ComparisonTrial(id + "-BFSK", id, DateTimeOffset.UtcNow, ExperimentMode.Bfsk,
            ExperimentSource.Generated, "Señal digital generada y procesada; dos tramas BFSK secuenciales; semillas de ruido A=base, B=base+1",
            hamming, false, BfskToneGenerator.SampleRate,
            bfskSignals.Sum(s => s.Length) / (double)BfskToneGenerator.SampleRate,
            bfskObserved.Sum(s => s.Length) / (double)BfskToneGenerator.SampleRate, watch.Elapsed.TotalMilliseconds,
            snrDb, snrDb.HasValue ? seed : null, TargetRms, Rms(combinedBfsk),
            $"RMS digital limpio {TargetRms:0.###} por trama; amplitud escalada desde 0,15", channels.AsReadOnly(),
            Spectrum(combinedBfsk, BfskToneGenerator.SampleRate, token));
        watch.Restart();
        var fdmAnalysis = new FdmExperimentAnalyzer().Analyze(fdmObserved, FdmBitstreamGenerator.SampleRate,
            a, b, hamming, "Señal digital generada", token, profile);
        watch.Stop();
        var fdmChannels = new[] { ToChannel(fdmAnalysis.ChannelA, fdmFrames.ChannelA),
            ToChannel(fdmAnalysis.ChannelB, fdmFrames.ChannelB) };
        var fdm = new ComparisonTrial(id + "-FDM", id, DateTimeOffset.UtcNow, ExperimentMode.Fdm,
            ExperimentSource.Generated, "Señal digital generada y procesada; dos canales simultáneos",
            hamming, false, FdmBitstreamGenerator.SampleRate, fdmSignal.Length / (double)FdmBitstreamGenerator.SampleRate,
            fdmObserved.Length / (double)FdmBitstreamGenerator.SampleRate, watch.Elapsed.TotalMilliseconds,
            snrDb, snrDb.HasValue ? seed + 2 : null, TargetRms, Rms(fdmObserved),
            $"RMS digital limpio {TargetRms:0.###}; perfil {FdmAmplitudeProfiles.Get(profile).Label} escalado en conjunto",
            fdmChannels, Spectrum(fdmObserved, FdmBitstreamGenerator.SampleRate, token));
        return (bfsk, fdm);
    }

    public ComparisonTrial AnalyzeObserved(string id, float[] samples, int rate, ExperimentMode mode,
        ExperimentSource source, string sourceDescription, string expectedA, string expectedB,
        bool referencesKnown, bool hamming, bool recoverErasures, FdmAmplitudeProfile? profile,
        CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Indicá un identificador.");
        if (samples.Length == 0 || rate <= 0) throw new ArgumentException("Se requieren muestras y tasa válidas.");
        var watch = Stopwatch.StartNew();
        IReadOnlyList<ChannelMeasurement> channels;
        if (mode == ExperimentMode.Bfsk)
        {
            channels = [AnalyzeBfskChannel(samples, rate, referencesKnown ? expectedA : null, "A", hamming,
                recoverErasures, token)];
        }
        else if (referencesKnown)
        {
            var result = new FdmExperimentAnalyzer().Analyze(samples, rate, expectedA, expectedB, hamming,
                sourceDescription, token, profile, recoverErasures);
            var frames = new FdmTextFrameBuilder().Build(expectedA, expectedB, hamming);
            channels = [ToChannel(result.ChannelA, frames.ChannelA), ToChannel(result.ChannelB, frames.ChannelB)];
        }
        else
        {
            var received = new FdmTextReceiver().Receive(samples, rate, token, hamming, recoverErasures);
            channels = [WithoutReference("A", received.ChannelA), WithoutReference("B", received.ChannelB)];
        }
        watch.Stop();
        // La referencia declarada permite BER si hay alineación, pero no prueba la longitud ni
        // los bits efectivamente emitidos por una fuente externa.
        channels = channels.Select(c => c with { HeaderBits = 0, ControlBits = 0, HammingBits = 0,
            PaddingBits = 0, TransmittedBits = 0 }).ToArray();
        return new(id, null, DateTimeOffset.UtcNow, mode, source, sourceDescription, hamming, recoverErasures,
            rate, null, samples.Length / (double)rate, watch.Elapsed.TotalMilliseconds, null, null, null, Rms(samples),
            profile.HasValue && mode == ExperimentMode.Fdm ? "Perfil declarado: " + FdmAmplitudeProfiles.Get(profile.Value).Label :
                "Amplitud de emisión desconocida; RMS del audio observado disponible", channels,
            Spectrum(samples, rate, token));
    }

    private static ChannelMeasurement AnalyzeBfskChannel(float[] samples, int rate, string? expected,
        string channel, bool hamming, bool recover, CancellationToken token)
    {
        var result = new BfskTextReceiver().Receive(samples, rate, token, hamming, recover);
        if (expected is null) return WithoutReference(channel, result.Channel);
        var frame = new BfskTextModem().Build(expected, hamming);
        FdmChannelExperiment? measured = null;
        if (result.Channel.HeaderValid && result.Synchronization.DataStartSeconds is double start)
        {
            int available = (int)Math.Floor((samples.Length - start * rate + 0.000001) /
                (BfskSequenceDecoder.BitDurationSeconds * rate));
            int count = Math.Clamp(available, 0, frame.EncodedBlockBitCount);
            string raw = count > 0 ? new BfskSequenceDecoder().DecodeMessageBlock(samples, rate, start, count, token).Bits : "";
            measured = FdmExperimentAnalyzer.Evaluate(channel, expected, frame, raw, result.Channel,
                hamming, token, recover);
        }
        return ToChannel(measured, frame, channel, expected, result.Channel);
    }

    private static ChannelMeasurement ToChannel(FdmChannelExperiment measured, FdmTextChannelFrame frame) =>
        ToChannel(measured, frame, measured.Channel, measured.ExpectedMessage, measured.Reception);

    private static ChannelMeasurement ToChannel(FdmChannelExperiment? measured, FdmTextChannelFrame frame,
        string channel, string expected, FdmTextChannelResult reception)
    {
        int control = frame.OriginalBlockBitCount - frame.Utf8ByteCount * 8;
        var h = measured?.Hamming;
        return new(channel, expected, reception.Text, reception.State, reception.HeaderValid, reception.CrcValid,
            frame.Utf8ByteCount * 8, frame.HeaderBitCount, control,
            frame.EncodedBlockBitCount - frame.OriginalBlockBitCount, frame.PaddingBitCount, frame.TransmittedBits.Length,
            measured?.Before?.ComparedBits ?? 0, measured?.Before?.IncorrectBits ?? 0,
            measured?.Before?.InconclusiveBits ?? 0, measured?.Before?.MissingBits ?? frame.EncodedBlockBitCount,
            measured?.Before?.Ber, measured?.After?.Ber, h?.NonzeroSyndromes, h?.AppliedBitFlips,
            h?.RecoveredErasures, measured?.Diagnostic ?? "No disponible: cabecera sin alineación validada.");
    }

    private static ChannelMeasurement WithoutReference(string channel, FdmTextChannelResult received) =>
        new(channel, "", received.Text, received.State, received.HeaderValid, received.CrcValid,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, null, null,
            received.HammingDiagnostics?.Words.Count(w => w.HasSyndrome && w.Syndrome != 0),
            received.HammingDiagnostics?.Words.Count(w => w.FlippedPosition.HasValue),
            received.HammingDiagnostics?.Words.Sum(w => w.RecoveredErasureCount),
            "No disponible: no se declaró una referencia transmitida; CRC no mide BER.");

    public static double Rms(ReadOnlySpan<float> samples)
    {
        if (samples.Length == 0) return 0;
        double sum = 0;
        foreach (float sample in samples) sum += (double)sample * sample;
        return Math.Sqrt(sum / samples.Length);
    }

    private static void Normalize(float[] samples, double target)
    {
        double rms = Rms(samples);
        if (rms <= 0) throw new InvalidOperationException("La señal digital carece de energía.");
        double scale = target / rms;
        for (int i = 0; i < samples.Length; i++) samples[i] = (float)(samples[i] * scale);
    }

    private static float[] AddNoise(float[] clean, double? snr, int seed, CancellationToken token)
    {
        var observed = (float[])clean.Clone();
        if (!snr.HasValue) return observed;
        double sigma = Rms(clean) / Math.Pow(10, snr.Value / 20);
        var random = new Random(seed);
        for (int i = 0; i < observed.Length; i += 2)
        {
            token.ThrowIfCancellationRequested();
            double u1 = Math.Max(double.Epsilon, random.NextDouble());
            double angle = 2 * Math.PI * random.NextDouble();
            double radius = Math.Sqrt(-2 * Math.Log(u1)) * sigma;
            observed[i] += (float)(radius * Math.Cos(angle));
            if (i + 1 < observed.Length) observed[i + 1] += (float)(radius * Math.Sin(angle));
        }
        return observed;
    }

    // Hann de 4096 muestras; cuatro ventanas distribuidas en la señal. Resolución = tasa/4096 Hz.
    // Se muestra energía relativa, no ancho de banda ocupado ni potencia acústica calibrada.
    public static IReadOnlyList<SpectrumPoint> Spectrum(float[] samples, int rate, CancellationToken token = default)
    {
        const int size = 4096;
        if (samples.Length < size || Rms(samples) == 0) return Array.Empty<SpectrumPoint>();
        int[] starts = Enumerable.Range(0, 4).Select(i => (samples.Length - size) * i / 3).Distinct().ToArray();
        int maxBin = Math.Min(size / 2, (int)Math.Floor(4200.0 * size / rate));
        var powers = new double[maxBin + 1];
        for (int k = 0; k <= maxBin; k++)
        {
            token.ThrowIfCancellationRequested();
            foreach (int start in starts)
            {
                double re = 0, im = 0;
                for (int n = 0; n < size; n++)
                {
                    double value = samples[start + n] * (0.5 - 0.5 * Math.Cos(2 * Math.PI * n / (size - 1)));
                    double phase = 2 * Math.PI * k * n / size;
                    re += value * Math.Cos(phase); im -= value * Math.Sin(phase);
                }
                powers[k] += (re * re + im * im) / starts.Length;
            }
        }
        double maximum = powers.Max();
        if (maximum <= 0) return Array.Empty<SpectrumPoint>();
        return Enumerable.Range(0, powers.Length).Select(k => new SpectrumPoint(k * rate / (double)size,
            10 * Math.Log10(Math.Max(powers[k] / maximum, 1e-8)))).ToArray();
    }
}
