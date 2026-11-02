namespace AcousticLink.App.Signals;

internal static class GoertzelEnergy
{
    // Los detectores validan muestras, tamaño y tasa antes de llamar a este cálculo compartido.
    public static double Estimate(ReadOnlySpan<float> samples, double mean, int sampleRate, double frequency)
    {
        // Frecuencia nominal exacta, sin redondearla a un bin de FFT.
        double coefficient = 2 * Math.Cos(2 * Math.PI * frequency / sampleRate);
        double previous = 0, previous2 = 0;
        foreach (float sample in samples)
        {
            double current = sample - mean + coefficient * previous - previous2;
            previous2 = previous;
            previous = current;
        }
        double magnitudeSquared = Math.Max(0, previous * previous + previous2 * previous2 - coefficient * previous * previous2);
        // Potencia media estimada: un seno de amplitud A produce aproximadamente A²/2.
        return 2 * magnitudeSquared / ((double)samples.Length * samples.Length);
    }
}
