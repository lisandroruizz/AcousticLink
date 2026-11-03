using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using AcousticLink.App.Experiments;
using AcousticLink.App.Protocol;

namespace AcousticLink.App.Visualization;

public enum DisplayBitState { Observed, Correct, Incorrect, Inconclusive, Missing }
public sealed record DisplayBit(int Index, char? Observed, char? Expected, DisplayBitState State);

// Representación de cadenas ya calculadas. No decodifica ni devuelve datos al receptor.
public sealed class BitMapView : UserControl
{
    private readonly StackPanel panel = new();
    private readonly List<Button> symbolButtons = new();
    private WrapPanel? renderedGroups;
    private int renderedGroupSize;
    public event Action<DisplayBit>? PositionSelected;
    public bool ShowConventionalRoles { get; set; }
    public bool CompactSingleGroup { get; set; }
    public IReadOnlyList<Hamming74WordTrace> WordTraces { get; set; } = Array.Empty<Hamming74WordTrace>();
    public BitMapView() { Content = panel; SizeChanged += (_, _) => FitGroupWidths(); }
    private void FitGroupWidths()
    {
        if (!CompactSingleGroup || renderedGroups is null) return;
        double available = ActualWidth > 0 ? ActualWidth - 12 : renderedGroupSize * 48;
        renderedGroups.ItemWidth = Math.Min(Math.Max(renderedGroupSize * 40, available), renderedGroupSize * 64);
    }

    public static IReadOnlyList<DisplayBit> Describe(string observed, string? expected = null)
    {
        if (observed.Any(c => c is not ('0' or '1' or '?')) ||
            expected is not null && (expected.Any(c => c is not ('0' or '1')) || observed.Length > expected.Length))
            throw new ArgumentException("Cadenas de visualización inválidas.");
        var bits = new List<DisplayBit>();
        for (int i = 0; i < (expected?.Length ?? observed.Length); i++)
        {
            char? received = i < observed.Length ? observed[i] : null;
            char? reference = expected is null ? null : expected[i];
            var state = received is null ? DisplayBitState.Missing : received == '?' ? DisplayBitState.Inconclusive
                : reference is null ? DisplayBitState.Observed : received == reference ? DisplayBitState.Correct : DisplayBitState.Incorrect;
            bits.Add(new(i, received, reference, state));
        }
        return bits.AsReadOnly();
    }

    public void ShowObserved(string observed, bool usesHamming, string channel = "sin identificar")
        => Show(Describe(observed), channel, usesHamming ? 7 : 8, false, usesHamming,
            "Símbolos recibidos disponibles · el fragmento puede corresponder solo a longitud");

    public void ShowComparison(BitComparison comparison, string channel, bool usesHamming, bool after)
        => Show(Describe(comparison.ObservedBits, comparison.ExpectedBits), channel, usesHamming ? after ? 4 : 7 : 8,
            after, usesHamming, after ? "Después: bits originales reconstruidos, no decisiones directas del micrófono"
                : "Antes: decisiones recibidas de Goertzel · referencia solo experimental");

    public void ShowSection(string observed, string channel, bool usesHamming, bool after, int offset, int size, string title)
    {
        if (offset < 0 || size < observed.Length) throw new ArgumentException("Rango de sección inválido.");
        var bits = Describe(observed).Select(b => b with { Index = b.Index + offset }).ToList();
        for (int i = observed.Length; i < size; i++) bits.Add(new(i + offset, null, null, DisplayBitState.Missing));
        Show(bits.AsReadOnly(), channel, usesHamming ? after ? 4 : 7 : 8, after, usesHamming, title);
    }

