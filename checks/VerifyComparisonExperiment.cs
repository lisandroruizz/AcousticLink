#:property TargetFramework=net10.0-windows
#:project ../AcousticLink.App/AcousticLink.App.csproj

using System.IO;
using Microsoft.VisualBasic.FileIO;
using AcousticLink.App.Experiments;
using AcousticLink.App.Signals;

static void Require(bool ok, string name)
{
    if (!ok) throw new Exception(name);
}

var equal = FdmExperimentAnalyzer.Compare("0101", "0101");
var oneWrong = FdmExperimentAnalyzer.Compare("0101", "0111");
Require(equal.Ber == 0 && oneWrong.Ber == 0.25, "BER con alineación conocida");
Require(FdmExperimentAnalyzer.Compare("0101", "????").Ber is null, "Sin bits comparables");
try { _ = FdmExperimentAnalyzer.Compare("0101", "01010"); throw new Exception("Longitud no rechazada"); }
catch (ArgumentException) { }

var engine = new ComparisonExperiment();
var (bfsk, fdm) = engine.RunPair("par-ñ", "ñ", "Mundo", false, null, 42, FdmAmplitudeProfile.Original);
Require(bfsk.Source == ExperimentSource.Generated && fdm.Source == ExperimentSource.Generated, "Fuentes sintéticas");
Require(bfsk.Channels.Count == 2 && fdm.Channels.Count == 2, "Canales y tramas");
Require(bfsk.IntendedUsefulBits == fdm.IntendedUsefulBits && bfsk.IntendedUsefulBits == 56, "UTF-8 y volumen útil");
Require(Math.Abs(bfsk.TransmissionSeconds!.Value - bfsk.Channels.Sum(c => c.TransmittedBits) * 0.1) < 1e-9, "Duración BFSK");
Require(Math.Abs(fdm.TransmissionSeconds!.Value - fdm.Channels.Max(c => c.TransmittedBits) * 0.1) < 1e-9, "Duración FDM simultánea");
Require(bfsk.ValidFrames == 2 && fdm.ValidFrames == 2, "CRC y texto validado");
Require(bfsk.EffectiveBitsPerSecond == 56 / bfsk.TransmissionSeconds &&
    fdm.EffectiveBitsPerSecond == 56 / fdm.TransmissionSeconds, "Rendimiento útil agregado");
Require(bfsk.Channels.All(c => c.BerBefore == 0) && fdm.Channels.All(c => c.BerBefore == 0), "BER cero observada");
Require(bfsk.Channels.All(c => c.HammingBits == 0) && fdm.Channels.All(c => c.HammingBits == 0), "Sin Hamming");
Require(ComparisonRules.Compatibility(bfsk, fdm).StartsWith("Comparables"), "Par equivalente");
Require(bfsk.Spectrum.Count > 0 && fdm.Spectrum.Count > 0, "Espectro del audio procesado");
var (protectedBfsk, protectedFdm) = engine.RunPair("protegido", "Hola", "Mundo", true, 30, 7,
    FdmAmplitudeProfile.PrioritizeChannelA);
Require(protectedBfsk.Channels.All(c => c.HammingBits > 0) && protectedFdm.Channels.All(c => c.HammingBits > 0) &&
    protectedBfsk.NoiseSeed == 7 && protectedFdm.NoiseSeed == 9 && protectedFdm.NoiseSnrDb == 30,
    "Hamming, perfil y ruido registrados");
Require(ComparisonRules.Compatibility(protectedBfsk, protectedFdm).StartsWith("Comparables"), "Par protegido equivalente");
var wavKnown = engine.AnalyzeObserved("wav-con-referencia", new AcousticLink.App.Protocol.BfskTextModem().Generate("Hola"),
    48000, ExperimentMode.Bfsk, ExperimentSource.ImportedWav, "WAV digital de prueba", "Hola", "", true,
    false, false, null);
Require(wavKnown.Channels[0].BerBefore == 0 && wavKnown.ValidFrames == 1 &&
    wavKnown.TransmissionSeconds is null && wavKnown.Channels[0].TransmittedBits == 0,
    "WAV con referencia: BER disponible, emisión no probada");

var summary = ComparisonSummary.From([bfsk, fdm]);
Require(summary.Count == 2 && summary.ExpectedFrames == 4 && summary.ValidFrames == 4 &&
    summary.CrcRejectedFrames == 0 && summary.MeanDurationSeconds.HasValue, "Resumen agregado");
var unknown = engine.AnalyzeObserved("wav", new float[48000], 48000, ExperimentMode.Bfsk,
    ExperimentSource.ImportedWav, "WAV de prueba", "", "", false, false, false, null);
Require(unknown.TransmissionSeconds is null && unknown.EffectiveBitsPerSecond is null &&
    unknown.Channels[0].BerBefore is null && unknown.CrcRejectedFrames == 0 && unknown.ValidFrames == 0,
    "Métricas no disponibles y trama no detectada");
Require(ComparisonRules.Compatibility(fdm, unknown).Contains("Fuentes diferentes"), "Comparación incompatible");

