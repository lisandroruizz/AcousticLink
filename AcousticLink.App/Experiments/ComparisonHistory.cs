using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AcousticLink.App.Experiments;

public static class ComparisonHistory
{
    public static void Save(string path, IReadOnlyList<ComparisonTrial> trials) =>
        File.WriteAllText(path, JsonSerializer.Serialize(trials.ToList(), ComparisonHistoryJsonContext.Default.ListComparisonTrial), new UTF8Encoding(false));

    public static IReadOnlyList<ComparisonTrial> Load(string path)
    {
        var trials = JsonSerializer.Deserialize(File.ReadAllText(path, Encoding.UTF8), ComparisonHistoryJsonContext.Default.ListComparisonTrial)
            ?? throw new InvalidDataException("Historial vacío o inválido.");
        if (trials.Any(t => string.IsNullOrWhiteSpace(t.Id) || t.SampleRate <= 0 || t.Channels is null || t.Spectrum is null))
            throw new InvalidDataException("El historial contiene un ensayo incompleto.");
        return trials;
    }

    // Formato nuevo; FdmExperimentCsv mantiene íntegro el esquema anterior de medición por canal.
    public static string ToCsv(IEnumerable<ComparisonTrial> trials)
    {
        var rows = new List<string>
        {
            Line("tipo_fila", "id", "par_id", "fecha_utc", "modalidad", "fuente", "origen", "canal",
                "mensaje_esperado", "mensaje_recibido", "hamming", "recuperacion_borrados", "tasa_hz",
                "duracion_transmision_s", "duracion_audio_analizado_s", "procesamiento_ms", "snr_configurada_db",
                "semilla", "rms_limpio_objetivo", "rms_observado", "amplitud", "bits_utiles_esperados",
                "bits_utiles_entregados", "bits_cabecera", "bits_control", "bits_hamming", "bits_relleno",
                "bits_transmitidos", "velocidad_nominal_bps", "rendimiento_efectivo_bps",
                "tramas_esperadas", "tramas_detectadas", "tramas_validas", "crc_rechazados", "truncadas",
                "estado", "crc", "ber_previa", "ber_posterior", "bits_comparados", "bits_incorrectos",
                "bits_inconclusos", "bits_faltantes", "sindromes_no_nulos", "inversiones_intentadas",
                "borrados_recuperados", "motivo_ber", "latencia_ms", "motivo_latencia")
        };
        foreach (var trial in trials)
        {
            foreach (var channel in trial.Channels)
                rows.Add(Row(trial, channel, false));
            rows.Add(Row(trial, null, true));
        }
        return string.Join("\r\n", rows) + "\r\n";
    }

    public static void SaveCsv(string path, IEnumerable<ComparisonTrial> trials) =>
        File.WriteAllText(path, ToCsv(trials), new UTF8Encoding(true));

    private static string Row(ComparisonTrial t, ChannelMeasurement? c, bool total)
    {
        string N(double? v) => v?.ToString("G17", CultureInfo.InvariantCulture) ?? "";
        string I(int? v) => v?.ToString(CultureInfo.InvariantCulture) ?? "";
        int usefulExpected = total ? t.IntendedUsefulBits : c!.UsefulBits;
        int usefulDelivered = total ? t.DeliveredUsefulBits : c!.MatchesReference ? c.UsefulBits : 0;
        int header = total ? t.Channels.Sum(x => x.HeaderBits) : c!.HeaderBits;
        int control = total ? t.Channels.Sum(x => x.ControlBits) : c!.ControlBits;
        int hamming = total ? t.Channels.Sum(x => x.HammingBits) : c!.HammingBits;
        int padding = total ? t.Channels.Sum(x => x.PaddingBits) : c!.PaddingBits;
        int bits = total ? t.TotalTransmittedBits : c!.TransmittedBits;
        double? channelSeconds = t.TransmissionSeconds;
        if (!total && t.Mode == ExperimentMode.Bfsk && t.Source == ExperimentSource.Generated)
            channelSeconds = c!.TransmittedBits * Signals.BfskToneGenerator.DefaultBitDurationSeconds;
        double? channelRate = channelSeconds is > 0 ? usefulDelivered / channelSeconds.Value : null;
        return Line(total ? "total" : "canal", t.Id, t.PairId ?? "", t.AtUtc.ToUniversalTime().ToString("O"),
            t.Mode.ToString(), t.Source.ToString(), t.SourceDescription, total ? "TOTAL" : c!.Channel,
            total ? "" : c!.ExpectedMessage, total ? "" : c!.ReceivedMessage ?? "", t.UsesHamming ? "si" : "no",
            t.RecoversErasures ? "si" : "no", I(t.SampleRate), N(channelSeconds), N(t.AnalyzedAudioSeconds),
            N(t.ProcessingMilliseconds), N(t.NoiseSnrDb), I(t.NoiseSeed), N(t.CleanRms), N(t.ObservedRms),
            t.AmplitudeDescription, I(total ? t.ExpectedFrames > 0 ? usefulExpected : null : c!.ExpectedMessage.Length > 0 ? usefulExpected : null),
            I(total ? t.ExpectedFrames > 0 ? usefulDelivered : null : c!.ExpectedMessage.Length > 0 ? usefulDelivered : null), I(bits > 0 ? header : null),
            I(bits > 0 ? control : null), I(bits > 0 ? hamming : null), I(bits > 0 ? padding : null),
            I(bits > 0 ? bits : null), N(total ? t.NominalAggregateBitsPerSecond : 10), N(channelRate),
            I(total ? t.ExpectedFrames > 0 ? t.ExpectedFrames : null : c!.ExpectedMessage.Length > 0 ? 1 : null), I(total ? t.DetectedFrames : c!.HeaderDetected ? 1 : 0),
            I(total ? t.ValidFrames : c!.Delivered ? 1 : 0), I(total ? t.CrcRejectedFrames : c!.CrcValid == false ? 1 : 0),
            I(total ? t.TruncatedFrames : c!.HeaderDetected && c.State == Protocol.FdmTextReceiveState.Truncated ? 1 : 0),
            total ? "" : c!.State.ToString(), total ? "" : c!.CrcValid?.ToString() ?? "no_comprobado",
            total ? "" : N(c!.BerBefore), total ? "" : N(c!.BerAfter), total ? "" : I(c!.ComparedBits),
            total ? "" : I(c!.IncorrectBits), total ? "" : I(c!.InconclusiveBits), total ? "" : I(c!.MissingBits),
            total ? "" : I(c!.HammingSyndromes), total ? "" : I(c!.HammingFlips),
            total ? "" : I(c!.HammingRecoveredErasures), total ? "" : c!.BerReason, "",
            "No disponible: no hay marcas temporales comunes entre emisión y recepción.");
    }

    private static string Line(params string?[] fields) =>
        string.Join(";", fields.Select(f => "\"" + (f ?? "").Replace("\"", "\"\"") + "\""));
}

[JsonSerializable(typeof(List<ComparisonTrial>))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class ComparisonHistoryJsonContext : JsonSerializerContext { }
