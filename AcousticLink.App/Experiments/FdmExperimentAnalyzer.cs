using System.Text;
using AcousticLink.App.Protocol;
using AcousticLink.App.Signals;

namespace AcousticLink.App.Experiments;

public sealed record BitComparison(string ExpectedBits, string ObservedBits, int ComparedBits, int IncorrectBits,
    int InconclusiveBits, int MissingBits)
{
    public int ExpectedCount => ExpectedBits.Length;
    public double? Ber => ComparedBits > 0 ? IncorrectBits / (double)ComparedBits : null;
    public double Coverage => ComparedBits / (double)ExpectedCount;
}

public sealed record ExperimentHammingStats(IReadOnlyList<Hamming74WordResult> Words, int InconclusiveWords, int IncompleteWords)
{
    public int AnalyzedWords => Words.Count;
    public int NonzeroSyndromes => Words.Count(w => w.Syndrome != 0);
    public int AppliedBitFlips => Words.Count(w => w.FlippedPosition.HasValue);
}

public sealed record FdmChannelExperiment(string Channel, string ExpectedMessage, string OriginalReferenceBits,
    string TransmittedReferenceBits, BitComparison? Before, BitComparison? After,
    ExperimentHammingStats? Hamming, FdmTextChannelResult Reception, string Diagnostic);

public sealed record FdmExperimentResult(DateTimeOffset AnalyzedAt, string AudioSource, int SampleRate,
    double AudioDurationSeconds, bool UsesHamming, FdmSynchronizationResult Synchronization,
    FdmChannelExperiment ChannelA, FdmChannelExperiment ChannelB)
{
    // Declaración manual del emisor; jamás se infiere del WAV ni se utiliza para decidir bits.
    public FdmAmplitudeProfile? DeclaredAmplitudeProfile { get; init; }
}

public sealed class FdmExperimentAnalyzer
{
    public FdmExperimentResult Analyze(ReadOnlySpan<float> samples, int sampleRate, string expectedA, string expectedB,
        bool useHamming = false, string audioSource = "Sin origen especificado", CancellationToken token = default,
        FdmAmplitudeProfile? declaredAmplitudeProfile = null)
    {
        if (declaredAmplitudeProfile.HasValue) _ = FdmAmplitudeProfiles.Get(declaredAmplitudeProfile.Value);
        var references = new FdmTextFrameBuilder().Build(expectedA, expectedB, useHamming);
        // El receptor decide sincronización y CRC a partir del audio, nunca de los textos esperados.
        var reception = new FdmTextReceiver().Receive(samples, sampleRate, token, useHamming);
        var sync = reception.Synchronization;
        var rawA = new StringBuilder();
        var rawB = new StringBuilder();
        if (sync.DataStartSeconds.HasValue)
        {
            var decoder = new FdmSequenceDecoder();
            double origin = sync.DataStartSeconds.Value * sampleRate;
            double step = FdmSequenceDecoder.SymbolDurationSeconds * sampleRate;
            int maximum = Math.Max(references.ChannelA.EncodedBlockBitCount, references.ChannelB.EncodedBlockBitCount);
            for (int i = 0; i < maximum; i++)
            {
                token.ThrowIfCancellationRequested();
                if (Math.Round(origin + (i + 1) * step) > samples.Length) break;
                // Límites calculados desde un único origen absoluto, sin acumular pasos redondeados.
                var symbol = decoder.Decode(samples, sampleRate, sync.DataStartSeconds.Value + i * FdmSequenceDecoder.SymbolDurationSeconds, 1);
                if (i < references.ChannelA.EncodedBlockBitCount) rawA.Append(symbol.ChannelA.Bits);
                if (i < references.ChannelB.EncodedBlockBitCount) rawB.Append(symbol.ChannelB.Bits);
            }
        }
        var a = Evaluate("A", expectedA, references.ChannelA, rawA.ToString(), reception.ChannelA, useHamming, token);
        var b = Evaluate("B", expectedB, references.ChannelB, rawB.ToString(), reception.ChannelB, useHamming, token);
        return new(DateTimeOffset.UtcNow, audioSource, sampleRate, samples.Length / (double)sampleRate, useHamming, sync, a, b)
        { DeclaredAmplitudeProfile = declaredAmplitudeProfile };
    }

    private static FdmChannelExperiment Evaluate(string name, string message, FdmTextChannelFrame reference,
        string raw, FdmTextChannelResult reception, bool useHamming, CancellationToken token)
    {
        if (!reception.HeaderValid)
            return new(name, message, reference.OriginalBlockBits, reference.EncodedBlockBits, null, null, null, reception,
                "Sin alineación validada para este canal: BER y cobertura no calculables. " + reception.Reason);
        var before = Compare(reference.EncodedBlockBits, raw);
        if (!useHamming)
            return new(name, message, reference.OriginalBlockBits, reference.EncodedBlockBits, before, null, null, reception,
                "Comparación del bloque original; cabecera y relleno excluidos. " + reception.Reason);

        var decoded = new StringBuilder();
        var words = new List<Hamming74WordResult>();
        var codec = new Hamming74Codec();
        int unknownWords = 0;
        int availableWords = raw.Length / 7;
        for (int i = 0; i < availableWords; i++)
        {
            token.ThrowIfCancellationRequested();
            string word = raw.Substring(i * 7, 7);
            if (word.Contains('?'))
            {
                // No existe una decisión sobre sus cuatro bits originales: mantener sus posiciones.
                decoded.Append("????");
                unknownWords++;
                continue;
            }
            var result = codec.Decode(word);
            decoded.Append(result.DataBits);
            words.Add(result.Words[0] with { WordIndex = i });
        }
        int incompleteWords = reference.EncodedBlockBitCount / 7 - availableWords;
        var after = Compare(reference.OriginalBlockBits, decoded.ToString());
        return new(name, message, reference.OriginalBlockBits, reference.EncodedBlockBits, before, after,
            new(words.AsReadOnly(), unknownWords, incompleteWords), reception,
            "BER previa sobre bits codificados; posterior sobre bits originales. Las inversiones son intentos, no correcciones demostradas. " +
            "Una palabra con '?' deja cuatro bits originales sin comparar; las palabras incompletas figuran como faltantes. " + reception.Reason);
    }

    public static BitComparison Compare(string expected, string observed)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(observed);
        if (expected.Length == 0 || expected.Any(c => c != '0' && c != '1') || observed.Length > expected.Length ||
            observed.Any(c => c != '0' && c != '1' && c != '?')) throw new ArgumentException("Secuencias de comparación inválidas.");
        int compared = 0, incorrect = 0, unknown = 0;
        for (int i = 0; i < observed.Length; i++)
        {
            if (observed[i] == '?') { unknown++; continue; }
            compared++;
            if (observed[i] != expected[i]) incorrect++;
        }
        return new(expected, observed, compared, incorrect, unknown, expected.Length - observed.Length);
    }
}
