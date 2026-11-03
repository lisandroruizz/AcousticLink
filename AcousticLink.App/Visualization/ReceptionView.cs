using System.Windows.Controls;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using AcousticLink.App.Protocol;
using AcousticLink.App.Signals;

namespace AcousticLink.App.Visualization;

public sealed class ReceptionView : UserControl
{
    public event Action? TextRequested;
    public static string MessageStatus(FdmTextChannelResult channel) => channel.State switch
    {
        FdmTextReceiveState.Valid => "Mensaje válido",
        FdmTextReceiveState.CrcIncorrect or FdmTextReceiveState.InvalidUtf8 or FdmTextReceiveState.InvalidLength => "Mensaje inválido",
        FdmTextReceiveState.HeaderNotSynchronized => "Sincronización fallida",
        FdmTextReceiveState.AmbiguousSynchronization => "Sincronización ambigua",
        _ => "Mensaje incompleto"
    };
    public static Brush MessageColor(FdmTextChannelResult channel) => channel.State == FdmTextReceiveState.Valid ? Brushes.LightGreen
        : channel.State is FdmTextReceiveState.CrcIncorrect or FdmTextReceiveState.InvalidUtf8 or FdmTextReceiveState.InvalidLength ? Brushes.Salmon : Brushes.Khaki;
    private readonly StackPanel panel = new();
    public ReceptionView() { Content = panel; Clear(); }
    public void Clear(string status = "Sin analizar")
    {
        panel.Children.Clear();
        panel.Children.Add(new TextBlock { Text = status, FontSize = 22 });
    }
    public void Show(FdmTextChannelResult channel, FdmSynchronizationResult synchronization, string channelName = "sin identificar")
    {
        panel.Children.Clear();
        var color = MessageColor(channel);
        void Row(string text, double size = 14) => panel.Children.Add(new TextBlock
        { Text = text, FontSize = size, Margin = new System.Windows.Thickness(0, 2, 0, 2) });
        panel.Children.Add(new Border { Child = new TextBlock { Text = MessageStatus(channel), Foreground = color, FontSize = 18 },
            BorderBrush = color, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 4, 8, 4), HorizontalAlignment = HorizontalAlignment.Left });
        Row(channel.Text ?? "Mensaje no recuperado", 28);
        if (channel.Text is null) Row(channel.State switch
        {
            FdmTextReceiveState.HeaderNotSynchronized => "No se validó la cabecera del canal.",
            FdmTextReceiveState.AmbiguousSynchronization => "Hay varias cabeceras candidatas.",
            FdmTextReceiveState.CrcIncorrect => "El bloque no superó la verificación de integridad.",
            FdmTextReceiveState.InvalidUtf8 => "CRC válido, pero el contenido no es UTF-8 válido.",
            FdmTextReceiveState.InvalidLength => "La longitud recuperada está fuera del rango admitido.",
            FdmTextReceiveState.Truncated => "La grabación no alcanza para recuperar el bloque.",
            _ => "Persisten símbolos o palabras no concluyentes."
        });
        Row($"CRC: {(channel.CrcValid is true ? "VÁLIDO" : channel.CrcValid is false ? "INVÁLIDO" : "NO COMPROBADO")}" +
            (channel.ByteLength.HasValue ? $" · longitud leída: {channel.ByteLength} bytes" : ""), 18);
        Row("Procesamiento posterior de la grabación", 12);
        var route = new UniformGrid { Columns = 4, Margin = new Thickness(0, 6, 0, 8) };
        void Stage(string title, string value, Brush brush)
        {
            var cell = new StackPanel();
            cell.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold });
            cell.Children.Add(new TextBlock { Text = value, Foreground = brush, FontSize = 14 });
            route.Children.Add(new Border { Child = cell, BorderBrush = brush, BorderThickness = new Thickness(0, 3, 0, 0), Padding = new Thickness(5, 6, 5, 4), Margin = new Thickness(0, 0, 5, 0) });
        }
        Stage("① Sincronía", channel.HeaderValid ? "Cabecera válida" : synchronization.IsAmbiguous ? "Ambigua" : "No validada",
            channel.HeaderValid ? Brushes.LightGreen : Brushes.Khaki);
        Stage("② Datos", channel.Block is { } block ?
            (channel.State is FdmTextReceiveState.Valid or FdmTextReceiveState.CrcIncorrect or FdmTextReceiveState.InvalidUtf8 ? "Bloque leído\n" : "Fragmento\n") +
            $"{block.ConclusiveCount} bits · {block.InconclusiveCount} '?' disponibles"
            : channel.State == FdmTextReceiveState.Truncated ? "Insuficientes" : "No disponible", Brushes.LightSkyBlue);
        var h = channel.HammingDiagnostics;
        Stage("③ Hamming", !channel.UsesHamming ? "Desactivado" : h is null ? "No disponible"
            : $"{h.WordCount} palabras\n{h.AppliedBitFlipCount} inversiones\n{h.RecoveredErasureCount} borrados" + (channel.HammingCoversCompleteBlock ? "" : "\nSolo longitud"), Brushes.LightSkyBlue);
        Stage("④ CRC", channel.CrcValid is true ? "Verificado" : channel.CrcValid is false ? "Inválido" : "No comprobado",
            channel.CrcValid is true ? Brushes.LightGreen : channel.CrcValid is false ? Brushes.Salmon : Brushes.Khaki);
        panel.Children.Add(route);
        var openText = new Button { Content = "Texto / UTF-8 · explorar bytes", Margin = new Thickness(0, 4, 0, 4) };
        openText.Click += (_, _) => TextRequested?.Invoke();
        panel.Children.Add(openText);
        panel.Children.Add(new Expander { Header = "Diagnóstico del canal", Content = new TextBlock { Text = $"Estado: {channel.State}\nSincronización global: {synchronization.State}\n" +
            (channel.UsesHamming ? $"Hamming activo · recuperación de borrados {(channel.ErasureRecoveryEnabled ? "sí" : "no")}\n" : "Sin Hamming\n") + channel.Reason } });
    }
}
