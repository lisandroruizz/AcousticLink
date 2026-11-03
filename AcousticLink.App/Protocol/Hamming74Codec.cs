namespace AcousticLink.App.Protocol;

// WordIndex es base cero; Syndrome y FlippedPosition usan posiciones convencionales 1–7.
// Syndrome solo tiene significado si HasSyndrome es true (palabra sin borrados).
public sealed record Hamming74WordResult(int WordIndex, int Syndrome, int? FlippedPosition)
{
    public bool HasSyndrome { get; init; } = true;
    public int ErasureCount { get; init; }
    public int RecoveredErasureCount { get; init; }
    public bool IsDecodable { get; init; } = true;
}

public sealed record Hamming74DecodeResult(string DataBits, IReadOnlyList<Hamming74WordResult> Words)
{
    public IReadOnlyList<Hamming74WordTrace> Traces { get; init; } = Array.Empty<Hamming74WordTrace>();
    public int WordCount => Words.Count;
    // Síndrome cero NO garantiza ausencia de errores múltiples.
    public int ZeroSyndromeWordCount => Words.Count(w => w.HasSyndrome && w.Syndrome == 0);
    public bool IsComplete => !DataBits.Contains('?');
    public int RecoveredErasureCount => Words.Sum(w => w.RecoveredErasureCount);
    public int ErasureRecoveredWordCount => Words.Count(w => w.RecoveredErasureCount > 0);
    public int UnrecoverableWordCount => Words.Count(w => !w.IsDecodable);
    public int CorrectedWordCount => Words.Count(w => w.FlippedPosition.HasValue);
    // Inversiones aplicadas, no errores reales demostrados sin una referencia transmitida.
    public int AppliedBitFlipCount => CorrectedWordCount;
}

/// <summary>
/// Hamming (7,4), paridad par. D1 es el primer bit (MSB) de cada grupo de cuatro.
/// Serialización por posiciones 1 a 7: P1 P2 D1 P4 D2 D3 D4.
/// Corrige un error por palabra; con dos o más puede corregir mal o no detectar errores.
/// El CRC-32 posterior detectará muchas alteraciones residuales, no absolutamente todas.
/// </summary>
public sealed class Hamming74Codec
{
    public string Encode(string dataBits)
    {
        Validate(dataBits, 4, nameof(dataBits));
        var encoded = new char[checked(dataBits.Length / 4 * 7)];
        for (int source = 0, target = 0; source < dataBits.Length; source += 4, target += 7)
        {
            int d1 = dataBits[source] - '0', d2 = dataBits[source + 1] - '0';
            int d3 = dataBits[source + 2] - '0', d4 = dataBits[source + 3] - '0';
            encoded[target] = ToChar(d1 ^ d2 ^ d4);
            encoded[target + 1] = ToChar(d1 ^ d3 ^ d4);
            encoded[target + 2] = ToChar(d1);
            encoded[target + 3] = ToChar(d2 ^ d3 ^ d4);
            encoded[target + 4] = ToChar(d2);
            encoded[target + 5] = ToChar(d3);
            encoded[target + 6] = ToChar(d4);
        }
        return new string(encoded);
    }

