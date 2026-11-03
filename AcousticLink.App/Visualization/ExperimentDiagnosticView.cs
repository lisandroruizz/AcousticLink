using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using AcousticLink.App.Experiments;
using AcousticLink.App.Signals;

namespace AcousticLink.App.Visualization;

// Inspector y comparación de resultados existentes. Nunca devuelve referencias al receptor.
public sealed class ExperimentDiagnosticView : UserControl
{
    private readonly TextBlock source = new(), metadata = new(), states = new(), fragmentTitle = new();
    private readonly ComboBox channels = new() { ItemsSource = new[] { "Canal A", "Canal B" }, SelectedIndex = 0, MinWidth = 135 };
    private readonly ComboBox modes = new() { ItemsSource = new[] { "Recibidos · bloque transmitido", "Reconstruidos · bloque original" }, SelectedIndex = 0, MinWidth = 240 };
    private readonly StackPanel properties = new();
    private readonly BitDistributionView distribution = new();
    private readonly StackPanel fragments = new();
    private FdmExperimentResult? measurement;
    private FdmChannelExperiment? Selected => measurement is null ? null : channels.SelectedIndex == 1 ? measurement.ChannelB : measurement.ChannelA;

    public ExperimentDiagnosticView()
    {
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); Content = root;
        var heading = new StackPanel(); source.TextTrimming = TextTrimming.CharacterEllipsis; source.TextWrapping = TextWrapping.NoWrap;
        source.FontWeight = FontWeights.SemiBold; metadata.FontSize = 13; metadata.Foreground = (Brush)FindResource("MutedBrush");
        states.FontSize = 13; heading.Children.Add(source); heading.Children.Add(metadata); heading.Children.Add(states); root.Children.Add(heading);
        var tools = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) };
        channels.Margin = new Thickness(0, 0, 8, 0); tools.Children.Add(channels); tools.Children.Add(modes);
        tools.Children.Add(new TextBlock { Text = "Referencias solo para medición · sin cabecera ni relleno", FontSize = 13, Margin = new Thickness(10, 8, 0, 0), Foreground = (Brush)FindResource("WarningBrush") });
        Grid.SetRow(tools, 1); root.Children.Add(tools);
        var workspace = new Grid(); workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260), MinWidth = 210, MaxWidth = 360 });
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) }); workspace.ColumnDefinitions.Add(new ColumnDefinition());
        workspace.Children.Add(new ScrollViewer { Content = properties, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var splitter = new GridSplitter { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
            Background = (Brush)FindResource("LineBrush"), ResizeDirection = GridResizeDirection.Columns, ResizeBehavior = GridResizeBehavior.PreviousAndNext, ShowsPreview = true, Focusable = true };
        Grid.SetColumn(splitter, 1); workspace.Children.Add(splitter);
        var comparison = new TabControl { Margin = new Thickness(10, 0, 0, 0), Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            ItemContainerStyle = (Style)FindResource("WorkspaceToolTab") };
        comparison.Items.Add(new TabItem { Header = "Mapa de diferencias", Content = distribution });
        var selected = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12) };
        fragmentTitle.FontWeight = FontWeights.SemiBold; fragmentTitle.FontSize = 14;
        selected.Children.Add(fragmentTitle); selected.Children.Add(fragments);
        comparison.Items.Add(new TabItem { Header = "Fragmento seleccionado", Content = new ScrollViewer { Content = selected, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        comparison.SelectedIndex = 0;
        comparison.SelectionChanged += (_, e) => { if (ReferenceEquals(e.OriginalSource, comparison) && comparison.SelectedContent is UIElement content) WorkspaceMotion.Reveal(content); };
        Grid.SetColumn(comparison, 2); workspace.Children.Add(comparison); Grid.SetRow(workspace, 2); root.Children.Add(workspace);
        channels.SelectionChanged += (_, _) => RenderChannel(); modes.SelectionChanged += (_, _) => RenderChannel();
        distribution.PositionSelected += _ => RenderFragment(); Clear();
    }

    public void Clear(string status = "Sin medición")
    {
        measurement = null; source.Text = status; source.ToolTip = null;
        metadata.Text = "Medí una grabación para consultar propiedades y comparar fragmentos reales.";
        states.Text = "";
        properties.Children.Clear(); distribution.Clear(); fragments.Children.Clear(); fragmentTitle.Text = "Seleccioná un grupo del mapa después de medir una grabación.";
        channels.IsEnabled = modes.IsEnabled = false;
    }
    public void Show(FdmExperimentResult result, int? sampleCount = null)
    {
        measurement = result; source.Text = result.AudioSource; source.ToolTip = result.AudioSource;
        metadata.Text = $"{result.AudioDurationSeconds:0.000} s · {result.SampleRate} Hz · muestras: {sampleCount?.ToString() ?? "no disponibles"}\n" +
            $"Hamming {(result.UsesHamming ? "activado" : "desactivado")} · borrados {(result.ErasureRecoveryEnabled ? "habilitados" : "deshabilitados")} · perfil declarado: " +
            (result.DeclaredAmplitudeProfile.HasValue ? FdmAmplitudeProfiles.Get(result.DeclaredAmplitudeProfile.Value).Label : "no informado") +
            $" · UTC {result.AnalyzedAt:yyyy-MM-dd HH:mm:ss}";
        states.Text = $"A: {ReceptionView.MessageStatus(result.ChannelA.Reception)} · CRC {ReceptionInspectorView.CrcLabel(result.ChannelA.Reception.CrcValid)}  |  " +
            $"B: {ReceptionView.MessageStatus(result.ChannelB.Reception)} · CRC {ReceptionInspectorView.CrcLabel(result.ChannelB.Reception.CrcValid)}";
        channels.IsEnabled = true; modes.IsEnabled = result.UsesHamming;
        if (!result.UsesHamming) modes.SelectedIndex = 0;
        RenderChannel(); WorkspaceMotion.Reveal(this);
    }
    private void Property(string name, string value)
    {
        var row = new Grid { Margin = new Thickness(0, 3, 8, 3) };
        row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(new TextBlock { Text = name, FontSize = 13, Foreground = (Brush)FindResource("MutedBrush") });
        var text = new TextBlock { Text = value, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 0, 0, 0) };
        Grid.SetColumn(text, 1); row.Children.Add(text); properties.Children.Add(row);
    }
    private void RenderChannel()
    {
        properties.Children.Clear(); fragments.Children.Clear(); fragmentTitle.Text = "";
        if (Selected is not { } channel || measurement is null) return;
        properties.Children.Add(new TextBlock { Text = $"CANAL {channel.Channel}", Foreground = (Brush)FindResource(channel.Channel == "A" ? "ChannelABrush" : "ChannelBBrush"), FontWeight = FontWeights.SemiBold });
        properties.Children.Add(new TextBlock { Text = ReceptionView.MessageStatus(channel.Reception), FontSize = 20, Foreground = ReceptionView.MessageColor(channel.Reception), Margin = new Thickness(0, 6, 0, 6) });
        Property("Cabecera", channel.Reception.HeaderValid ? "Validada" : "No validada");
        Property("CRC", ReceptionInspectorView.CrcLabel(channel.Reception.CrcValid));
        bool after = measurement.UsesHamming && modes.SelectedIndex == 1;
        var comparison = after ? channel.After : channel.Before;
        if (comparison is { } c)
        {
            Property("Esperados", c.ExpectedCount.ToString()); Property("Comparados", c.ComparedBits.ToString());
            Property("Incorrectos", c.IncorrectBits.ToString()); Property("No concluyentes", c.InconclusiveBits.ToString()); Property("Faltantes", c.MissingBits.ToString());
            Property("BER observada", c.Ber?.ToString("P2") ?? "No definida"); Property("Cobertura", c.Coverage.ToString("P2"));
            distribution.Show(c, channel.Channel, measurement.UsesHamming, after, channel.Hamming?.Traces, channel.Reception);
        }
        else { Property("Comparación", "Sin alineación / datos disponibles"); distribution.Clear(); fragmentTitle.Text = "Sin fragmentos alineados disponibles. Las referencias no permiten reconstruir datos ausentes."; }
        if (measurement.UsesHamming && channel.Hamming is { } h)
        {
            Property("Síndromes no nulos", h.NonzeroSyndromes.ToString()); Property("Inversiones intentadas", h.AppliedBitFlips.ToString());
            Property("Borrados recuperados", h.RecoveredErasures.ToString()); Property("Palabras recuperadas", h.ErasureRecoveredWords.ToString()); Property("Irrecuperables", h.UnrecoverableWords.ToString());
        }
        properties.Children.Add(new TextBlock { Text = channel.Reception.Reason, ToolTip = channel.Diagnostic, FontSize = 13, Margin = new Thickness(0, 10, 8, 0), Foreground = (Brush)FindResource("MutedBrush") });
        WorkspaceMotion.Reveal(properties);
    }
    private void RenderFragment()
    {
        fragments.Children.Clear();
        if (Selected is not { } channel || measurement is null || distribution.SelectedBit is not { } bit) return;
        bool hamming = measurement.UsesHamming, after = hamming && modes.SelectedIndex == 1;
        int group = bit.Index / (hamming ? after ? 4 : 7 : 8);
        int originalStart = group * (hamming ? 4 : 8), encodedStart = group * (hamming ? 7 : 8);
        fragmentTitle.Text = $"Fragmento {group + 1} · original desde {originalStart + 1} · recibido desde {encodedStart + 1} (posiciones base 1)";
        void Line(string label, string? bits, int start, int count)
        {
            string slice = bits is null ? "No disponible" : start >= bits.Length ? "Sin posiciones disponibles" : bits.Substring(start, Math.Min(count, bits.Length - start));
            var row = new DockPanel(); var caption = new TextBlock { Text = label, Width = 165, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(caption, Dock.Left); row.Children.Add(caption);
            row.Children.Add(new TextBox { Text = slice, IsReadOnly = true, FontFamily = (FontFamily)FindResource("CodeFont"), MinHeight = 24, Padding = new Thickness(4, 1, 4, 1), Margin = new Thickness(0, 1, 0, 1) }); fragments.Children.Add(row);
        }
        Line("Referencia original", channel.OriginalReferenceBits, originalStart, hamming ? 4 : 8);
        if (hamming) Line("Referencia codificada", channel.TransmittedReferenceBits, encodedStart, 7);
        Line("Observado · Goertzel", channel.Before?.ObservedBits, encodedStart, hamming ? 7 : 8);
        if (hamming) Line("Reconstruido · Hamming", channel.After?.ObservedBits, originalStart, 4);
    }
}
