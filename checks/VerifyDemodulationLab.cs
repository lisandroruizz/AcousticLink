#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property PublishAot=false
#:property PublishTrimmed=false
#:project ../AcousticLink.App/AcousticLink.App.csproj

using AcousticLink.App.Protocol;
using AcousticLink.App.Signals;
using AcousticLink.App.Visualization;

var detector = new FdmToneDetector();
foreach (int bit in new[] { 0, 1 })
{
    var values = new FdmToneMixer().Generate(bit == 1, false);
    var result = detector.Analyze(values.AsSpan(4800, 2880), 48000).ChannelA;
    var trace = result.FdmDiagnostics!;
    Require(result.Bit == bit && trace.DurationAccepted && trace.EnergyAccepted == true && trace.RatioAccepted == true, "Decisión y traza coherentes");
    Require(trace.MinimumToneEnergy == detector.MinimumToneEnergy && trace.MinimumEnergyRatio == detector.MinimumEnergyRatio && trace.MinimumDurationSeconds == detector.MinimumDurationSeconds, "Umbrales del mismo detector");
}
var absent = detector.Analyze(new float[2880], 48000).ChannelA;
Require(absent.Bit is null && absent.FdmDiagnostics!.EnergyAccepted == false && absent.FdmDiagnostics.RatioAccepted is null && DemodulationSelection.EnergyRatio(absent) is null, "Silencio: energía insuficiente y razón no definida");
var shortResult = detector.Analyze(new float[480], 48000).ChannelA;
Require(!shortResult.FdmDiagnostics!.DurationAccepted && shortResult.FdmDiagnostics.EnergyAccepted is null, "Energías no calculadas en fragmento corto");
var ambiguous = Enumerable.Range(0, 2880).Select(i => (float)(.04 * Math.Sin(2 * Math.PI * 1200 * i / 48000) + .04 * Math.Sin(2 * Math.PI * 1900 * i / 48000))).ToArray();
var doubt = detector.Analyze(ambiguous, 48000).ChannelA;
Require(doubt.Bit is null && doubt.FdmDiagnostics!.EnergyAccepted == true && doubt.FdmDiagnostics.RatioAccepted == false, "Ambigüedad real del detector");
var custom = new FdmToneDetector(minimumToneEnergy: .01, minimumEnergyRatio: 6);
Require(custom.Analyze(new float[2880], 48000).ChannelA.FdmDiagnostics!.MinimumEnergyRatio == 6, "No se inventan umbrales predeterminados en la interfaz");

