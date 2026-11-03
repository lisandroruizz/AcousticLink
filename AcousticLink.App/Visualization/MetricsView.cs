using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using AcousticLink.App.Experiments;
using AcousticLink.App.Protocol;

namespace AcousticLink.App.Visualization;

public sealed class MetricsView : UserControl
{
    private readonly StackPanel panel = new();
    private readonly Grid metrics = new();
    public MetricsView()
    {
        var layout = new Grid(); layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); layout.RowDefinitions.Add(new RowDefinition());
        layout.Children.Add(panel); Grid.SetRow(metrics, 1); layout.Children.Add(metrics); Content = layout;
        metrics.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); metrics.RowDefinitions.Add(new RowDefinition()); Clear();
    }
    public void Clear(string status = "Sin analizar")
    {
        panel.Children.Clear(); metrics.Children.Clear(); panel.Children.Add(Text(status, 22));
    }
    private static TextBlock Text(string value, double size = 14) => new()
    { Text = value, FontSize = size, Margin = new Thickness(0, 3, 0, 3) };

    public void Show(FdmChannelExperiment channel, bool usesHamming)
    {
        panel.Children.Clear();
        var reception = channel.Reception;
        var outcome = new WrapPanel();
        var status = Text(ReceptionView.MessageStatus(reception), 16);
        status.Foreground = ReceptionView.MessageColor(reception);
        var message = Text(reception.Text ?? "Sin texto validado", 24); message.Margin = new Thickness(0, 3, 12, 3);
        outcome.Children.Add(message); outcome.Children.Add(status); panel.Children.Add(outcome);
        panel.Children.Add(Text($"CRC: {(reception.CrcValid is true ? "VÁLIDO" : reception.CrcValid is false ? "INVÁLIDO" : "NO COMPROBADO")} · cabecera {(reception.HeaderValid ? "válida" : "no validada")}", 12));
        if (usesHamming)
        {
            var selector = new ComboBox { ItemsSource = new[] { "Antes · bits codificados", "Después · bits originales" }, SelectedIndex = 1, Margin = new Thickness(0, 4, 0, 6) };
            selector.SelectionChanged += (_, _) => Comparison(metrics, selector.SelectedIndex == 0 ? channel.Before : channel.After, channel, true, selector.SelectedIndex == 1);
            panel.Children.Add(selector); Comparison(metrics, channel.After, channel, true, true);
        }
        else
        {
            panel.Children.Add(Text("Bloque original · Hamming no aplicable")); Comparison(metrics, channel.Before, channel, false, false);
        }
        WorkspaceMotion.Reveal(this);
    }

    private static void Comparison(Grid panel, BitComparison? value, FdmChannelExperiment channel, bool usesHamming, bool after)
    {
        panel.Children.Clear();
        if (value is null) { panel.Children.Add(Text("No calculable · sin alineación validada")); return; }
        var indicators = new UniformGrid { Columns = 2 };
        void Indicator(string title, double? number, Brush color)
        {
            var cell = new StackPanel { Margin = new Thickness(0, 0, 12, 4) };
            var caption = Text(title); caption.ToolTip = title.StartsWith("BER") ? "Bits incorrectos / bits comparados. Los símbolos ? y faltantes no cuentan como errores; una BER cero no verifica el mensaje." : "Bits comparados / bits esperados. No equivale a éxito de recepción ni a CRC válido."; cell.Children.Add(caption);
            var valueText = Text(number.HasValue ? number.Value.ToString("P2") : "No definida", number.HasValue ? 28 : 20);
            if (number.HasValue) valueText.Style = (Style)Application.Current.FindResource("MetricValue");
            cell.Children.Add(valueText);
            if (number.HasValue) cell.Children.Add(new ProgressBar { Minimum = 0, Maximum = 1, Value = number.Value, Height = 8, Foreground = color,
                ToolTip = number.Value.ToString("G17") });
            indicators.Children.Add(cell);
        }
        Indicator("BER observada", value.Ber, Brushes.Salmon);
        Indicator("Cobertura", value.Coverage, Brushes.LightSkyBlue);
        panel.Children.Add(indicators);
        var distribution = new BitDistributionView();
        distribution.Show(value, channel.Channel, usesHamming, after, channel.Hamming?.Traces, channel.Reception);
        Grid.SetRow(distribution, 1);
        panel.Children.Add(distribution);
    }
}
