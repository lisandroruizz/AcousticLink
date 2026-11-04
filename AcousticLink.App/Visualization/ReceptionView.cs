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
    public static string MessageStatus(FdmTextChannelResult channel) => channel.IsReceiving ? channel.HeaderValid ? "Recibiendo mensaje" : "Buscando cabecera" : channel.State switch
    {
        FdmTextReceiveState.Valid => "Mensaje válido",
        FdmTextReceiveState.CrcIncorrect or FdmTextReceiveState.InvalidUtf8 or FdmTextReceiveState.InvalidLength => "Mensaje inválido",
        FdmTextReceiveState.HeaderNotSynchronized => "Sincronización fallida",
        FdmTextReceiveState.AmbiguousSynchronization => "Sincronización ambigua",
        _ => "Mensaje incompleto"
    };
    public static Brush MessageColor(FdmTextChannelResult channel) => channel.State == FdmTextReceiveState.Valid ? (Brush)Application.Current.FindResource("SuccessBrush")
        : channel.State is FdmTextReceiveState.CrcIncorrect or FdmTextReceiveState.InvalidUtf8 or FdmTextReceiveState.InvalidLength ? (Brush)Application.Current.FindResource("ErrorBrush") : (Brush)Application.Current.FindResource("WarningBrush");
    private static readonly DependencyPropertyKey DiagnosticTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(DiagnosticText), typeof(string), typeof(ReceptionView), new PropertyMetadata("Sin analizar"));
    public static readonly DependencyProperty DiagnosticTextProperty = DiagnosticTextPropertyKey.DependencyProperty;
    public string DiagnosticText => (string)GetValue(DiagnosticTextProperty);
    private static readonly DependencyPropertyKey ResultPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(Result), typeof(FdmTextChannelResult), typeof(ReceptionView), new PropertyMetadata(null));
    public static readonly DependencyProperty ResultProperty = ResultPropertyKey.DependencyProperty;
    public FdmTextChannelResult? Result => (FdmTextChannelResult?)GetValue(ResultProperty);
    private static readonly DependencyPropertyKey SummaryTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(SummaryText), typeof(string), typeof(ReceptionView), new PropertyMetadata("Sin analizar"));
    public static readonly DependencyProperty SummaryTextProperty = SummaryTextPropertyKey.DependencyProperty;
    public string SummaryText => (string)GetValue(SummaryTextProperty);
    private readonly StackPanel panel = new();
    public ReceptionView() { Content = panel; Clear(); }
    public void Clear(string status = "Sin analizar")
    {
        SetValue(ResultPropertyKey, null); SetValue(SummaryTextPropertyKey, status);
        panel.Children.Clear(); panel.Children.Add(Label(status, 22)); SetValue(DiagnosticTextPropertyKey, status);
        if (status == "Sin analizar") panel.Children.Add(Label("Capturá o abrí un WAV y ejecutá Recibir mensajes.", 13));
    }
    private static TextBlock Label(string text, double size = 14) => new() { Text = text, FontSize = size, Margin = new Thickness(0, 2, 0, 2) };
    public void Show(FdmTextChannelResult channel, FdmSynchronizationResult synchronization, string channelName = "sin identificar")
    {
        bool changed = Result?.State != channel.State || Result?.Text != channel.Text;
        SetValue(ResultPropertyKey, channel);
        SetValue(SummaryTextPropertyKey, $"{(channel.State == FdmTextReceiveState.Valid ? channel.Text : MessageStatus(channel))}\nCRC: {ReceptionInspectorView.CrcLabel(channel.CrcValid)}");
        panel.Children.Clear();
        var status = Label(MessageStatus(channel), 16); status.Foreground = MessageColor(channel); status.ToolTip = channel.Reason; panel.Children.Add(status);
        var message = Label(channel.Text ?? "Mensaje no recuperado", 24); message.FontWeight = FontWeights.SemiBold; panel.Children.Add(message);
        string crc = channel.CrcValid is true ? "VÁLIDO" : channel.CrcValid is false ? "INVÁLIDO" : "NO COMPROBADO";
        panel.Children.Add(Label($"CRC: {crc}" + (channel.ByteLength.HasValue ? $" · longitud: {channel.ByteLength} bytes" : "")));
        var h = channel.HammingDiagnostics;
        string sync = channel.HeaderValid ? "Validada" : synchronization.IsAmbiguous ? "Ambigua" : "No validada";
        string data = channel.Block is null ? "No disponible" : channel.State is FdmTextReceiveState.Valid or FdmTextReceiveState.CrcIncorrect or FdmTextReceiveState.InvalidUtf8 ? "Bloque leído" : "Parcial";
        string hamming = !channel.UsesHamming ? "Desactivado" : h is null ? "No disponible" : $"{h.AppliedBitFlipCount} inv. · {h.RecoveredErasureCount} borr.";
        var route = new UniformGrid { Columns = 2, Margin = new Thickness(0, 4, 0, 4) };
        foreach (string value in new[] { "① Sincronía · " + sync, "② Datos · " + data, "③ Hamming · " + hamming, "④ CRC · " + crc })
            route.Children.Add(new TextBlock { Text = value, FontSize = 13, LineHeight = 18, Foreground = value.StartsWith("③") ? (Brush)FindResource("HammingBrush") : (Brush)FindResource("MutedBrush") });
        panel.Children.Add(route);
        var open = new Button { Content = "Explorar texto / trama →", Padding = new Thickness(8, 4, 8, 4), MinHeight = 28 };
        open.Click += (_, _) => TextRequested?.Invoke(); panel.Children.Add(open);
        SetValue(DiagnosticTextPropertyKey, $"Canal {channelName} · {MessageStatus(channel)}\nSincronización: {synchronization.State} · cabecera: {sync}\nDatos: {data} · longitud leída: {channel.ByteLength?.ToString() ?? "No disponible"} bytes\n" +
            $"Concluyentes: {channel.Block?.ConclusiveCount.ToString() ?? "No disponible"} · '?': {channel.Block?.InconclusiveCount.ToString() ?? "No disponible"}\n" +
            (channel.UsesHamming ? $"Hamming activo · recuperación {(channel.ErasureRecoveryEnabled ? "habilitada" : "deshabilitada")} · {h?.WordCount.ToString() ?? "No disponible"} palabras · {hamming} · {(channel.HammingCoversCompleteBlock ? "bloque completo" : "solo longitud o fragmento")}\n" : "Hamming desactivado\n") +
            $"CRC: {crc}\n{channel.Reason}\n{(channel.IsReceiving ? "Procesamiento incremental de muestras recibidas." : "Procesamiento del bloque recibido.")}");
        if (changed) WorkspaceMotion.Reveal(this);
    }
}