foreach (bool hamming in new[] { false, true })
{
    var raw = new FdmBitstreamGenerator().GenerateTextFrames("ñ", "a", hamming);
    var samples = new float[raw.Length + 48000]; raw.CopyTo(samples, 5000);
    var signal = new MonitorSignal(samples, 48000, MonitorSignalOrigin.ImportedWav, "Ensayo numérico");
    var reception = new FdmTextReceiver().Receive(samples, 48000, useHamming: hamming, recoverErasures: true);
    Require(reception.ChannelA.CrcValid == true && reception.ChannelB.CrcValid == true, "Mensaje real del receptor");
    var sourceCopy = samples.ToArray();
    foreach (var trace in ReceivedSymbols.From(reception, false, 48000))
    {
        double center = (trace.Symbol.AnalysisStartSample + trace.Symbol.AnalysisSampleCount / 2.0) / 48000;
        var selected = DemodulationSelection.From(signal, reception, false, center)!;
        Require(ReferenceEquals(selected.Detection, trace.Symbol.Detection), "No se recalcula la decisión del símbolo seleccionado");
        var expected = detector.Analyze(samples.AsSpan(selected.StartSample, selected.SampleCount), 48000).ChannelA;
        Require(selected.Detection == expected, "Energías y decisión coinciden con la ventana real");
        if (selected.BlockBitIndex is not null)
        {
            int logical = selected.OriginalBitIndex ?? (selected.Word?.WordIndex * 4 ?? -1);
            string expectedField = logical < 8 ? "Campo longitud" : logical < 8 + reception.ChannelA.ByteLength * 8 ? "Contenido UTF-8" : "Campo CRC-32";
            Require(selected.Field(reception.ChannelA) == expectedField, "Campo obtenido de los bits originales; CRC no se etiqueta como contenido UTF-8");
            var noLength = reception.ChannelA with { ByteLength = null };
            Require(logical < 8 || selected.Field(noLength).Contains("no determinada") || selected.Field(noLength).Contains("no recuperada"), "Sin longitud no se atribuyen campos posteriores");
        }
        if (selected.Word is { } word)
        {
            Require(ReferenceEquals(word, reception.ChannelA.HammingDiagnostics!.Traces[word.WordIndex]), "Se usa la traza Hamming conservada");
            Require(DemodulationSelection.SymbolForBlockBit(reception.ChannelA, word.EncodedBitOffset) == reception.ChannelA.Block!.Symbols[word.EncodedBitOffset], "Selección inversa palabra → símbolo");
            if (selected.BlockBitIndex!.Value % 7 is 0 or 1 or 3) Require(selected.OriginalBitIndex is null, "Paridad no se confunde con información");
        }
    }
    var available = ReceivedSymbols.From(reception, false, 48000);
    Require(DemodulationSelection.Find(available, available[0].Number, -1) is null && DemodulationSelection.Find(available, available[^1].Number, 1) is null, "Navegación limitada a símbolos realmente disponibles");
    Require(DemodulationSelection.Find(available, available[0].Number, 1) == available[1] && DemodulationSelection.Find(available, available[1].Number, -1) == available[0], "Anterior y siguiente conservan orden");
    Require(DemodulationSelection.Find(available, 0, 1, true) is null, "Accesos a ? sin coincidencias se deshabilitan");
    if (hamming)
    {
        // Fixture de mapeo: borrado controlado y decodificación real del códec; no medición acústica.
        var originalChannel = reception.ChannelA; var erasedBits = originalChannel.Block!.Bits.ToCharArray(); erasedBits[16] = '?';
        var recovered = new Hamming74Codec().Decode(new string(erasedBits), true);
        var rawSymbols = originalChannel.Block.Symbols.ToArray(); rawSymbols[16] = rawSymbols[16] with { Detection = detector.Analyze(new float[rawSymbols[16].AnalysisSampleCount], 48000).ChannelA };
        var fixture = reception with { ChannelA = originalChannel with { Block = new BfskSequenceResult(rawSymbols), HammingDiagnostics = recovered } };
        var at = rawSymbols[16]; var inspected = DemodulationSelection.From(signal, fixture, false, (at.AnalysisStartSample + at.AnalysisSampleCount / 2.0) / 48000)!;
        Require(DemodulationSelection.NextProblem(fixture.ChannelA with { HeaderValid = false }, -1) is null, "Sin cabecera validada no se ofrecen posiciones Hamming");
        var erasedTraces = ReceivedSymbols.From(fixture, false, 48000);
        var firstErased = DemodulationSelection.Find(erasedTraces, 0, 1, true, true)!;
        Require(firstErased.Symbol == rawSymbols[16] && DemodulationSelection.Find(erasedTraces, firstErased.Number, 1, true) is null, "Primer y siguiente ? proceden de decisiones reales");
        Require(DemodulationSelection.NextProblem(fixture.ChannelA, -1) == rawSymbols[14] && DemodulationSelection.NextProblem(fixture.ChannelA, 14) is null, "Salto a palabra modificada, sin duplicar el símbolo recibido");
        Require(inspected.Detection.Bit is null && inspected.Word!.State == Hamming74TraceState.ErasuresRecovered && inspected.Word.ReceivedBits.Contains('?') && inspected.Word.DataBits is not null, "El borrado recibido se mantiene distinto de sus datos reconstruidos");
    }
    var partial = reception with { ChannelA = reception.ChannelA with { HeaderValid = false } };
    Require(DemodulationSelection.From(signal, partial, false, 3) is null && ReceivedSymbols.From(partial, true, 48000).Count > 0, "A incompleto no impide B");
    var reverse = reception with { ChannelB = reception.ChannelB with { HeaderValid = false } };
    Require(DemodulationSelection.From(signal, reverse, true, 3) is null && ReceivedSymbols.From(reverse, false, 48000).Count > 0, "B incompleto no impide A");
    Require(samples.SequenceEqual(sourceCopy), "Muestras intactas");
}
Require(ReceivedSymbols.From(null, false, 48000).Count == 0 && DemodulationSelection.Find([], 0, 1) is null && DemodulationSelection.NextProblem(null, -1) is null, "Invalidación sin resultados ni navegación obsoleta");
Console.WriteLine("Laboratorio de demodulación: traza, 0/1/?, umbrales, ventanas, Hamming, paridad, bytes y canales independientes: OK");
static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
