namespace AcousticLink.App.Experiments;

internal static class CsvText
{
    // Excel y otras hojas pueden interpretar como fórmula el primer carácter visible,
    // incluso si el campo CSV está entre comillas o precedido por espacios/controles.
    public static string Safe(string? value)
    {
        value ??= "";
        int first = 0;
        while (first < value.Length && (char.IsWhiteSpace(value[first]) || char.IsControl(value[first]))) first++;
        return first < value.Length && value[first] is '=' or '+' or '-' or '@' ? "'" + value : value;
    }
}
