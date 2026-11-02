#:property TargetFramework=net10.0-windows
#:project ../AcousticLink.App/AcousticLink.App.csproj

using AcousticLink.App.Audio;
using AcousticLink.App.Experiments;
using AcousticLink.App.Protocol;
using AcousticLink.App.Signals;

var analyzer = new FdmExperimentAnalyzer();
var builder = new FdmTextFrameBuilder();
var sender = new FdmBitstreamGenerator();
FdmExperimentResult? csvWithErrors = null;
var basic = FdmExperimentAnalyzer.Compare("1010", "0?11");
Require(basic.ComparedBits == 3 && basic.IncorrectBits == 2 && basic.InconclusiveBits == 1 && basic.MissingBits == 0 && basic.Ber == 2.0 / 3 && basic.Coverage == 0.75, "Denominadores y símbolos '?'");
var unobserved = FdmExperimentAnalyzer.Compare("1010", "??");
Require(unobserved.Ber is null && unobserved.Coverage == 0 && unobserved.MissingBits == 2, "Sin comparables no hay BER");
foreach (bool hamming in new[] { false, true })
foreach (var (a, b) in new[] { ("Hola", "Mundo"), ("ñ", "áé") })
{
    var result = analyzer.Analyze(Padded(sender.GenerateTextFrames(a, b, hamming)), 48000, a, b, hamming, "sintético");
    foreach (var channel in new[] { result.ChannelA, result.ChannelB })
    {
        Require(channel.Before!.Ber == 0 && channel.Before.Coverage == 1 && channel.Before.MissingBits == 0, "BER cero con cobertura completa");
        Require(channel.Reception.CrcValid == true, "CRC real del receptor");
        if (hamming) Require(channel.After!.Ber == 0 && channel.After.Coverage == 1 && channel.Hamming!.AppliedBitFlips == 0, "Hamming sin errores");
        else Require(channel.After is null && channel.Hamming is null, "Sin estadísticas de corrección en modo simple");
    }
    Console.WriteLine($"{a}/{b}, Hamming={hamming}: referencias, cobertura y BER cero: OK");
}

