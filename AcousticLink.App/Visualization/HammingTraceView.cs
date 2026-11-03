using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using AcousticLink.App.Experiments;
using AcousticLink.App.Protocol;

namespace AcousticLink.App.Visualization;

public sealed class HammingTraceView : UserControl
{
    private readonly ComboBox channel = new() { ItemsSource = new[] { "Canal A", "Canal B" }, SelectedIndex = 0 };
    private readonly TextBlock summary = new(), scope = new(), diagnostics = new();
    private readonly UniformGrid counters = new() { Columns = 6 };
    private readonly ListBox words = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly StackPanel detail = new();
    private readonly Grid workspace = new();
    private readonly ScrollViewer detailScroll = new();
    private readonly Dictionary<string, int> selectedWords = new();
    private FdmExperimentResult? result;
    private FdmChannelExperiment? Selected => result is null ? null : channel.SelectedIndex == 1 ? result.ChannelB : result.ChannelA;

    public HammingTraceView()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        channel.MinWidth = 140; channel.Margin = new Thickness(0, 0, 12, 0); DockPanel.SetDock(channel, Dock.Left);
        top.Children.Add(channel); top.Children.Add(summary); root.Children.Add(top);
        var info = new StackPanel(); info.Children.Add(counters); info.Children.Add(scope); Grid.SetRow(info, 1); root.Children.Add(info);
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.32, GridUnitType.Star) }); workspace.ColumnDefinitions.Add(new ColumnDefinition());
        workspace.RowDefinitions.Add(new RowDefinition()); workspace.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        ScrollViewer.SetHorizontalScrollBarVisibility(words, ScrollBarVisibility.Disabled);
        detailScroll.Content = detail; detailScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; detailScroll.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(detailScroll, 1); workspace.Children.Add(words); workspace.Children.Add(detailScroll); Grid.SetRow(workspace, 2); root.Children.Add(workspace); Content = root;
        var exact = new Expander { Header = "Contadores exactos y alcance del análisis", Content = new ScrollViewer { Content = diagnostics, MaxHeight = 140, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        Grid.SetRow(exact, 3); root.Children.Add(exact);
        SizeChanged += (_, _) => LayoutPanels();
        channel.SelectionChanged += (_, _) => UpdateChannel(); words.SelectionChanged += (_, _) => ShowWord(); Clear();
    }
    private void LayoutPanels()
    {
        bool narrow = ActualWidth < 760, available = words.Items.Count > 0;
        counters.Columns = narrow ? 3 : 6;
        workspace.ColumnDefinitions[0].Width = new GridLength(narrow ? 1 : available ? 0.32 : 0, GridUnitType.Star);
        workspace.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        workspace.RowDefinitions[0].Height = narrow ? new GridLength(available ? 100 : 0) : new GridLength(1, GridUnitType.Star);
        workspace.RowDefinitions[1].Height = narrow ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        Grid.SetColumn(detailScroll, narrow ? 0 : 1); Grid.SetRow(detailScroll, narrow ? 1 : 0);
    }
    public void Clear(string status = "Sin analizar")
    {
        result = null; selectedWords.Clear(); words.Items.Clear(); detail.Children.Clear(); counters.Children.Clear();
        summary.Text = status; scope.Text = diagnostics.Text = ""; channel.IsEnabled = false; LayoutPanels();
    }
    public void Show(FdmExperimentResult measurement)
    {
        if (!ReferenceEquals(result, measurement)) selectedWords.Clear();
        result = measurement; channel.IsEnabled = measurement.UsesHamming; UpdateChannel();
    }
    private static TextBlock Label(string text, double size = 14) => new() { Text = text, FontSize = size, Margin = new Thickness(0, 2, 0, 2) };
    private void UpdateChannel()
    {
        words.Items.Clear(); detail.Children.Clear(); counters.Children.Clear(); diagnostics.Text = scope.Text = "";
        if (result is null || Selected is not { } selected) return;
        summary.Text = (result.UsesHamming ? $"Hamming activo · borrados {(result.ErasureRecoveryEnabled ? "habilitados" : "deshabilitados")}" : "Hamming desactivado · no aplicable") +
            " · CRC " + (selected.Reception.CrcValid is true ? "válido" : selected.Reception.CrcValid is false ? "inválido" : "no comprobado");
        var stats = selected.Hamming;
        if (!result.UsesHamming || stats is null)
        {
            detail.Children.Add(Label(result.UsesHamming ? "Sin trazas: cabecera no sincronizada para este canal. " + selected.Reception.Reason : "No se aplicó el código Hamming.", 20));
            LayoutPanels(); return;
        }
        int unchanged = stats.Words.Count(w => w.HasSyndrome && w.Syndrome == 0);
        void Count(string caption, int count)
        {
            var cell = new StackPanel { Margin = new Thickness(3, 0, 6, 6) }; cell.Children.Add(Label(count.ToString(), 26)); cell.Children.Add(Label(caption)); counters.Children.Add(cell);
        }
        Count("Procesadas¹", stats.AnalyzedWords); Count("Sin cambios", unchanged); Count("Inversiones", stats.AppliedBitFlips);
        Count("Borrados rec.", stats.RecoveredErasures); Count("Palabras rec.", stats.ErasureRecoveredWords); Count("Irrecuperables", stats.UnrecoverableWords);
        scope.Text = $"{stats.Traces.Count} trazas disponibles · ¹conteo de palabras decodificables del bloque experimental esperado (no se suman estas tarjetas).";
        if (selected.Reception.ByteLength is not (>= 1 and <= FdmTextFrameBuilder.MaximumUtf8Bytes))
            scope.Text += "\nLongitud no validada: solo hay detalles del campo de longitud; los contadores pueden abarcar más posiciones esperadas.";
        diagnostics.Text = $"Canal {selected.Channel} · UTC {result.AnalyzedAt:O} · origen {result.AudioSource}\n" +
            $"Palabras decodificables: {stats.AnalyzedWords}; síndrome cero: {unchanged}; síndromes no nulos: {stats.NonzeroSyndromes}; inversiones: {stats.AppliedBitFlips}.\n" +
            $"Símbolos '?' originales: {selected.Before?.InconclusiveBits.ToString() ?? "No disponible"}; palabras con '?': {stats.InconclusiveWords}; borrados recuperados: {stats.RecoveredErasures}; palabras recuperadas: {stats.ErasureRecoveredWords}; irrecuperables: {stats.UnrecoverableWords}; incompletas/faltantes: {stats.IncompleteWords}.\n" +
            "Contadores experimentales y detalle de trazas tienen alcances distintos. Cabecera y relleno excluidos. Las referencias no reconstruyen los bits mostrados.";
        foreach (var trace in stats.Traces)
        {
            var text = new StackPanel(); var title = Label($"#{trace.WordIndex + 1} · {StateLabel(trace.State)}"); title.Foreground = StatusColor(trace.State); text.Children.Add(title);
            var section = HammingTeaching.Section(selected.Reception, trace);
            var sectionLabel = Label(section?.Name ?? "Sección no disponible", 12);
            sectionLabel.Foreground = section?.Part switch { FramePart.Length => Brushes.Plum, FramePart.Payload => Brushes.LightSkyBlue, FramePart.Crc => Brushes.LightGreen, _ => Brushes.LightGray };
            text.Children.Add(sectionLabel);
            words.Items.Add(new ListBoxItem { Content = text, Foreground = StatusColor(trace.State), Tag = trace, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 2, 0, 2) });
        }
        var saved = selectedWords.TryGetValue(selected.Channel, out int index) ? words.Items.OfType<ListBoxItem>().FirstOrDefault(i => ((Hamming74WordTrace)i.Tag).WordIndex == index) : null;
        words.SelectedItem = saved ?? words.Items.OfType<ListBoxItem>().FirstOrDefault(i => ((Hamming74WordTrace)i.Tag).State != Hamming74TraceState.Unchanged) ?? words.Items.OfType<ListBoxItem>().FirstOrDefault();
        if (words.Items.Count == 0) detail.Children.Add(Label("Ninguna palabra disponible. No se inventan registros para símbolos ausentes.", 20));
        LayoutPanels();
    }
    private void ShowWord()
    {
        detail.Children.Clear();
        if (Selected is not { } selected || (words.SelectedItem as ListBoxItem)?.Tag is not Hamming74WordTrace trace) return;
        selectedWords[selected.Channel] = trace.WordIndex;
        detail.Children.Add(Label($"Palabra {trace.WordIndex + 1} · {StateLabel(trace.State)}", 20));
        detail.Children.Add(Label(HammingTeaching.Association(selected.Reception, trace)));
        string operation = trace.State switch { Hamming74TraceState.BitFlipped => $"Inversión en posición {trace.FlippedPosition} (síndrome {trace.Syndrome})",
            Hamming74TraceState.ErasuresRecovered => $"Candidata única · {trace.RecoveredPositions.Count} borrados recuperados",
            Hamming74TraceState.Unchanged => $"Sin modificaciones · síndrome {trace.Syndrome}", _ => "Sin reconstrucción" };
        detail.Children.Add(Label($"RECIBIDA ↓ {operation} ↓ RESULTANTE", 14));
        var cells = new UniformGrid { Columns = 7, MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Stretch };
        string[] roles = ["P1", "P2", "D1", "P4", "D2", "D3", "D4"];
        var borders = new List<Border>();
        for (int i = 0; i < 7; i++)
        {
            var cell = new StackPanel(); bool parity = i is 0 or 1 or 3;
            var role = Label($"{i + 1} · {roles[i]}"); role.Foreground = parity ? Brushes.Plum : Brushes.LightSkyBlue; cell.Children.Add(role);
            char? received = i < trace.ReceivedBits.Length ? trace.ReceivedBits[i] : null;
            var input = Label(received?.ToString() ?? "—", 24); input.Foreground = received == '?' ? Brushes.Khaki : Brushes.LightGray; cell.Children.Add(input);
            cell.Children.Add(Label("↓", 12));
            var output = Label(trace.ResultBits?[i].ToString() ?? "—", 24);
            output.Foreground = trace.FlippedPosition == i + 1 ? Brushes.Salmon : trace.RecoveredPositions.Contains(i + 1) ? Brushes.LightGreen : Brushes.LightGray;
            cell.Children.Add(output);
            var border = new Border { Child = cell, BorderBrush = Brushes.SlateGray, BorderThickness = new Thickness(1), Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(2), CornerRadius = new CornerRadius(4) };
            cells.Children.Add(border); borders.Add(border);
        }
        detail.Children.Add(cells);
        detail.Children.Add(Label("P violeta: paridad · D azul: información · '?' amarillo: recibido desconocido · verde: recuperado · rojo: invertido · '—': no disponible", 12));
        var data = new WrapPanel(); data.Children.Add(Label("↓ INFORMACIÓN  ", 14));
        if (trace.DataBits is null) data.Children.Add(Label("No disponible", 22));
        else foreach (char bit in trace.DataBits) data.Children.Add(new Border { Child = Label(bit.ToString(), 24), Padding = new Thickness(12, 0, 12, 0), BorderBrush = Brushes.SlateGray, BorderThickness = new Thickness(1) });
        detail.Children.Add(data); detail.Children.Add(Label(HammingTeaching.Explain(trace)));
        var parityChoice = new ComboBox { ItemsSource = new[] { "Relaciones de paridad (opcional)", "P1: posiciones 1, 3, 5, 7", "P2: posiciones 2, 3, 6, 7", "P4: posiciones 4, 5, 6, 7" }, SelectedIndex = 0, Margin = new Thickness(0, 4, 0, 0) };
        var parityInfo = Label("Propiedades del código de paridad par; no representan pasos ejecutados.", 12);
        parityChoice.SelectionChanged += (_, _) =>
        {
            int parity = parityChoice.SelectedIndex switch { 1 => 1, 2 => 2, 3 => 4, _ => 0 };
            var covered = HammingTeaching.Coverage(parity);
            for (int i = 0; i < 7; i++) borders[i].BorderBrush = covered.Contains(i + 1) ? Brushes.LightSkyBlue : Brushes.SlateGray;
            parityInfo.Text = parity == 0 ? "Propiedades del código de paridad par; no representan pasos ejecutados."
                : $"P{parity}: XOR de {string.Join(", ", covered)} = 0 en una palabra válida. En borrados, el receptor evaluó las 16 candidatas válidas; no ejecutó esta visualización paso a paso.";
        };
        detail.Children.Add(parityChoice); detail.Children.Add(parityInfo);
        detail.Children.Add(new Expander { Header = "Diagnóstico completo de la palabra", Content = new TextBlock { Text =
            $"Índice de palabra {trace.WordIndex} · offset codificado {trace.EncodedBitOffset} (base cero)\nRecibida: {trace.ReceivedBits}\nResultante: {trace.ResultBits ?? "No disponible"}\nDatos: {trace.DataBits ?? "No disponible"}\n" +
            $"Síndrome: {trace.Syndrome?.ToString() ?? "no calculado"} · inversión: {trace.FlippedPosition?.ToString() ?? "ninguna"} · posiciones recuperadas: {string.Join(", ", trace.RecoveredPositions)}\n{trace.Reason}" } });
    }
    private static Brush StatusColor(Hamming74TraceState state) => state switch
    { Hamming74TraceState.BitFlipped => Brushes.Salmon, Hamming74TraceState.ErasuresRecovered => Brushes.LightGreen,
        Hamming74TraceState.Unrecoverable or Hamming74TraceState.Incomplete => Brushes.Khaki, _ => Brushes.LightGray };
    private static string StateLabel(Hamming74TraceState state) => state switch
    { Hamming74TraceState.Unchanged => "= Sin cambios", Hamming74TraceState.BitFlipped => "! Inversión por síndrome", Hamming74TraceState.ErasuresRecovered => "+ Borrados recuperados",
        Hamming74TraceState.Incomplete => "… Incompleta", _ => "? Irrecuperable" };
}
