#:property TargetFramework=net10.0-windows
#:project ../AcousticLink.App/AcousticLink.App.csproj

using AcousticLink.App.Protocol;

var codec = new Hamming74Codec();
Require(codec.Encode("1011") == "0110011", "Vector 1011 → 0110011");
for (int value = 0; value < 16; value++)
{
    string data = Convert.ToString(value, 2).PadLeft(4, '0');
    string encoded = codec.Encode(data);
    Require(encoded.Length == 7, "Longitud de palabra");
    Require(encoded[2] == data[0] && encoded[4] == data[1] && encoded[5] == data[2] && encoded[6] == data[3], "Distribución MSB-first");
    // Comprobar paridad con sumas sobre los grupos convencionales, independiente del codificador.
    foreach (int[] positions in new[] { new[] { 1, 3, 5, 7 }, new[] { 2, 3, 6, 7 }, new[] { 4, 5, 6, 7 } })
        Require(positions.Sum(p => encoded[p - 1] - '0') % 2 == 0, "Paridad par");
    var clean = codec.Decode(encoded);
    Require(clean.DataBits == data && clean.WordCount == 1 && clean.ZeroSyndromeWordCount == 1 && clean.CorrectedWordCount == 0,
        "Decodificación sin síndrome");
    for (int position = 1; position <= 7; position++)
    {
        var corrected = codec.Decode(Flip(encoded, position - 1));
        Require(corrected.DataBits == data, "Corrección de error único");
        Require(corrected.Words[0].Syndrome == position && corrected.Words[0].FlippedPosition == position && corrected.Words[0].WordIndex == 0,
            "Síndrome y posición corregida");
        Require(corrected.CorrectedWordCount == 1 && corrected.AppliedBitFlipCount == 1 && corrected.ZeroSyndromeWordCount == 0, "Conteos");
    }
    // Dos errores distintos: el síndrome equivale al XOR de sus posiciones; se invierte un tercer bit.
    for (int first = 1; first <= 7; first++)
    for (int second = first + 1; second <= 7; second++)
    {
        var multiple = codec.Decode(Flip(Flip(encoded, first - 1), second - 1));
        Require(multiple.Words[0].Syndrome == (first ^ second), "Síndrome de dos errores");
        Require(multiple.DataBits != data, "Dos errores no se corrigen de forma garantizada");
    }
}
Console.WriteLine("16 combinaciones, 112 errores únicos y 336 casos de error doble: OK");
var messageCodec = new MessageCodec();
foreach (string message in new[] { "Hola", "ñá🎵", new string('a', 255) })
{
    string block = messageCodec.Encode(message);
    string encoded = codec.Encode(block);
    Require(encoded.Length == block.Length / 4 * 7, "Redundancia 7/4");
    Require(messageCodec.Decode(codec.Decode(encoded).DataBits) == message, "Bloque y CRC intactos");
    char[] corrupted = encoded.ToCharArray();
    int count = encoded.Length / 7;
    for (int word = 0; word < count; word++)
    {
        int position = word % 7 + 1;
        int index = word * 7 + position - 1;
        corrupted[index] = corrupted[index] == '0' ? '1' : '0';
    }
    var recovered = codec.Decode(new string(corrupted));
    Require(recovered.DataBits == block && messageCodec.Decode(recovered.DataBits) == message, "Un error por palabra y CRC válido");
    Require(recovered.CorrectedWordCount == count && recovered.AppliedBitFlipCount == count && recovered.ZeroSyndromeWordCount == 0, "Conteos de secuencia larga");
    for (int i = 0; i < count; i++) Require(recovered.Words[i].FlippedPosition == i % 7 + 1, "Diagnóstico por palabra");
}
string mixed = codec.Encode("000010111111");
var mixedResult = codec.Decode(Flip(mixed, 7 + 4));
Require(mixedResult.ZeroSyndromeWordCount == 2 && mixedResult.CorrectedWordCount == 1 && mixedResult.Words[1].FlippedPosition == 5, "Conteo mixto");
string messageBlock = messageCodec.Encode("Hola");
// Doble error en una palabra del contenido; Hamming aplica una corrección equivocada y CRC lo rechaza.
string doubleError = Flip(Flip(codec.Encode(messageBlock), 14), 15);
var miscorrected = codec.Decode(doubleError);
Require(miscorrected.DataBits != messageBlock, "Error residual tras corrección equivocada");
try { messageCodec.Decode(miscorrected.DataBits); throw new InvalidOperationException("CRC aceptó el caso de prueba alterado"); }
catch (FormatException) { }
// Tres errores pueden producir síndrome cero: no confundirlo con integridad demostrada.
var undetected = codec.Decode(Flip(Flip(Flip(codec.Encode("0000"), 0), 1), 2));
Require(undetected.ZeroSyndromeWordCount == 1 && undetected.DataBits != "0000", "Límite de síndrome cero");
foreach (string? input in new string?[] { null, "", "0", "101", "10101", "10?1", "10 1", "000\n" })
    ExpectInvalid(() => codec.Encode(input!));
foreach (string? input in new string?[] { null, "", "0", "011001", "01100110", "01100?1", "01100 1" })
    ExpectInvalid(() => codec.Decode(input!));
Console.WriteLine("Secuencias, MessageCodec/CRC, límites de errores múltiples y validaciones: OK");

static string Flip(string bits, int index) => bits[..index] + (bits[index] == '0' ? "1" : "0") + bits[(index + 1)..];
static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
static void ExpectInvalid(Action action)
{
    try { action(); }
    catch (ArgumentException) { return; }
    throw new InvalidOperationException("Se esperaba rechazo de entrada inválida.");
}
