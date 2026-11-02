#:property TargetFramework=net10.0-windows
#:project ../AcousticLink.App/AcousticLink.App.csproj

using AcousticLink.App.Signals;
using AcousticLink.App.Protocol;
using AcousticLink.App.Experiments;

var mixer = new FdmToneMixer();
var generator = new FdmBitstreamGenerator();
var detector = new FdmToneDetector();
var original = FdmAmplitudeProfiles.Get(FdmAmplitudeProfile.Original);
var prioritized = FdmAmplitudeProfiles.Get(FdmAmplitudeProfile.PrioritizeChannelA);
Require(original.ChannelA == 0.075 && original.ChannelB == 0.075 && prioritized.ChannelA == 0.10 && prioritized.ChannelB == 0.05, "Perfiles exactos");
foreach (var setting in FdmAmplitudeProfiles.All)
{
    Require(setting.ChannelA + setting.ChannelB <= 0.15 + 1e-15, "Límite teórico conjunto");
    foreach (bool a in new[] { false, true })
    foreach (bool b in new[] { false, true })
    {
        var tone = mixer.Generate(a, b, profile: setting.Profile);
        Verify(tone, new string(a ? '1' : '0', 10), new string(b ? '1' : '0', 10), setting);
        var energies = detector.Analyze(tone.AsSpan(9600, 4800), 48000);
        double energyA = a ? energies.ChannelA.Energy1 : energies.ChannelA.Energy0;
        double energyB = b ? energies.ChannelB.Energy1 : energies.ChannelB.Energy0;
        Require(Math.Abs(energyA - setting.ChannelA * setting.ChannelA / 2) < 1e-8 && Math.Abs(energyB - setting.ChannelB * setting.ChannelB / 2) < 1e-8, "Energías ideales por banda");
        if (setting.Profile == FdmAmplitudeProfile.PrioritizeChannelA)
        {
            var baseline = detector.Analyze(mixer.Generate(a, b).AsSpan(9600, 4800), 48000);
            Require(Math.Abs(energyA / (a ? baseline.ChannelA.Energy1 : baseline.ChannelA.Energy0) - 16.0 / 9) < 1e-6, "Energía A = 16/9");
            Require(Math.Abs(energyB / (b ? baseline.ChannelB.Energy1 : baseline.ChannelB.Energy0) - 4.0 / 9) < 1e-6, "Energía B = 4/9");
        }
        else Require(tone.SequenceEqual(mixer.Generate(a, b)), "Perfil original predeterminado idéntico");
    }
    const string bitsA = "10110010", bitsB = "01001101";
    Verify(generator.Generate(bitsA, bitsB, setting.Profile), bitsA, bitsB, setting);
    var frames = FdmBitstreamGenerator.BuildFrames(bitsA, bitsB);
    Verify(generator.GenerateFrames(bitsA, bitsB, setting.Profile), frames.ChannelA, frames.ChannelB, setting);
    foreach (bool hamming in new[] { false, true })
    {
        var textFrames = new FdmTextFrameBuilder().Build("Hola", "Mundo", hamming);
        var audio = generator.GenerateTextFrames("Hola", "Mundo", hamming, setting.Profile);
        Verify(audio, textFrames.ChannelA.TransmittedBits, textFrames.ChannelB.TransmittedBits, setting);
        var received = new FdmTextReceiver().Receive(audio, 48000, useHamming: hamming);
        Require(received.ChannelA.Text == "Hola" && received.ChannelB.Text == "Mundo" && received.ChannelA.CrcValid == true && received.ChannelB.CrcValid == true, "Compatibilidad de recepción ideal con ambos modos");
        if (setting.Profile == FdmAmplitudeProfile.Original)
            Require(audio.SequenceEqual(generator.GenerateTextFrames("Hola", "Mundo", hamming)), "Muestras originales de texto idénticas");
    }
    Console.WriteLine($"{setting.Label}: cuatro combinaciones, secuencias, tramas, texto ±Hamming, fases y energías: OK");
}
Require(generator.Generate("01", "10").SequenceEqual(generator.Generate("01", "10", FdmAmplitudeProfile.Original)), "Secuencias originales idénticas");
Require(generator.GenerateFrames("01", "10").SequenceEqual(generator.GenerateFrames("01", "10", FdmAmplitudeProfile.Original)), "Tramas originales idénticas");
try { mixer.Generate(false, false, profile: (FdmAmplitudeProfile)999); throw new InvalidOperationException("Perfil inválido aceptado"); }
catch (ArgumentOutOfRangeException) { }
var analyzer = new FdmExperimentAnalyzer();
var signal = generator.GenerateTextFrames("Hola", "Mundo", profile: FdmAmplitudeProfile.PrioritizeChannelA);
var unknown = analyzer.Analyze(signal, 48000, "Hola", "Mundo", audioSource: "WAV importado: sintético");
Require(unknown.DeclaredAmplitudeProfile is null && FdmExperimentCsv.Serialize(unknown).Contains("no_informado"), "No se infiere perfil desde WAV o energía");
var declared = analyzer.Analyze(signal, 48000, "Hola", "Mundo", audioSource: "WAV importado: sintético", declaredAmplitudeProfile: FdmAmplitudeProfile.PrioritizeChannelA);
Require(declared.ChannelA.Before == unknown.ChannelA.Before && declared.ChannelB.Before == unknown.ChannelB.Before, "Metadato no altera mediciones ni decisiones");
string csv = FdmExperimentCsv.Serialize(declared);
Require(csv.Contains("perfil_amplitud_declarado") && csv.Contains("\"Priorizar canal A\";\"0,1\";\"0,05\""), "CSV registra declaración y amplitudes, no una medición");
Console.WriteLine("Predeterminados, perfil inválido y metadato explícito desconocido/declarado en CSV: OK");

void Verify(float[] samples, string a, string b, FdmAmplitudeSetting setting)
{
    Require(samples.Length == a.Length * 4800 && a.Length == b.Length, "Duración y reloj común");
    Require(samples.All(x => float.IsFinite(x) && Math.Abs(x) <= 0.15 + 1e-8 && Math.Abs(x) < 1), "Muestras finitas, límite sin recorte");
    Require(samples[0] == 0 && samples[^1] == 0, "Extremos de rampas");
    double cyclesA = 0, cyclesB = 0;
    for (int symbol = 0; symbol < a.Length; symbol++)
    {
        double fa = a[symbol] == '0' ? 1200 : 1900, fb = b[symbol] == '0' ? 3000 : 3700;
        for (int j = 0; j < 4800; j++)
        {
            int i = symbol * 4800 + j;
            double envelope = Math.Min(1, Math.Min(i, samples.Length - 1 - i) / 240.0);
            double expected = envelope * (setting.ChannelA * Math.Sin(2 * Math.PI * (cyclesA + fa * j / 48000)) + setting.ChannelB * Math.Sin(2 * Math.PI * (cyclesB + fb * j / 48000)));
            Require(Math.Abs(samples[i] - expected) < 1e-7, "Referencia matemática sin normalización, fases y rampas globales");
        }
        cyclesA += fa * 0.1; cyclesB += fb * 0.1;
        var result = detector.Analyze(samples.AsSpan(symbol * 4800 + 960, 2880), 48000);
        Require(result.ChannelA.Bit == a[symbol] - '0' && result.ChannelB.Bit == b[symbol] - '0', "Frecuencias presentes por símbolo");
    }
}
static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
