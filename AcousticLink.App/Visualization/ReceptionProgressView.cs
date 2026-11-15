using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AcousticLink.App.Visualization;

public sealed class ReceptionProgressView : UserControl
{
    private readonly TextBlock mode = Text(12), timing = Text(11);
    private readonly WrapPanel header = new();
    private readonly System.Windows.Controls.Primitives.UniformGrid channels = new();
    private readonly ChannelPanel a = new("A", Brushes.LightSkyBlue), b = new("B", Brushes.SandyBrown);
    public ReceptionActivity? Activity { get; private set; }
    private bool compact, singleChannel;
    public bool Compact { get => compact; set { compact = value; channels.Columns = value && !singleChannel ? 2 : 1; } }
    public bool SingleChannel
    {
        get => singleChannel;
        set { singleChannel = value; b.Panel.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
            channels.Columns = compact && !value ? 2 : 1; }
    }
    public ReceptionProgressView()
    {
        var panel = new StackPanel { Margin = new Thickness(2, 4, 2, 4) };
        mode.FontWeight = FontWeights.SemiBold;
        timing.TextWrapping = TextWrapping.NoWrap; timing.TextTrimming = TextTrimming.CharacterEllipsis;
        mode.Margin = new Thickness(0, 0, 12, 0);
        header.Children.Add(mode); header.Children.Add(timing); panel.Children.Add(header); panel.Children.Add(channels);
        channels.Children.Add(a.Panel); channels.Children.Add(b.Panel); Compact = false;
        Content = panel; Clear();
    }
    public void Clear() { Activity = null; mode.Text = "Sin recepción analizada"; timing.Text = "Capture audio o importe un WAV."; a.Show(ReceptionChannelActivity.From(null, false)); b.Show(ReceptionChannelActivity.From(null, false)); }
    public void Show(ReceptionActivity activity)
    {
        if (Activity == activity) return;
        Activity = activity; mode.Text = activity.Mode;
        timing.Text = Compact ? $"Recibido {activity.ReceivedSamples / (double)activity.SampleRate:0.00} s · procesado {activity.ProcessedSamples / (double)activity.SampleRate:0.00} s" : $"{activity.Source} · {activity.SampleRate:N0} Hz · recibido {activity.ReceivedSamples / (double)activity.SampleRate:0.00} s · procesado {activity.ProcessedSamples / (double)activity.SampleRate:0.00} s";
        timing.ToolTip = timing.Text;
        a.Show(activity.ChannelA); b.Show(activity.ChannelB);
    }
    private static TextBlock Text(double size) => new() { FontSize = size, Foreground = Brushes.LightGray,
        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 1) };
    private sealed class ChannelPanel
    {
        public StackPanel Panel { get; } = new() { Margin = new Thickness(0, 4, 10, 2) };
        private readonly TextBlock stage = Text(14), counts = Text(12), treatment = Text(12), message = Text(18);
        private readonly ProgressBar progress = new() { Minimum = 0, Maximum = 100, Height = 4, Margin = new Thickness(0, 3, 0, 0) };
        private readonly string name;
        public ChannelPanel(string name, Brush color)
        {
            this.name = name; stage.Foreground = color;
            Panel.Children.Add(stage); Panel.Children.Add(counts); Panel.Children.Add(treatment); Panel.Children.Add(progress); Panel.Children.Add(message);
        }
        public void Show(ReceptionChannelActivity data)
        {
            stage.Text = $"{name} · {data.Stage}" + (data.ByteLength.HasValue ? $" · {data.ByteLength} bytes" : " · longitud pendiente");
            counts.Text = $"Cabecera {(data.HeaderValid ? "✓" : "sin validar")} · 0/1: {data.ConclusiveBits} · ?: {data.UnknownBits} · bloque {data.AvailableBits}" +
                (data.ExpectedBits is int expected ? $"/{expected} bits ({data.Percent:0.00} %)" : " bits · tamaño pendiente");
            treatment.Text = (data.UsesHamming ? $"Hamming: {data.HammingWords} palabras · {data.Inversions} inv. · {data.RecoveredErasures} borr. rec. · " : "Hamming no aplicado · ") + $"CRC {ReceptionInspectorView.CrcLabel(data.CrcValid)}";
            treatment.Foreground = data.CrcValid == true ? Brushes.LightGreen : data.CrcValid == false ? Brushes.Salmon : Brushes.LightGray;
            progress.Visibility = data.Percent.HasValue ? Visibility.Visible : Visibility.Collapsed;
            progress.Value = data.Percent ?? 0;
            message.Text = data.ValidatedText ?? ""; message.Visibility = data.ValidatedText is null ? Visibility.Collapsed : Visibility.Visible;
            counts.ToolTip = counts.Text; treatment.ToolTip = treatment.Text;
        }
    }
}