foreach (bool hamming in new[] { false, true })
{
    var reference = builder.Build("Hola", "Mundo", hamming);
    string originalA = reference.ChannelA.TransmittedBits, originalB = reference.ChannelB.TransmittedBits;
    string changedA = Flip(originalA, hamming ? 24 + 14 : 32);
    var single = AnalyzeChanged(changedA, originalB, hamming);
    if (!hamming) csvWithErrors = single;
    Require(single.ChannelA.Before!.IncorrectBits == 1 && single.ChannelB.Before!.IncorrectBits == 0, "Error controlado independiente");
    Require(single.ChannelA.Before.ExpectedCount == (hamming ? 126 : 72), "Cabecera y relleno excluidos");
    Require(single.ChannelA.Before.Ber == 1.0 / (hamming ? 126 : 72), "BER anterior exacta");
    if (hamming)
    {
        Require(single.ChannelA.After!.ExpectedCount == 72 && single.ChannelA.After.IncorrectBits == 0 && single.ChannelA.Reception.CrcValid == true,
            "Corrección de un error y denominador original");
        var allWords = originalA.ToCharArray();
        for (int offset = 24; offset < reference.ChannelA.RealBitCount; offset += 7)
            allWords[offset] = allWords[offset] == '0' ? '1' : '0';
        var distributed = AnalyzeChanged(new string(allWords), originalB, true);
        Require(distributed.ChannelA.Before!.IncorrectBits == 18 && distributed.ChannelA.Before.ComparedBits == 126 &&
            distributed.ChannelA.After!.IncorrectBits == 0 && distributed.ChannelA.After.ComparedBits == 72, "Un error por palabra y denominadores 126/72");
        Require(distributed.ChannelA.Hamming!.AnalyzedWords == 18 && distributed.ChannelA.Hamming.AppliedBitFlips == 18 &&
            distributed.ChannelA.Reception.CrcValid == true, "Conteos y recuperación exacta");
        var multiple = AnalyzeChanged(Flip(Flip(originalA, 38), 39), originalB, true);
        Require(multiple.ChannelA.Before!.IncorrectBits == 2 && multiple.ChannelA.After!.IncorrectBits == 1 &&
            multiple.ChannelA.Hamming!.AppliedBitFlips == 1, "Mala corrección de error doble conserva error residual");
        Require(multiple.ChannelA.Reception.CrcValid == false && multiple.ChannelB.Reception.CrcValid == true, "CRC e independencia");
    }
    else Require(single.ChannelA.Reception.CrcValid == false && single.ChannelB.Reception.CrcValid == true, "CRC de error simple sin Hamming");

    int unknownPosition = hamming ? 40 : 35;
    string unknownA = originalA[..unknownPosition] + "?" + originalA[(unknownPosition + 1)..];
    var unknown = AnalyzeChanged(unknownA, originalB, hamming);
    Require(unknown.ChannelA.Before!.InconclusiveBits == 1 && unknown.ChannelA.Before.IncorrectBits == 0 &&
        unknown.ChannelA.Before.ComparedBits == unknown.ChannelA.Before.ExpectedCount - 1, "No confundir '?' con error");
    Require(unknown.ChannelA.Before.Ber == 0 && unknown.ChannelA.Before.Coverage < 1 && unknown.ChannelA.Reception.CrcValid is null, "BER parcial y CRC no comprobado");
    if (hamming)
    {
        Require(unknown.ChannelA.After!.InconclusiveBits == 4 && unknown.ChannelA.After.ComparedBits == 68 &&
            unknown.ChannelA.Hamming!.InconclusiveWords == 1 && unknown.ChannelA.Hamming.AnalyzedWords == 17, "Palabra con '?' deja cuatro bits no comparables");
    }
    var partial = AnalyzeChanged(Flip(originalA, 23), originalB, hamming);
    Require(partial.ChannelA.Before is null && partial.ChannelB.Before!.Ber == 0, "Sincronización parcial no inventa BER");
}
var plain = Padded(sender.GenerateTextFrames("Hola", "Mundo"));
var truncated = analyzer.Analyze(plain[..(int)((0.237 + 8) * 48000)], 48000, "Hola", "Mundo");
Require(truncated.ChannelA.Before!.MissingBits > 0 && truncated.ChannelA.Before.Coverage < 1, "Truncación y cobertura");
var absent = analyzer.Analyze(new float[48000 * 12], 48000, "Hola", "Mundo");
Require(absent.ChannelA.Before is null && absent.ChannelB.Before is null, "Falta de sincronización sin BER ficticia");
var ambiguous = analyzer.Analyze(plain.Concat(plain).ToArray(), 48000, "Hola", "Mundo");
Require(ambiguous.Synchronization.IsAmbiguous && ambiguous.ChannelA.Before is null, "Ambigüedad sin elegir trama");

