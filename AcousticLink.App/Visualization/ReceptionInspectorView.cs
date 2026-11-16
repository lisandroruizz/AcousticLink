using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AcousticLink.App.Protocol;

namespace AcousticLink.App.Visualization;

// Inspector de solo lectura: presenta el resultado del receptor, sin interpretar referencias.
public sealed class ReceptionInspectorView : UserControl
{
    public static readonly DependencyProperty ResultProperty = DependencyProperty.Register(nameof(Result),
        typeof(FdmTextChannelResult), typeof(ReceptionInspectorView), new PropertyMetadata(null, (d, _) => ((ReceptionInspectorView)d).Render()));
    public FdmTextChannelResult? Result { get => (FdmTextChannelResult?)GetValue(ResultProperty); set => SetValue(ResultProperty, value); }
    private string channel = "A";
    public string Channel { get => channel; set { channel = value; Render(); } }
    private readonly StackPanel panel = new();

    public ReceptionInspectorView() { Content = panel; Render(); }
    public static string CrcLabel(bool? valid) => valid is true ? "Válido" : valid is false ? "Inválido" : "No comprobado";
    private static TextBlock Text(string value, double size = 14) => new() { Text = value, FontSize = size, Margin = new Thickness(0, 2, 0, 2) };
    private void Section(string title)
    {
        var heading = Text(title, 13); heading.FontWeight = FontWeights.SemiBold;
        heading.Foreground = (Brush)FindResource("MutedBrush"); heading.Margin = new Thickness(0, 12, 0, 4); panel.Children.Add(heading);
    }
    private void Property(string label, string value, Brush? color = null, string? hint = null)
    {
        var row = new Grid { Margin = new Thickness(0, 3, 0, 3), ToolTip = hint };
        row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        var caption = Text(label, 13); caption.Foreground = (Brush)FindResource("MutedBrush"); row.Children.Add(caption);
        var content = Text(value); content.FontWeight = FontWeights.SemiBold; content.Margin = new Thickness(8, 2, 0, 2);
        if (color is not null) content.Foreground = color;
        Grid.SetColumn(content, 1); row.Children.Add(content); panel.Children.Add(row);
    }
    private void Render()
    {
        panel.Children.Clear();
        var context = Text($"CANAL {Channel} · INSPECTOR", 14); context.FontWeight = FontWeights.SemiBold;
        context.Foreground = (Brush)FindResource(Channel == "B" ? "ChannelBBrush" : "ChannelABrush"); panel.Children.Add(context);
        if (Result is not { } result)
        {
            panel.Children.Add(Text("Sin analizar", 20));
            panel.Children.Add(Text("Capturá o abrí un WAV y elegí Analizar grabación. Aquí aparecerá el resultado de este canal.", 13)); return;
        }
        var status = Text(ReceptionView.MessageStatus(result), 20); status.Foreground = ReceptionView.MessageColor(result); status.ToolTip = result.Reason; panel.Children.Add(status);
        if (result.State == FdmTextReceiveState.Valid && result.CrcValid == true && result.Text is not null)
            panel.Children.Add(Text(result.Text, 24));
        Section("INTEGRIDAD CRC-32");
        Property("CRC", CrcLabel(result.CrcValid), (Brush)FindResource(result.CrcValid is true ? "SuccessBrush" : result.CrcValid is false ? "ErrorBrush" : "WarningBrush"),
            "Comprueba el byte de longitud y los bytes UTF-8. No comprobado no significa inválido.");
        Section("SINCRONIZACIÓN");
        Property("Cabecera", result.HeaderValid ? "Validada" : result.State == FdmTextReceiveState.AmbiguousSynchronization ? "Ambigua" : "No validada",
            result.HeaderValid ? null : (Brush)FindResource("WarningBrush"), "Preámbulo y marcador de este canal. Una cabecera válida no verifica el mensaje.");
        Section("DATOS");
        Property("Longitud leída", result.ByteLength is int n ? $"{n} bytes UTF-8" : "No disponible");
        Property("Concluyentes", result.Block?.ConclusiveCount.ToString() ?? "No disponible");
        Property("Símbolos ?", result.Block?.InconclusiveCount.ToString() ?? "No disponible", hint: "Decisiones no concluyentes del detector; no son inversiones ni errores comparados.");
        Section("HAMMING (7,4)");
        Property("Modalidad", result.UsesHamming ? "Activado" : "Desactivado", result.UsesHamming ? (Brush)FindResource("HammingBrush") : null);
        if (result.UsesHamming)
        {
            Property("Borrados", result.ErasureRecoveryEnabled ? "Recuperación activa" : "Recuperación desactivada");
            if (result.HammingDiagnostics is { } h)
            {
                Property("Palabras", h.WordCount.ToString());
                Property("Inversiones", h.AppliedBitFlipCount.ToString(), hint: "Inversiones intentadas por síndrome; no equivalen a errores reales demostrados.");
                Property("Borrados rec.", h.RecoveredErasureCount.ToString());
                Property("Alcance", result.HammingCoversCompleteBlock ? "Bloque completo" : "Longitud / fragmento");
            }
            else Property("Procesamiento", "No disponible");
        }
        var reason = Text(result.Reason, 13); reason.Foreground = (Brush)FindResource("MutedBrush");
        reason.Margin = new Thickness(0, 12, 0, 4); panel.Children.Add(reason);
        WorkspaceMotion.Reveal(panel);
    }
}
