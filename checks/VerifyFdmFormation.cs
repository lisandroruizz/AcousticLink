#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property PublishAot=false
#:property PublishTrimmed=false
#:project ../AcousticLink.App/AcousticLink.App.csproj

using AcousticLink.App.Protocol;
using AcousticLink.App.Visualization;

foreach (bool hamming in new[] { false, true })
foreach (string text in new[] { "a", "Hola", "Mundo", "ñ", "áé", "🎵🎵", "12345678" })
{
    var frames = new FdmTextFrameBuilder().Build(text, "a", hamming);
    var info = new FdmTransmissionInfo(frames.ChannelA.TransmittedBits, frames.ChannelB.TransmittedBits, .1, frames);
    foreach (bool b in new[] { false, true })
    {
        var frame = b ? frames.ChannelB : frames.ChannelA;
        var fields = info.Segments(b);
        Require(fields[0].Name == "Preámbulo" && fields[0].SymbolCount == 16 && !fields[0].Protected, "Preámbulo sin codificar");
        Require(fields[1].FirstSymbol == 16 && fields[1].SymbolCount == 8 && !fields[1].Protected, "Marcador sin codificar");
        int factor = hamming ? 7 : 4;
        Require(fields[2].FirstSymbol == 24 && fields[2].SymbolCount == 2 * factor, "Longitud 8/14 símbolos");
        Require(fields[3].SymbolCount == frame.Utf8ByteCount * 2 * factor && fields[4].SymbolCount == 8 * factor, "UTF-8 y CRC reales");
        Require(fields.Skip(2).Take(3).All(f => f.Protected == hamming), "Solo longitud/datos/CRC protegidos");
        Require(fields.Sum(f => f.SymbolCount) == frame.TransmittedBits.Length && fields[^1].EndSymbol == frame.TransmittedBits.Length, "Relleno fuera del bloque protegido");
        for (int i = 1; i < fields.Count; i++) Require(fields[i].FirstSymbol == fields[i - 1].EndSymbol, "Sin huecos ni superposición");
        for (int i = 0; i < frame.TransmittedBits.Length; i++)
        {
            var tone = info.Tone(b, (i + .5) * .1);
            Require(tone.Bit == frame.TransmittedBits[i], "Bit proviene del emisor");
            Require(tone.Frequency == (b ? tone.Bit == '0' ? 3000 : 3700 : tone.Bit == '0' ? 1200 : 1900), "Frecuencia nominal");
            Require(info.SymbolAt(i * .1) == i, "Límite absoluto de símbolo sin deriva");
        }
    }
}
Require(new FdmTransmissionInfo("01", "10", .1).Segments(false).Count == 0, "Sin inventar campos en secuencias directas");
Console.WriteLine("FDM didáctico: símbolos, frecuencias, cabecera, UTF-8, CRC, Hamming, relleno y rangos: OK");
static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
