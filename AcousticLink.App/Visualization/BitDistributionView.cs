using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using AcousticLink.App.Experiments;
using AcousticLink.App.Protocol;

namespace AcousticLink.App.Visualization;

// Explora comparaciones existentes. La referencia se utiliza solo para presentar la calidad.
public sealed class BitDistributionView : UserControl
{
    private readonly ListBox groups = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly UniformGrid enlarged = new();
    private readonly TextBlock totals = Label(""), heading = Label(""), inspector = Label("");
    private readonly List<Button> bitButtons = new();
    private IReadOnlyList<Hamming74WordTrace> traces = Array.Empty<Hamming74WordTrace>();
    private string channel = "";
    private bool hamming, after;
    private FdmTextChannelResult? reception;
    public IReadOnlyList<DisplayBit> Bits { get; private set; } = Array.Empty<DisplayBit>();
    public int GroupSize { get; private set; } = 8;
    public int SelectedGroup => groups.SelectedIndex;
    public DisplayBit? SelectedBit { get; private set; }
    public event Action<DisplayBit>? PositionSelected;

    public void Clear()
    {
        Bits = Array.Empty<DisplayBit>(); traces = Array.Empty<Hamming74WordTrace>(); reception = null;
        groups.Items.Clear(); enlarged.Children.Clear(); bitButtons.Clear(); SelectedBit = null;
        totals.Text = "Sin comparación disponible"; heading.Text = ""; inspector.Text = "";
    }

