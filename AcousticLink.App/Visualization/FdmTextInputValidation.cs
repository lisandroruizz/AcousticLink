using System.Text;
using AcousticLink.App.Protocol;

namespace AcousticLink.App.Visualization;

public readonly record struct FdmTextInputCheck(int? ByteCount, string? Error);

// Validación de presentación del modo acústico; no cambia el límite general de MessageCodec.
public static class FdmTextInputValidation
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    public static FdmTextInputCheck Check(string text, string channel)
    {
        try
        {
            int count = StrictUtf8.GetByteCount(text);
            string? error = count == 0 ? $"Canal {channel}: ingresá un mensaje de 1 a {FdmTextFrameBuilder.MaximumUtf8Bytes} bytes UTF-8."
                : count > FdmTextFrameBuilder.MaximumUtf8Bytes ? $"Canal {channel}: el mensaje ocupa {count} bytes UTF-8; el máximo FDM es {FdmTextFrameBuilder.MaximumUtf8Bytes}." : null;
            return new(count, error);
        }
        catch (EncoderFallbackException)
        {
            return new(null, $"Canal {channel}: el texto contiene un carácter Unicode incompleto o inválido.");
        }
    }
}