    public void HighlightRange(int start, int count)
    {
        foreach (var button in symbolButtons)
            button.BorderThickness = ((DisplayBit)button.Tag).Index is var i && i >= start && i < start + count ? new Thickness(3) : new Thickness(1);
        symbolButtons.FirstOrDefault(b => ((DisplayBit)b.Tag).Index >= start)?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private void Show(IReadOnlyList<DisplayBit> bits, string channel, int groupSize, bool after, bool usesHamming, string title)
    {
        panel.Children.Clear();
        symbolButtons.Clear();
        panel.Children.Add(new TextBlock { Text = title, FontSize = 14 });
        if (bits.Count == 0) { panel.Children.Add(new TextBlock { Text = "No hay símbolos disponibles" }); return; }
        var groups = new WrapPanel();
        groups.HorizontalAlignment = CompactSingleGroup ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        renderedGroups = groups; renderedGroupSize = groupSize; FitGroupWidths();
        var selection = new TextBlock { Text = "Seleccioná una posición para inspeccionarla.", MinHeight = 42, Margin = new Thickness(0, 4, 0, 0) };
        var focusedSymbol = new TextBlock { Text = "·", FontSize = 32, MinWidth = 48, Margin = new Thickness(0, 4, 8, 0), FontFamily = (FontFamily)FindResource("CodeFont") };
        for (int start = 0; start < bits.Count; start += groupSize)
        {
            var group = new StackPanel();
            group.Children.Add(new TextBlock { Text = usesHamming ? $"Palabra {bits[start].Index / groupSize + 1}" : $"Grupo {bits[start].Index / groupSize + 1} · sin Hamming", FontSize = 12 });
            var row = new UniformGrid { Columns = groupSize };
            foreach (var bit in bits.Skip(start).Take(groupSize))
            {
                var labels = new StackPanel();
                labels.Children.Add(new TextBlock { Text = bit.Observed?.ToString() ?? "—", FontSize = 20, FontFamily = (FontFamily)FindResource("CodeFont"), HorizontalAlignment = HorizontalAlignment.Center });
                labels.Children.Add(new TextBlock { Text = (bit.Index + 1).ToString(), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center });
                if (ShowConventionalRoles && usesHamming)
                    labels.Children.Insert(0, new TextBlock { Text = after ? $"D{bit.Index % 4 + 1}" : new[] { "P1", "P2", "D1", "P4", "D2", "D3", "D4" }[bit.Index % 7], HorizontalAlignment = HorizontalAlignment.Center });
                Brush color = bit.State switch { DisplayBitState.Incorrect => Brushes.Salmon, DisplayBitState.Inconclusive => Brushes.Khaki,
                    DisplayBitState.Missing => Brushes.SlateGray, DisplayBitState.Correct => Brushes.LightGreen,
                    _ => bit.Observed == '0' ? Brushes.LightSkyBlue : Brushes.White };
                int codePosition = after ? new[] { 3, 5, 6, 7 }[bit.Index % 4] : bit.Index % 7 + 1;
                var trace = WordTraces.FirstOrDefault(t => t.WordIndex == bit.Index / (after ? 4 : 7));
                if (trace is not null && bit.Observed is '0' or '1')
                {
                    if (after && trace.RecoveredPositions.Contains(codePosition)) color = (Brush)FindResource("HammingBrush");
                    else if (trace.FlippedPosition == codePosition) color = Brushes.Salmon;
                }
                var button = new Button { Content = labels, Foreground = color, Padding = new Thickness(2), MinWidth = 28,
                    MinHeight = 42, HorizontalAlignment = HorizontalAlignment.Stretch, BorderBrush = color, Margin = new Thickness(1), Tag = bit };
                System.Windows.Automation.AutomationProperties.SetName(button, $"Bit {bit.Index + 1}, {(after ? "reconstruido" : "recibido")}: {bit.Observed?.ToString() ?? "faltante"}");
                button.Click += (_, _) =>
                {
                    focusedSymbol.Text = bit.Observed?.ToString() ?? "—";
                    focusedSymbol.Foreground = color;
                    string state = bit.State switch { DisplayBitState.Correct => "Correcto respecto de la referencia", DisplayBitState.Incorrect => "Incorrecto respecto de la referencia",
                        DisplayBitState.Inconclusive => "No concluyente", DisplayBitState.Missing => "Faltante", _ => "Recibido concluyente · sin referencia" };
                    string position = usesHamming ? after
                        ? $" · palabra {bit.Index / 4 + 1}, D{bit.Index % 4 + 1} de 4; corresponde a posición {new[] { 3, 5, 6, 7 }[bit.Index % 4]} del código de 7"
                        : $" · palabra {bit.Index / 7 + 1}, posición {bit.Index % 7 + 1} de 7" : " · sin agrupación Hamming";
                    selection.Text = $"Canal {channel} · bit {bit.Index + 1} (índice {bit.Index}){position}\n" +
                        $"{(after ? "Reconstruido" : "Observado")}: {bit.Observed?.ToString() ?? "faltante"} · esperado: {bit.Expected?.ToString() ?? "sin referencia"} · {state}";
                    PositionSelected?.Invoke(bit);
                };
                row.Children.Add(button);
                symbolButtons.Add(button);
            }
            group.Children.Add(row);
            groups.Children.Add(new Border { Child = group, BorderBrush = Brushes.SlateGray, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 3, 6, 3), Padding = new Thickness(3) });
        }
        if (CompactSingleGroup) panel.Children.Add(groups);
        else panel.Children.Add(new ScrollViewer { Content = groups, MaxHeight = 130, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var inspector = new DockPanel(); DockPanel.SetDock(focusedSymbol, Dock.Left);
        inspector.Children.Add(focusedSymbol); inspector.Children.Add(selection);
        if (!ShowConventionalRoles) panel.Children.Add(inspector); // El explorador aporta su inspector de índices y correspondencias.
        var legend = new TextBlock { Text = ShowConventionalRoles
            ? "0 / 1: valor · amarillo ?: no concluyente · gris —: faltante · rojo: inversión · violeta: borrado recuperado"
            : "0 / 1: valor · rojo: incorrecto respecto de referencia · amarillo ?: indeterminado · gris —: faltante", FontSize = 12 };
        if (CompactSingleGroup) groups.ToolTip = legend.Text; else panel.Children.Add(legend);
    }
}
