#:property TargetFramework=net10.0-windows
#:project ../AcousticLink.App/AcousticLink.App.csproj

using AcousticLink.App.Signals;

var generator = new BfskToneGenerator();

// Conservar las verificaciones de los tonos aislados.
foreach (bool bit in new[] { false, true })
foreach (double duration in new[] { 0.10, 1.0 })
{
    float[] samples = generator.Generate(bit, duration);
    double expectedFrequency = bit ? 1900 : 1200;
    Require(samples.Length == (int)(duration * 48_000), "Duración incorrecta");
    Require(samples.All(float.IsFinite), "Muestras no finitas");
    double peak = samples.Max(x => Math.Abs((double)x));
    Require(Math.Abs(peak - 0.15) < 1e-6, "Amplitud incorrecta");
    Require(samples[0] == 0 && samples[^1] == 0, "Extremos sin silencio");
    double measuredFrequency = MeasureFrequency(samples, 481, samples.Length - 480);
    Require(Math.Abs(measuredFrequency - expectedFrequency) < 0.1, "Frecuencia incorrecta");
    for (int i = 0; i < 240; i++)
    {
        Require(Math.Abs(samples[i]) <= 0.15 * i / 240 + 1e-7, "Rampa inicial incorrecta");
        Require(Math.Abs(samples[^(i + 1)]) <= 0.15 * i / 240 + 1e-7, "Rampa final incorrecta");
    }
    Console.WriteLine($"Bit {(bit ? 1 : 0)}: {samples.Length} muestras, {duration:F2} s, {measuredFrequency:F3} Hz, pico {peak:F6}: OK");
}
Require(generator.Generate(false).Length == 4800, "Duración predeterminada incorrecta");

const string bits = "10110010";
const int samplesPerBit = 4800;
float[] sequence = generator.GenerateSequence(bits);
Require(sequence.Length == bits.Length * samplesPerBit, "Cantidad de muestras de la secuencia incorrecta");
Require(Math.Abs(sequence.Length / 48_000.0 - 0.8) < 1e-12, "Duración de la secuencia incorrecta");
Require(sequence.All(float.IsFinite), "La secuencia contiene muestras no finitas");
Require(sequence.Max(x => Math.Abs((double)x)) <= 0.15 + 1e-7, "La secuencia supera la amplitud configurada");

// Recalcular toda la señal con un acumulador único valida la fase en cada cambio de símbolo.
double phase = 0;
for (int i = 0; i < sequence.Length; i++)
{
    double frequency = bits[i / samplesPerBit] == '1' ? 1900 : 1200;
    double envelope = Math.Min(1.0, Math.Min(i, sequence.Length - 1 - i) / 240.0);
    double expected = 0.15 * envelope * Math.Sin(phase);
    Require(Math.Abs(sequence[i] - expected) < 1e-6, $"Fase discontinua en la muestra {i}");
    phase = (phase + 2 * Math.PI * frequency / 48_000) % (2 * Math.PI);
}

for (int symbol = 0; symbol < bits.Length; symbol++)
{
    int start = symbol * samplesPerBit;
    double measured = MeasureFrequency(sequence, start + 1, start + samplesPerBit);
    double expected = bits[symbol] == '1' ? 1900 : 1200;
    Require(Math.Abs(measured - expected) < 0.1, $"Frecuencia incorrecta en el bit {symbol}");
    if (symbol > 0)
    {
        int boundary = start;
        Require(sequence.Skip(boundary - 8).Take(17).Any(x => Math.Abs(x) > 0.01f),
            $"Silencio artificial en el límite del bit {symbol}");
    }
}

ExpectArgumentError(() => generator.GenerateSequence(""));
ExpectArgumentError(() => generator.GenerateSequence("1002"));
ExpectArgumentError(() => generator.GenerateSequence(new string('1', 65)));
Console.WriteLine("Secuencia 10110010: 38400 muestras, 0,80 s, fase continua y sin silencios: OK");
Console.WriteLine("Todas las verificaciones pasaron.");

static double MeasureFrequency(float[] samples, int start, int end)
{
    var crossings = new List<double>();
    for (int i = start; i < end; i++)
        if (samples[i - 1] <= 0 && samples[i] > 0)
            crossings.Add(i - 1 - samples[i - 1] / (double)(samples[i] - samples[i - 1]));
    return (crossings.Count - 1) * 48_000 / (crossings[^1] - crossings[0]);
}

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void ExpectArgumentError(Action action)
{
    try
    {
        action();
        throw new InvalidOperationException("Se esperaba un error de validación");
    }
    catch (ArgumentException)
    {
    }
}
