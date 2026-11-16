#:property TargetFramework=net10.0-windows
#:project ../AcousticLink.App/AcousticLink.App.csproj

using AcousticLink.App.Audio;
using AcousticLink.App.Experiments;
using AcousticLink.App.Signals;

static void Require(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

const int rate = 48000;
float[] Background(int seconds, double amplitude)
{
    var random = new Random(23);
    var values = new float[rate * seconds];
    for (int i = 0; i < values.Length; i++) values[i] = (float)((random.NextDouble() * 2 - 1) * amplitude);
    return values;
}

float[] TestAudio(CalibrationMode mode, double scale = 1)
{
    var tone = AcousticCalibration.Generate(mode);
    var values = new float[rate + tone.Length + rate];
    for (int i = 0; i < tone.Length; i++) values[rate + i] = (float)(tone[i] * scale);
    return values;
}

var silence = AcousticCalibration.Level(new float[rate], rate);
Require(silence.Rms == 0 && double.IsNegativeInfinity(silence.Dbfs) && silence.Peak == 0, "Silencio digital");
var sine = new BfskToneGenerator().Generate(false, 1, 0.2);
var sineLevel = AcousticCalibration.Level(sine, rate);
Require(Math.Abs(sineLevel.Rms - 0.2 / Math.Sqrt(2)) < 0.001 &&
    Math.Abs(sineLevel.Dbfs - 20 * Math.Log10(0.2 / Math.Sqrt(2))) < 0.1, "RMS y dBFS de seno");
Require(AcousticCalibration.Level(Enumerable.Repeat(1f, rate).ToArray(), rate).PossibleClipping, "Saturación");
foreach (var bad in new[] { new[] { float.NaN }, new[] { float.PositiveInfinity } })
{
    try { AcousticCalibration.Level(bad, rate); throw new Exception("Muestra inválida aceptada"); }
    catch (ArgumentException) { }
}
try { AcousticCalibration.MeasureNoise(new float[rate / 2], rate, CalibrationMode.Bfsk, "test", "sintético"); throw new Exception("Ruido corto aceptado"); }
catch (ArgumentException) { }

foreach (var mode in new[] { CalibrationMode.Bfsk, CalibrationMode.Fdm })
{
    var background = Background(5, 0.003);
    var noise = AcousticCalibration.MeasureNoise(background, rate, mode, "dispositivo simulado", "sintético");
    Require(noise.BandPowers.Count == (mode == CalibrationMode.Bfsk ? 2 : 4), "Bandas por modalidad");
    var signal = TestAudio(mode);
    var detected = AcousticCalibration.Analyze(signal, rate, mode, noise, "sintético");
    Require(detected.StartSeconds is >= 0.8 and <= 1.2 && detected.Tones.Count == noise.BandPowers.Count,
        "Secuencia alineada y bandas completas");
    Require(detected.Tones.All(t => t.SnrDb is > 10), "Contraste de señal conocida");
    Require(AcousticCalibration.Analyze(new float[rate * 8], rate, mode, noise, "sintético").StartSeconds is null,
        "Señal ausente inconclusa");
    Require(AcousticCalibration.Analyze(new float[rate], rate, mode, noise, "sintético").Status.Contains("corta"),
        "Captura demasiado corta");
    Require(AcousticCalibration.Analyze(signal, rate, mode, noise, "WAV", comparableReference: false)
        .Tones.All(t => t.SnrDb is null), "SNR WAV sin comparabilidad comprobada");
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    try { AcousticCalibration.Analyze(signal, rate, mode, noise, "sintético", cancellation.Token); throw new Exception("Cancelación ignorada"); }
    catch (OperationCanceledException) { }
}

var fdmNoise = AcousticCalibration.MeasureNoise(Background(5, 0.003), rate, CalibrationMode.Fdm, "test", "sintético");
var uneven = TestAudio(CalibrationMode.Fdm);
int stride = rate;
for (int i = 0; i < (int)(AcousticCalibration.ToneSeconds * rate); i++) uneven[rate + 3 * stride + i] *= 0.18f;
var unevenResult = AcousticCalibration.Analyze(uneven, rate, CalibrationMode.Fdm, fdmNoise, "sintético");
Require(unevenResult.Tones.Count == 4 && unevenResult.Recommendations.Any(r => r.Contains("Diferencia")),
    "Diferencia entre frecuencias FDM");
var strongNoise = AcousticCalibration.MeasureNoise(Background(5, 0.2), rate, CalibrationMode.Bfsk, "test", "sintético");
Require(AcousticCalibration.Analyze(TestAudio(CalibrationMode.Bfsk, 0.02), rate, CalibrationMode.Bfsk,
    strongNoise, "sintético").StartSeconds is null, "Señal débil frente al ruido");

var continuous = TestAudio(CalibrationMode.Bfsk);
var generator = new BfskToneGenerator();
for (int toneIndex = 0; toneIndex < 2; toneIndex++)
{
    var gapTone = generator.Generate(toneIndex == 1, AcousticCalibration.GapSeconds,
        AcousticCalibration.TestAmplitude);
    gapTone.CopyTo(continuous, rate + toneIndex * rate + (int)(AcousticCalibration.ToneSeconds * rate));
}
var continuousResult = AcousticCalibration.Analyze(continuous, rate, CalibrationMode.Bfsk,
    AcousticCalibration.MeasureNoise(Background(5, 0.003), rate, CalibrationMode.Bfsk, "test", "sintético"),
    "sintético");
Require(continuousResult.StartSeconds is null, "Tonos continuos sin silencios no son la secuencia");

var knownNoise = new float[rate * 5];
for (int i = 0; i < knownNoise.Length; i++)
    knownNoise[i] = (float)(0.01 * Math.Sin(2 * Math.PI * 1200 * i / rate) +
        0.01 * Math.Sin(2 * Math.PI * 1900 * i / rate));
var knownReference = AcousticCalibration.MeasureNoise(knownNoise, rate, CalibrationMode.Bfsk, "test", "sintético");
var knownSignal = TestAudio(CalibrationMode.Bfsk);
for (int i = 0; i < knownSignal.Length; i++) knownSignal[i] += knownNoise[i % knownNoise.Length];
var knownResult = AcousticCalibration.Analyze(knownSignal, rate, CalibrationMode.Bfsk, knownReference, "sintético");
Require(knownResult.Tones.All(t => t.SnrDb is > 21 and < 26), "SNR de potencia conocida, tolerancia 2.5 dB por ventanas y fase");

const int otherRate = 44100;
var original = TestAudio(CalibrationMode.Bfsk);
var resampled = new float[(int)(original.Length * (double)otherRate / rate)];
for (int i = 0; i < resampled.Length; i++)
{
    double position = i * (double)rate / otherRate;
    int first = (int)position;
    resampled[i] = (float)(original[first] * (1 - (position - first)) + original[Math.Min(first + 1, original.Length - 1)] * (position - first));
}
var otherReference = AcousticCalibration.MeasureNoise(Background(5, 0.003)[..(otherRate * 5)], otherRate,
    CalibrationMode.Bfsk, "test", "sintético");
Require(AcousticCalibration.Analyze(resampled, otherRate, CalibrationMode.Bfsk, otherReference, "sintético").Tones.Count == 2,
    "Tasa efectiva 44.1 kHz");

string path = Path.Combine(Path.GetTempPath(), $"calibration-{Guid.NewGuid():N}.wav");
try
{
    var recording = new CapturedAudio(rate, 1, 10);
    var values = TestAudio(CalibrationMode.Bfsk);
    recording.Append(values, values.Length);
    recording.SaveWav(path);
    var imported = new AudioRecordingSelection();
    imported.ImportWav(path);
    Require(imported.Current!.Audio.SampleRate == rate && imported.Current.Audio.GetSamples().Length == values.Length,
        "WAV válido importado");
    File.WriteAllText(path, "invalid");
    try { imported.ImportWav(path); throw new Exception("WAV inválido aceptado"); }
    catch (InvalidDataException) { }
    Require(imported.Current.Audio.GetSamples().Length == values.Length, "Importación fallida conserva selección");
}
finally { File.Delete(path); }

Console.WriteLine("Calibración: niveles, bandas, secuencia, inconclusos, SNR condicionada, WAV y cancelación: OK");