string csv = ComparisonHistory.ToCsv([bfsk, fdm, unknown]);
Require(csv.Contains("par-ñ") && csv.Contains("mensaje_esperado") && csv.Contains("tipo_fila") &&
    csv.Contains("No disponible: no hay marcas temporales") && csv.Split("\r\n").Length == 10,
    "CSV UTF-8, motivos y filas por canal/total");
using var parser = new TextFieldParser(new StringReader(csv));
parser.SetDelimiters(";");
parser.HasFieldsEnclosedInQuotes = true;
var parsedHeader = parser.ReadFields()!;
var parsedRows = new List<string[]>();
while (!parser.EndOfData) parsedRows.Add(parser.ReadFields()!);
int usefulColumn = Array.IndexOf(parsedHeader, "bits_utiles_esperados");
int durationColumn = Array.IndexOf(parsedHeader, "duracion_transmision_s");
var unknownTotal = parsedRows.Single(row => row[1] == "wav" && row[7] == "TOTAL");
var bfskChannel = parsedRows.Single(row => row[1] == "par-ñ-BFSK" && row[7] == "A");
Require(unknownTotal[usefulColumn] == "" &&
    double.Parse(bfskChannel[durationColumn], System.Globalization.CultureInfo.InvariantCulture) < bfsk.TransmissionSeconds,
    "CSV sin referencia y denominador individual BFSK");
foreach (string dangerous in new[] { "=SUMA(1;1)", "+123", "-1+2", "@SUMA(1;1)", "  =SUMA(1;1)", "\t+123", "\r\n@SUMA(1;1)" })
{
    var changed = bfsk with { Id = dangerous, PairId = dangerous, SourceDescription = dangerous,
        AmplitudeDescription = dangerous, Channels = bfsk.Channels.Select(c => c with
        { Channel = dangerous, ExpectedMessage = dangerous, ReceivedMessage = dangerous, BerReason = dangerous }).ToArray() };
    using var reader = new TextFieldParser(new StringReader(ComparisonHistory.ToCsv([changed])));
    reader.SetDelimiters(";"); reader.HasFieldsEnclosedInQuotes = true;
    var header = reader.ReadFields()!;
    var rows = new List<string[]>();
    while (!reader.EndOfData) rows.Add(reader.ReadFields()!);
    Require(rows.Count == 3 && rows.All(r => r.Length == header.Length), "CSV seguro mantiene columnas y filas");
    foreach (string column in new[] { "id", "par_id", "origen", "amplitud" })
        Require(rows.All(r => r[Array.IndexOf(header, column)] == "'" + dangerous), "Texto peligroso protegido: " + column);
    foreach (string column in new[] { "mensaje_esperado", "mensaje_recibido", "motivo_ber" })
        Require(rows.Where(r => r[7] != "TOTAL").All(r => r[Array.IndexOf(header, column)] == "'" + dangerous), "Texto de canal protegido: " + column);
    Require(rows.Take(2).All(r => r[7] == "'" + dangerous), "Nombre de canal importado protegido");
    Require(rows.All(r => double.Parse(r[Array.IndexOf(header, "rms_observado")], System.Globalization.CultureInfo.InvariantCulture) == bfsk.ObservedRms), "RMS numérico intacto");
}
var harmless = bfsk with { Id = "áé;\"東京\nfin", SourceDescription = "" };
using (var reader = new TextFieldParser(new StringReader(ComparisonHistory.ToCsv([harmless]))))
{
    reader.SetDelimiters(";"); reader.HasFieldsEnclosedInQuotes = true;
    var header = reader.ReadFields()!; var row = reader.ReadFields()!;
    Require(row.Length == header.Length && row[1] == harmless.Id && row[6] == "", "UTF-8, separador, comillas, salto y vacío conservados");
}
using (var reader = new TextFieldParser(new StringReader(ComparisonHistory.ToCsv([bfsk with { NoiseSnrDb = -3, ProcessingMilliseconds = -1 }]))))
{
    reader.SetDelimiters(";"); reader.HasFieldsEnclosedInQuotes = true;
    var header = reader.ReadFields()!; var row = reader.ReadFields()!;
    Require(row[Array.IndexOf(header, "snr_configurada_db")] == "-3" &&
        row[Array.IndexOf(header, "procesamiento_ms")] == "-1" &&
        double.Parse(row[Array.IndexOf(header, "duracion_transmision_s")], System.Globalization.CultureInfo.InvariantCulture) > 0,
        "Signos numéricos y duración siguen siendo números");
}
string path = Path.Combine(Path.GetTempPath(), "acousticlink-comparison-check-" + Guid.NewGuid() + ".json");
try
{
    ComparisonHistory.Save(path, [bfsk, fdm]);
    var restored = ComparisonHistory.Load(path);
    Require(restored.Count == 2 && restored[0].Channels[0].ExpectedMessage == "ñ" &&
        ComparisonRules.Compatibility(restored[0], restored[1]).StartsWith("Comparables"), "Historial recargado");
}
finally { File.Delete(path); }
Console.WriteLine("Comparación BFSK/FDM: métricas, equivalencia, historial y CSV OK");
