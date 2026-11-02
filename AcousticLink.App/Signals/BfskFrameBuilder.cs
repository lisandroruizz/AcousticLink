namespace AcousticLink.App.Signals;

// Trama de prueba: todavía no representa el formato completo del preproyecto.
public sealed class BfskFrameBuilder
{
    // El preproyecto fija longitudes 16/8, pero deja los patrones exactos para ensayos.
    // Centralizados para que el futuro receptor comparta estos mismos valores.
    public const string Preamble = "1010101010101010";
    public const string StartMarker = "11010011"; // Provisional; pendiente de validación física.
    public const int MaximumDataBits = 16;

    public string Build(string dataBits)
    {
        ArgumentNullException.ThrowIfNull(dataBits);
        if (dataBits.Length < 1 || dataBits.Length > MaximumDataBits)
            throw new ArgumentException("La trama de prueba requiere entre 1 y 16 bits de datos.", nameof(dataBits));
        if (dataBits.Any(bit => bit != '0' && bit != '1'))
            throw new ArgumentException("Los datos solo pueden contener caracteres 0 y 1.", nameof(dataBits));
        return Preamble + StartMarker + dataBits;
    }
}
