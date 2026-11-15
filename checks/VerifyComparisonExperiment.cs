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
