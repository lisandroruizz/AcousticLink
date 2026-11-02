#:property TargetFramework=net10.0-windows
#:project ../AcousticLink.App/AcousticLink.App.csproj

using AcousticLink.App.Signals;

var generator = new FdmBitstreamGenerator();
foreach (var (a, b) in new[] { ("10110010", "01001101"), ("0011", "0101"), ("0", "1"),
    ("0000000000000000", "1111111111111111"), ("1111000011110000", "0011001100110011") })
{
    var samples = generator.Generate(a, b);
    Require(samples.Length == a.Length * 4800, "Cantidad de muestras");
    Require(Math.Abs(samples.Length / 48000.0 - a.Length * 0.1) < 1e-12, "Duración");
    Require(samples.All(x => float.IsFinite(x) && Math.Abs(x) <= 0.15 + 1e-8 && Math.Abs(x) < 1), "Amplitud/recorte");
    Require(samples[0] == 0 && samples[^1] == 0, "Extremos en cero");
    // Referencia analítica: fase inicial de cada símbolo calculada integrando sus frecuencias previas.
    double cyclesA = 0, cyclesB = 0;
    for (int symbol = 0; symbol < a.Length; symbol++)
    {
        double fa = a[symbol] == '0' ? 1200 : 1900;
        double fb = b[symbol] == '0' ? 3000 : 3700;
        for (int j = 0; j < 4800; j++)
        {
            int i = symbol * 4800 + j;
            double envelope = Math.Min(1, Math.Min(i, samples.Length - 1 - i) / 240.0);
            double expected = 0.075 * envelope * (Math.Sin(2 * Math.PI * (cyclesA + fa * j / 48000)) +
                Math.Sin(2 * Math.PI * (cyclesB + fb * j / 48000)));
            Require(Math.Abs(samples[i] - expected) < 1e-7, "Fases independientes, suma y envolvente global");
        }
        cyclesA += fa * 0.1;
        cyclesB += fb * 0.1;
        // Proyecciones independientes sobre el 60 % central: validación espectral, no un receptor nuevo.
        foreach (double f in new[] { 1200.0, 1900, 3000, 3700 })
        {
            double real = 0, imaginary = 0;
            for (int j = 960; j < 3840; j++)
            {
                int i = symbol * 4800 + j;
                real += samples[i] * Math.Cos(2 * Math.PI * f * j / 48000);
                imaginary += samples[i] * Math.Sin(2 * Math.PI * f * j / 48000);
            }
            double amplitude = 2 * Math.Sqrt(real * real + imaginary * imaginary) / 2880;
            Require(Math.Abs(amplitude - (f == fa || f == fb ? 0.075 : 0)) < 1e-6, "Frecuencias por intervalo");
        }
        if (symbol > 0)
            Require(samples.Skip(symbol * 4800 - 48).Take(96).Max(x => Math.Abs(x)) > 0.05,
                "Sin silencio ni rampa intermedia");
    }
    Console.WriteLine($"A={a}, B={b}: {samples.Length} muestras, duración, frecuencias, fases y amplitud: OK");
}
foreach (var (a, b) in new (string?, string?)[] { (null, "0"), ("0", null), ("", ""), ("0", ""),
    ("01", "0"), ("2", "0"), ("0", "x"), ("0 ", "01"), (new string('0', 17), new string('1', 17)) })
{
    try { generator.Generate(a!, b!); }
    catch (ArgumentException) { continue; }
    throw new InvalidOperationException("Se aceptaron datos inválidos.");
}
Console.WriteLine("Validación de entradas y máximo 16 bits: OK");
static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