    public BitDistributionView()
    {
        var panel = new Grid(); Content = panel;
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); panel.RowDefinitions.Add(new RowDefinition());
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.Children.Add(totals);
        var factory = new FrameworkElementFactory(typeof(WrapPanel));
        groups.ItemsPanel = new ItemsPanelTemplate(factory);
        ScrollViewer.SetHorizontalScrollBarVisibility(groups, ScrollBarVisibility.Disabled);
        var workspace = new Grid(); workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.34, GridUnitType.Star) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition());
        workspace.Children.Add(groups);
        var detail = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
        detail.Children.Add(heading);
        detail.Children.Add(new ScrollViewer { Content = enlarged, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
        detail.Children.Add(inspector);
        var scroll = new ScrollViewer { Content = detail, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetColumn(scroll, 1); workspace.Children.Add(scroll); Grid.SetRow(workspace, 1); panel.Children.Add(workspace);
        var legend = Label("✓ correcto · ✗ error · ? no concluyente · — faltante · ◆ borrado reconstruido (no implica CRC válido)", 12);
        Grid.SetRow(legend, 2); panel.Children.Add(legend);
        groups.SelectionChanged += (_, _) => ShowGroup();
    }

    public void Show(BitComparison comparison, string channelName, bool usesHamming, bool originalBits,
        IReadOnlyList<Hamming74WordTrace>? wordTraces = null, FdmTextChannelResult? received = null)
    {
        channel = channelName; hamming = usesHamming; after = usesHamming && originalBits;
        reception = received;
        traces = wordTraces ?? Array.Empty<Hamming74WordTrace>();
        Bits = BitMapView.Describe(comparison.ObservedBits, comparison.ExpectedBits);
        GroupSize = hamming ? after ? 4 : 7 : 8;
        groups.Items.Clear(); enlarged.Children.Clear(); SelectedBit = null; inspector.Text = "";
        string Count(int count) => $"{count} ({(comparison.ExpectedCount == 0 ? 0 : (double)count / comparison.ExpectedCount):P2})";
        totals.ToolTip = $"Correctos {Count(comparison.ComparedBits - comparison.IncorrectBits)} · errores {Count(comparison.IncorrectBits)}\n" +
            $"? {Count(comparison.InconclusiveBits)} · faltantes {Count(comparison.MissingBits)} · total {comparison.ExpectedCount}";
        totals.Text = $"✓ {comparison.ComparedBits - comparison.IncorrectBits} · ✗ {comparison.IncorrectBits} · ? {comparison.InconclusiveBits} · — {comparison.MissingBits} / {comparison.ExpectedCount}";
        for (int start = 0; start < Bits.Count; start += GroupSize)
        {
            var item = new StackPanel();
            item.Children.Add(Label($"{(hamming ? "P" : "B")}{start / GroupSize + 1}", 12));
            var strip = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var bit in Bits.Skip(start).Take(GroupSize))
                strip.Children.Add(new Border { Width = 8, Height = 14, Margin = new Thickness(1, 0, 1, 0),
                    Background = Color(bit), BorderBrush = IsRecovered(bit) ? (Brush)FindResource("HammingBrush") : Brushes.Transparent,
                    BorderThickness = new Thickness(0, 0, 0, 3), ToolTip = $"Posición {bit.Index + 1}: {bit.Observed?.ToString() ?? "faltante"}" });
            item.Children.Add(strip);
            groups.Items.Add(new ListBoxItem { Content = item, Padding = new Thickness(4, 1, 4, 3),
                ToolTip = hamming ? $"Palabra {start / GroupSize + 1}" : $"Byte {start / GroupSize + 1}" });
        }
        int interesting = Bits.ToList().FindIndex(b => b.State != DisplayBitState.Correct || IsRecovered(b));
        groups.SelectedIndex = Bits.Count == 0 ? -1 : Math.Max(0, interesting) / GroupSize;
        if (Bits.Count == 0) { heading.Text = "Sin posiciones disponibles"; inspector.Text = "No disponible"; }
    }

    private bool IsRecovered(DisplayBit bit)
    {
        if (!after || bit.Observed is not ('0' or '1')) return false;
        int position = new[] { 3, 5, 6, 7 }[bit.Index % 4];
        return traces.Any(t => t.WordIndex == bit.Index / 4 && t.RecoveredPositions.Contains(position));
    }

    private void ShowGroup()
    {
        enlarged.Children.Clear(); bitButtons.Clear();
        if (groups.SelectedIndex < 0) return;
        int start = groups.SelectedIndex * GroupSize;
        heading.Text = $"{(hamming ? "Palabra" : "Byte")} {groups.SelectedIndex + 1} · " +
            (after ? "información reconstruida (4 bits)" : hamming ? "recibida (7 símbolos)" : "bits recibidos") +
            $" · posiciones {start + 1}–{Math.Min(start + GroupSize, Bits.Count)}";
        enlarged.Columns = GroupSize;
        enlarged.MinWidth = GroupSize * 38;
        foreach (var bit in Bits.Skip(start).Take(GroupSize))
        {
            var text = new StackPanel();
            text.Children.Add(Label(hamming ? after ? $"D{bit.Index % 4 + 1}" : new[] { "P1", "P2", "D1", "P4", "D2", "D3", "D4" }[bit.Index % 7] : $"{bit.Index % 8 + 1}/8", 12));
            var symbol = Label((bit.Observed?.ToString() ?? "—") + (IsRecovered(bit) ? "◆" : ""), 22);
            symbol.FontFamily = (FontFamily)FindResource("CodeFont"); symbol.Foreground = Color(bit); text.Children.Add(symbol); text.Children.Add(Label($"#{bit.Index + 1}", 12));
            var button = new Button { Content = text, Tag = bit, Padding = new Thickness(3, 0, 3, 0), Margin = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
            System.Windows.Automation.AutomationProperties.SetName(button, $"Canal {channel}, bit {bit.Index + 1}, {(after ? "reconstruido" : "recibido")}: {bit.Observed?.ToString() ?? "faltante"}");
            button.Click += (_, _) => Inspect(bit); enlarged.Children.Add(button); bitButtons.Add(button);
        }
        if (start < Bits.Count) Inspect(Bits[start]);
        WorkspaceMotion.Reveal(enlarged);
    }

    private void Inspect(DisplayBit bit)
    {
        SelectedBit = bit;
        foreach (var button in bitButtons) button.Background = ReferenceEquals(button.Tag, bit) ? Brushes.DarkSlateBlue : new SolidColorBrush(System.Windows.Media.Color.FromRgb(36, 54, 80));
        string state = bit.State switch { DisplayBitState.Correct => "correcto frente a referencia", DisplayBitState.Incorrect => "incorrecto frente a referencia",
            DisplayBitState.Inconclusive => "no concluyente", DisplayBitState.Missing => "faltante", _ => "observado" };
        var section = reception is null ? null : FramePresentation.Sections(reception).FirstOrDefault(s => s.Part != FramePart.Header &&
            (after ? s.OriginalStart : s.EncodedStart) is int offset && bit.Index >= offset && bit.Index < offset + (after ? s.OriginalSize : s.EncodedSize));
        inspector.Text = $"Canal {channel} · {section?.Name ?? "sección no validada"}\n" +
            $"Bit {bit.Index % GroupSize + 1} de {GroupSize} · {(after ? "reconstruido" : "recibido")}: {bit.Observed?.ToString() ?? "no disponible"} · {state}\n" +
            $"Referencia experimental: {bit.Expected?.ToString() ?? "no disponible"} · posición {bit.Index + 1} del bloque {(after ? "original" : "recibido")}" +
            (IsRecovered(bit) ? " · borrado recuperado (no es una inversión)" : "");
        WorkspaceMotion.Reveal(inspector);
        PositionSelected?.Invoke(bit);
    }
    private static Brush Color(DisplayBit bit) => bit.State switch
    { DisplayBitState.Correct => Brushes.LightGreen, DisplayBitState.Incorrect => Brushes.Salmon,
        DisplayBitState.Inconclusive => Brushes.Khaki, DisplayBitState.Missing => Brushes.SlateGray, _ => Brushes.LightSkyBlue };
    private static TextBlock Label(string text, double size = 14) => new() { Text = text, FontSize = size, Margin = new Thickness(0, 2, 0, 2) };
}
