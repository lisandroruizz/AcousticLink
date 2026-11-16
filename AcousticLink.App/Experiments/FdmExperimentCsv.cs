using System.Globalization;
using System.IO;
using System.Text;

namespace AcousticLink.App.Experiments;

public static class FdmExperimentCsv
{
    // UTF-8 con BOM, ';' y coma decimal para Excel con configuración regional argentina.
    private static readonly CultureInfo Numbers = CultureInfo.GetCultureInfo("es-AR");

    public static void Save(string path, FdmExperimentResult result) => File.WriteAllText(path, Serialize(result), new UTF8Encoding(true));

    public static string Serialize(FdmExperimentResult result)
    {
        var rows = new List<string>();
        var header = new List<string> { "fecha_utc", "origen", "tasa_hz", "duracion_s", "canal", "mensaje_esperado", "hamming",
            "sincronizacion", "inicio_s", "cabecera_valida", "cabecera_bits", "referencia_original", "referencia_transmitida",
            "perfil_amplitud_declarado", "amplitud_a_declarada", "amplitud_b_declarada" };
        foreach (string prefix in new[] { "antes", "despues" })
            header.AddRange(new[] { "esperados", "comparados", "incorrectos", "no_concluyentes", "faltantes", "ber", "cobertura", "observados" }.Select(s => prefix + "_" + s));
        header.AddRange(new[] { "hamming_palabras", "hamming_sindromes_no_nulos", "hamming_inversiones", "hamming_palabras_con_interrogantes",
            "hamming_palabras_incompletas", "hamming_posiciones", "estado_recepcion", "crc", "diagnostico" });
        // Columnas nuevas al final: se conserva el orden de todas las columnas anteriores.
        header.AddRange(new[] { "recuperacion_borrados", "borrados_recuperados", "palabras_recuperadas_por_borrados", "palabras_irrecuperables", "modalidad" });
        rows.Add(Line(header));
        foreach (var channel in new[] { result.ChannelA, result.ChannelB })
        {
            var syncChannel = channel.Channel == "A" ? result.Synchronization.ChannelA : result.Synchronization.ChannelB;
            var fields = new List<string> { result.AnalyzedAt.ToUniversalTime().ToString("O"), CsvText.Safe(result.AudioSource), result.SampleRate.ToString(Numbers),
                Number(result.AudioDurationSeconds), channel.Channel, CsvText.Safe(channel.ExpectedMessage), result.UsesHamming ? "si" : "no",
                result.Synchronization.State.ToString(), Number(result.Synchronization.StartSeconds), channel.Reception.HeaderValid ? "si" : "no",
                syncChannel?.Header.Bits ?? "", channel.OriginalReferenceBits, channel.TransmittedReferenceBits };
            var declared = result.DeclaredAmplitudeProfile.HasValue
                ? Signals.FdmAmplitudeProfiles.Get(result.DeclaredAmplitudeProfile.Value) : null;
            fields.AddRange(new[] { CsvText.Safe(declared?.Name ?? "no_informado"), declared?.ChannelA.ToString("0.###", Numbers) ?? "",
                declared?.ChannelB.ToString("0.###", Numbers) ?? "" });
            AddComparison(fields, channel.Before, channel.TransmittedReferenceBits.Length);
            AddComparison(fields, channel.After, result.UsesHamming ? channel.OriginalReferenceBits.Length : null);
            var h = channel.Hamming;
            fields.AddRange(new[] { h?.AnalyzedWords.ToString(Numbers) ?? "", h?.NonzeroSyndromes.ToString(Numbers) ?? "",
                h?.AppliedBitFlips.ToString(Numbers) ?? "", h?.InconclusiveWords.ToString(Numbers) ?? "", h?.IncompleteWords.ToString(Numbers) ?? "",
                h is null ? "" : string.Join(" | ", h.Words.Where(w => w.FlippedPosition.HasValue).Select(w => $"palabra {w.WordIndex + 1}: {w.FlippedPosition}")),
                channel.Reception.State.ToString(), channel.Reception.CrcValid is true ? "valido" : channel.Reception.CrcValid is false ? "invalido" : "no_comprobado",
                CsvText.Safe(channel.Diagnostic) });
            fields.AddRange(new[] { result.ErasureRecoveryEnabled ? "si" : "no", h?.RecoveredErasures.ToString(Numbers) ?? "",
                h?.ErasureRecoveredWords.ToString(Numbers) ?? "", h?.UnrecoverableWords.ToString(Numbers) ?? "", "FDM" });
            rows.Add(Line(fields));
        }
        return string.Join("\r\n", rows) + "\r\n";
    }

    private static void AddComparison(List<string> fields, BitComparison? comparison, int? expected)
    {
        fields.Add(expected?.ToString(Numbers) ?? "");
        if (comparison is null) { fields.AddRange(Enumerable.Repeat("", 7)); return; }
        fields.AddRange(new[] { comparison.ComparedBits.ToString(Numbers), comparison.IncorrectBits.ToString(Numbers),
            comparison.InconclusiveBits.ToString(Numbers), comparison.MissingBits.ToString(Numbers), Number(comparison.Ber),
            Number(comparison.Coverage), comparison.ObservedBits });
    }
    private static string Number(double? value) => value?.ToString("G17", Numbers) ?? "";
    private static string Line(IEnumerable<string> fields) => string.Join(";", fields.Select(f => "\"" + f.Replace("\"", "\"\"") + "\""));
}
