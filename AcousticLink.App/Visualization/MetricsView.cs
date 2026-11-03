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
    public MetricsView() { Content = panel; Clear(); }
    public void Clear(string status = "Sin analizar")
    {
        panel.Children.Clear(); panel.Children.Add(Text(status, 22));
    }
    private static TextBlock Text(string value, double size = 14) => new()
    { Text = value, FontSize = size, Margin = new Thickness(0, 3, 0, 3) };

    public void Show(FdmChannelExperiment channel, bool usesHamming)
    {
        panel.Children.Clear();
        var reception = channel.Reception;
        var status = Text(ReceptionView.MessageStatus(reception), 20);
        status.Foreground = ReceptionView.MessageColor(reception); panel.Children.Add(status);
        panel.Children.Add(Text(reception.Text ?? "Sin texto validado", 24));
        panel.Children.Add(Text($"CRC: {(reception.CrcValid is true ? "VÁLIDO" : reception.CrcValid is false ? "INVÁLIDO" : "NO COMPROBADO")} · cabecera {(reception.HeaderValid ? "válida" : "no validada")}"));
        var metrics = new StackPanel();
        if (usesHamming)
        {
            var selector = new ComboBox { ItemsSource = new[] { "Antes · bits codificados", "Después · bits originales" }, SelectedIndex = 1, Margin = new Thickness(0, 4, 0, 6) };
            selector.SelectionChanged += (_, _) => Comparison(metrics, selector.SelectedIndex == 0 ? channel.Before : channel.After, channel.Channel, true, selector.SelectedIndex == 1);
            panel.Children.Add(selector); Comparison(metrics, channel.After, channel.Channel, true, true);
        }
        else
        {
            panel.Children.Add(Text("Bloque original · Hamming no aplicable")); Comparison(metrics, channel.Before, channel.Channel, false, false);
        }
        panel.Children.Add(metrics);
    }

    private static void Comparison(StackPanel panel, BitComparison? value, string channel, bool usesHamming, bool after)
    {
        panel.Children.Clear();
        if (value is null) { panel.Children.Add(Text("No calculable · sin alineación validada")); return; }
        var indicators = new UniformGrid { Columns = 2 };
        void Indicator(string title, double? number, Brush color)
        {
            var cell = new StackPanel { Margin = new Thickness(0, 0, 12, 4) };
            cell.Children.Add(Text(title));
            cell.Children.Add(Text(number.HasValue ? number.Value.ToString("P2") : "No definida", 26));
            if (number.HasValue) cell.Children.Add(new ProgressBar { Minimum = 0, Maximum = 1, Value = number.Value, Height = 8, Foreground = color,
                ToolTip = number.Value.ToString("G17") });
            indicators.Children.Add(cell);
        }
        Indicator("BER observada", value.Ber, Brushes.Salmon);
        Indicator("Cobertura", value.Coverage, Brushes.LightSkyBlue);
        panel.Children.Add(indicators);
        int[] counts = [value.ComparedBits - value.IncorrectBits, value.IncorrectBits, value.InconclusiveBits, value.MissingBits];
        Brush[] colors = [Brushes.LightGreen, Brushes.Salmon, Brushes.Khaki, Brushes.SlateGray];
        var composition = new Grid { Height = 14, Margin = new Thickness(0, 8, 0, 6) };
        for (int i = 0; i < counts.Length; i++)
        {
            composition.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(counts[i], GridUnitType.Star) });
            var segment = new Border { Background = colors[i] }; Grid.SetColumn(segment, i); composition.Children.Add(segment);
        }
        panel.Children.Add(composition);
        panel.Children.Add(Text($"Correctos {counts[0]} · incorrectos {counts[1]} · '?' {counts[2]} · faltantes {counts[3]}"));
        var map = new BitMapView(); map.ShowComparison(value, channel, usesHamming, after);
        panel.Children.Add(new Expander { Header = $"Explorar {value.ExpectedCount} posiciones · comparados {value.ComparedBits}", Content = map });
    }
}