    // Con borrados: hasta dos, candidata única y bits conocidos supuestos correctos.
    // No garantiza corregir errores conocidos mezclados con borrados. El CRC sigue siendo obligatorio.
    // Sin recuperación se mantiene la validación binaria estricta del códec anterior.
    public Hamming74DecodeResult Decode(string encodedBits, bool recoverErasures = false)
    {
        Validate(encodedBits, 7, nameof(encodedBits), recoverErasures);
        int wordCount = encodedBits.Length / 7;
        var data = new char[checked(wordCount * 4)];
        var words = new List<Hamming74WordResult>(wordCount);
        var traces = new List<Hamming74WordTrace>(wordCount);
        Span<int> word = stackalloc int[8]; // Índice cero sin usar para respetar posiciones 1–7.
        for (int index = 0; index < wordCount; index++)
        {
            string received = encodedBits.Substring(index * 7, 7);
            int erasures = received.Count(c => c == '?');
            if (erasures > 0)
            {
                // Solo coincidencias exactas con los bits conocidos, que se suponen correctos.
                // No se combina el borrado con la corrección por síndrome.
                string? candidate = null;
                string? candidateWord = null;
                int matches = 0;
                if (erasures <= 2)
                    for (int value = 0; value < 16; value++)
                    {
                        string information = Convert.ToString(value, 2).PadLeft(4, '0');
                        string valid = Encode(information);
                        if (!Enumerable.Range(0, 7).All(p => received[p] == '?' || received[p] == valid[p])) continue;
                        candidate = information;
                        candidateWord = valid;
                        matches++;
                    }
                bool recovered = matches == 1;
                (recovered ? candidate! : "????").CopyTo(0, data, index * 4, 4);
                words.Add(new(index, 0, null) { HasSyndrome = false, ErasureCount = erasures,
                    RecoveredErasureCount = recovered ? erasures : 0, IsDecodable = recovered });
                traces.Add(new(index, received, recovered ? candidateWord : null, recovered ? candidate : null,
                    null, null, Array.AsReadOnly(recovered ? Enumerable.Range(1, 7).Where(p => received[p - 1] == '?').ToArray() : []),
                    recovered ? Hamming74TraceState.ErasuresRecovered : Hamming74TraceState.Unrecoverable,
                    recovered ? "Candidata única compatible con los bits conocidos; comprobar CRC."
                        : erasures > 2 ? "Tres o más borrados: recuperación no admitida." : "No hay una candidata única compatible."));
                continue;
            }
            for (int position = 1; position <= 7; position++)
                word[position] = encodedBits[index * 7 + position - 1] - '0';
            int s1 = word[1] ^ word[3] ^ word[5] ^ word[7];
            int s2 = word[2] ^ word[3] ^ word[6] ^ word[7];
            int s4 = word[4] ^ word[5] ^ word[6] ^ word[7];
            int syndrome = s1 | (s2 << 1) | (s4 << 2);
            if (syndrome != 0) word[syndrome] ^= 1;
            words.Add(new(index, syndrome, syndrome == 0 ? null : syndrome));
            data[index * 4] = ToChar(word[3]);
            data[index * 4 + 1] = ToChar(word[5]);
            data[index * 4 + 2] = ToChar(word[6]);
            data[index * 4 + 3] = ToChar(word[7]);
            var resultWord = new char[7];
            for (int p = 1; p <= 7; p++) resultWord[p - 1] = ToChar(word[p]);
            traces.Add(new(index, received, new string(resultWord),
                new string(data, index * 4, 4), syndrome, syndrome == 0 ? null : syndrome, Array.Empty<int>(),
                syndrome == 0 ? Hamming74TraceState.Unchanged : Hamming74TraceState.BitFlipped,
                syndrome == 0 ? "Síndrome cero; no garantiza ausencia de errores múltiples."
                    : "Inversión indicada por el síndrome; la integridad final depende del CRC."));
        }
        return new(new string(data), words.AsReadOnly()) { Traces = traces.AsReadOnly() };
    }

    private static char ToChar(int bit) => (char)('0' + bit);

    private static void Validate(string bits, int groupSize, string parameter, bool allowErasures = false)
    {
        ArgumentNullException.ThrowIfNull(bits, parameter);
        if (bits.Length == 0 || bits.Length % groupSize != 0)
            throw new ArgumentException($"Se requiere una cadena no vacía de longitud múltiplo de {groupSize}; no se agrega relleno.", parameter);
        if (bits.Any(c => c != '0' && c != '1' && !(allowErasures && c == '?')))
            throw new ArgumentException(allowErasures ? "Solo se admiten caracteres 0, 1 y ?." : "Solo se admiten caracteres 0 y 1.", parameter);
    }
}