string wavPath = Path.Combine(Path.GetTempPath(), $"AcousticLink-metrics-{Guid.NewGuid()}.wav");
string csvPath = Path.ChangeExtension(wavPath, ".csv");
try
{
    float[] signal = Padded(sender.GenerateTextFrames("Hola", "Mundo", true));
    var captured = new CapturedAudio(48000, 1, 30);
    captured.Append(signal, signal.Length);
    captured.Complete(CaptureStopReason.Manual);
    var direct = analyzer.Analyze(captured.GetSamples(), captured.SampleRate, "Hola", "Mundo", true, "sintético;\"captura\"\nnueva línea");
    captured.SaveWav(wavPath);
    var selection = new AudioRecordingSelection();
    selection.ImportWav(wavPath);
    var imported = analyzer.Analyze(selection.Current!.Audio.GetSamples(), selection.Current.Audio.SampleRate, "Hola", "Mundo", true, selection.Current.Source);
    Require(direct.ChannelA.Before == imported.ChannelA.Before && direct.ChannelB.After == imported.ChannelB.After &&
        direct.ChannelA.Hamming!.AppliedBitFlips == imported.ChannelA.Hamming!.AppliedBitFlips, "Equivalencia captura/importación");
    FdmExperimentCsv.Save(csvPath, direct);
    byte[] bytes = File.ReadAllBytes(csvPath);
    Require(bytes.Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }), "BOM UTF-8 para Excel");
    var rows = ParseCsv(File.ReadAllText(csvPath));
    Require(rows.Count == 3 && rows.All(row => row.Count == rows[0].Count), "Una cabecera y dos filas por canal");
    string Value(int row, string key) => rows[row][rows[0].IndexOf(key)];
    Require(Value(1, "mensaje_esperado") == "Hola" && Value(2, "mensaje_esperado") == "Mundo" && Value(1, "origen") == direct.AudioSource, "CSV preserva texto, comillas y saltos");
    Require(Value(1, "antes_esperados") == "126" && Value(1, "despues_esperados") == "72" && Value(1, "antes_ber") == "0" &&
        Value(1, "crc") == "valido" && Value(1, "hamming_inversiones") == "0", "Valores y denominadores CSV");
    var errorRows = ParseCsv(FdmExperimentCsv.Serialize(csvWithErrors!));
    string ErrorValue(string key) => errorRows[1][errorRows[0].IndexOf(key)];
    Require(ErrorValue("antes_incorrectos") == "1" && ErrorValue("crc") == "invalido" && ErrorValue("hamming_palabras") == "" &&
        double.Parse(ErrorValue("antes_ber"), System.Globalization.CultureInfo.GetCultureInfo("es-AR")) == 1.0 / 72,
        "CSV de error real conserva BER, denominador y modalidad sin estadísticas Hamming");
    var missingRows = ParseCsv(FdmExperimentCsv.Serialize(absent));
    Require(missingRows[1][missingRows[0].IndexOf("antes_ber")] == "" && missingRows[1][missingRows[0].IndexOf("crc")] == "no_comprobado", "CSV no inventa métricas ausentes");
}
finally { File.Delete(wavPath); File.Delete(csvPath); }
Console.WriteLine("Errores y denominadores, '?', Hamming, fallos de sincronización, CSV y equivalencia WAV: OK");

FdmExperimentResult AnalyzeChanged(string a, string b, bool hamming)
{
    var reference = builder.Build("Hola", "Mundo", hamming);
    var samples = sender.GenerateTextFrames("Hola", "Mundo", hamming);
    for (int symbol = 0; symbol < a.Length; symbol++)
    {
        if (a[symbol] == reference.ChannelA.TransmittedBits[symbol] && b[symbol] == reference.ChannelB.TransmittedBits[symbol]) continue;
        float[] replacement = new FdmToneMixer().Generate(a[symbol] == '1', b[symbol] == '1', 0.1);
        if (a[symbol] == '?')
            for (int j = 0; j < 4800; j++) replacement[j] = (float)(0.075 * Math.Sin(2 * Math.PI * (b[symbol] == '0' ? 3000 : 3700) * j / 48000));
        Array.Copy(replacement, 0, samples, symbol * 4800, 4800);
    }
    return analyzer.Analyze(Padded(samples), 48000, "Hola", "Mundo", hamming, "sintético con errores deliberados");
}
static float[] Padded(float[] signal)
{
    var samples = new float[11376 + signal.Length + 9600];
    signal.CopyTo(samples, 11376);
    return samples;
}
static string Flip(string bits, int index) => bits[..index] + (bits[index] == '0' ? "1" : "0") + bits[(index + 1)..];
static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
static List<List<string>> ParseCsv(string csv)
{
    var rows = new List<List<string>>();
    var row = new List<string>();
    var field = new System.Text.StringBuilder();
    bool quoted = false;
    for (int i = 0; i < csv.Length; i++)
    {
        char c = csv[i];
        if (c == '"')
        {
            if (quoted && i + 1 < csv.Length && csv[i + 1] == '"') { field.Append('"'); i++; }
            else quoted = !quoted;
        }
        else if (!quoted && c == ';') { row.Add(field.ToString()); field.Clear(); }
        else if (!quoted && c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n')
        {
            row.Add(field.ToString()); field.Clear(); rows.Add(row); row = new(); i++;
        }
        else field.Append(c);
    }
    return rows;
}
