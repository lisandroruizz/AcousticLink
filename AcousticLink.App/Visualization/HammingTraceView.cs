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
    private readonly StackPanel technical = new();
    private readonly WrapPanel reconstructed = new();
    private readonly StackPanel wordFooter = new();
    private UniformGrid? bitCells;
    private readonly TabControl detailTools = new() { Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
    private readonly ToggleButton showTechnical = new() { Content = "Paridad / detalle", Padding = new Thickness(6, 2, 6, 2), MinHeight = 28 };
    private readonly Grid workspace = new();
    private readonly Grid inspector = new();
    private readonly GridSplitter splitter = new() { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch, Background = Brushes.SlateGray, ShowsPreview = true,
        ResizeDirection = GridResizeDirection.Columns, ResizeBehavior = GridResizeBehavior.PreviousAndNext,
        Focusable = true, ToolTip = "Ajustar lista e inspector" };
    private readonly TextBlock selectedHeading = new(), explanation = new();
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
        summary.TextWrapping = TextWrapping.Wrap; summary.VerticalAlignment = VerticalAlignment.Center;
        top.Children.Add(channel); top.Children.Add(summary); root.Children.Add(top);
        var info = new StackPanel(); info.Children.Add(counters); info.Children.Add(scope); Grid.SetRow(info, 1); root.Children.Add(info);
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.32, GridUnitType.Star) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); workspace.ColumnDefinitions.Add(new ColumnDefinition());
        workspace.RowDefinitions.Add(new RowDefinition()); workspace.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        ScrollViewer.SetHorizontalScrollBarVisibility(words, ScrollBarVisibility.Disabled);
        inspector.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); inspector.RowDefinitions.Add(new RowDefinition());
        inspector.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        selectedHeading.FontSize = 18; selectedHeading.TextWrapping = TextWrapping.Wrap; selectedHeading.Margin = new Thickness(0, 0, 8, 4);
        var wordToolbar = new DockPanel();
        DockPanel.SetDock(showTechnical, Dock.Right); wordToolbar.Children.Add(showTechnical); wordToolbar.Children.Add(selectedHeading);
        showTechnical.Click += (_, _) => { detailTools.SelectedIndex = showTechnical.IsChecked == true ? 1 : 0; showTechnical.Content = showTechnical.IsChecked == true ? "Volver a transformación" : "Paridad / detalle"; };
        inspector.Children.Add(wordToolbar);
        detailScroll.Content = detail; detailScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        detailScroll.SizeChanged += (_, _) => SizeSymbols();
        detailTools.Items.Add(new TabItem { Header = "Transformación", Content = detailScroll });
        detailTools.Items.Add(new TabItem { Header = "Paridad / detalle", Content = new ScrollViewer { Content = technical, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        detailTools.SelectedIndex = 0;
        var selectedContent = new FrameworkElementFactory(typeof(ContentPresenter), "PART_SelectedContentHost");
        selectedContent.SetBinding(ContentPresenter.ContentProperty, new System.Windows.Data.Binding("SelectedContent") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        detailTools.Template = new ControlTemplate(typeof(TabControl)) { VisualTree = selectedContent };
        detailTools.SelectionChanged += (_, e) => { if (ReferenceEquals(e.OriginalSource, detailTools) && detailTools.SelectedContent is UIElement content) WorkspaceMotion.Reveal(content); };
        Grid.SetRow(detailTools, 1); inspector.Children.Add(detailTools);
        explanation.Margin = new Thickness(0, 4, 0, 0);
        wordFooter.Children.Add(reconstructed); wordFooter.Children.Add(explanation);
        Grid.SetRow(wordFooter, 2); inspector.Children.Add(wordFooter);
        inspector.Margin = new Thickness(8, 0, 0, 0); splitter.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(splitter, 1); Grid.SetColumn(inspector, 2);
        workspace.Children.Add(words); workspace.Children.Add(splitter); workspace.Children.Add(inspector); Grid.SetRow(workspace, 2); root.Children.Add(workspace); Content = root;
        var exact = new Expander { Header = "Contadores exactos y alcance del análisis", Content = new ScrollViewer { Content = diagnostics, MaxHeight = 140, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        Grid.SetRow(exact, 3); root.Children.Add(exact);
        SizeChanged += (_, _) => LayoutPanels();
        channel.SelectionChanged += (_, _) => UpdateChannel(); words.SelectionChanged += (_, _) => ShowWord(); Clear();
    }
    private void LayoutPanels()
    {
        bool narrow = ActualWidth < 760, available = words.Items.Count > 0;
        counters.Columns = narrow ? 3 : 6;
        // Conservar el ajuste manual del divisor mientras se permanezca en la distribución ancha.
        bool wasNarrow = Grid.GetRow(inspector) == 1;
        workspace.ColumnDefinitions[0].MinWidth = !narrow && available ? 140 : 0;
        workspace.ColumnDefinitions[2].MinWidth = narrow ? 0 : 380;
        if (narrow || wasNarrow || !available || workspace.ColumnDefinitions[0].Width.Value == 0)
            workspace.ColumnDefinitions[0].Width = new GridLength(narrow ? 1 : available ? 0.32 : 0, GridUnitType.Star);
        workspace.ColumnDefinitions[2].Width = narrow ? new GridLength(0) : wasNarrow ? new GridLength(1, GridUnitType.Star) : workspace.ColumnDefinitions[2].Width;
        splitter.Visibility = narrow || !available ? Visibility.Collapsed : Visibility.Visible;
        workspace.RowDefinitions[0].Height = narrow ? new GridLength(available ? 100 : 0) : new GridLength(1, GridUnitType.Star);
        workspace.RowDefinitions[1].Height = narrow ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        Grid.SetColumn(inspector, narrow ? 0 : 2); Grid.SetRow(inspector, narrow ? 1 : 0);
        inspector.Margin = new Thickness(narrow ? 0 : 8, narrow ? 6 : 0, 0, 0);
    }
    public void Clear(string status = "Sin analizar")
    {
        bitCells = null;
        SelectTransformation();
        result = null; selectedWords.Clear(); words.Items.Clear(); detail.Children.Clear(); technical.Children.Clear(); reconstructed.Children.Clear(); counters.Children.Clear();
        summary.Text = status; selectedHeading.Text = explanation.Text = scope.Text = diagnostics.Text = ""; channel.IsEnabled = false; LayoutPanels();
    }
    public void Show(FdmExperimentResult measurement)
    {
        if (!ReferenceEquals(result, measurement)) { selectedWords.Clear(); SelectTransformation(); }
        result = measurement; channel.IsEnabled = measurement.UsesHamming; UpdateChannel();
    }
    private static TextBlock Label(string text, double size = 14) => new() { Text = text, FontSize = size, LineHeight = size >= 22 ? 28 : 18, Margin = new Thickness(0, 2, 0, 2) };
    private void SizeSymbols()
    {
        if (bitCells is null) return;
        bool spacious = detailScroll.ActualHeight >= 230;
        var target = spacious ? detail : wordFooter;
        if (!ReferenceEquals(reconstructed.Parent, target))
        {
            if (reconstructed.Parent is Panel previous) previous.Children.Remove(reconstructed);
            if (spacious) target.Children.Add(reconstructed); else target.Children.Insert(0, reconstructed);
        }
        detail.VerticalAlignment = spacious ? VerticalAlignment.Center : VerticalAlignment.Top;
        double size = detailScroll.ActualHeight >= 190 ? 38 : 26;
        foreach (var border in bitCells.Children.OfType<Border>())
        foreach (var text in ((StackPanel)border.Child).Children.OfType<TextBlock>().Where(t => Equals(t.Tag, "symbol")))
        { text.FontSize = size; text.LineHeight = size + 4; }
    }
    private void SelectTransformation()
    {
        showTechnical.IsChecked = false; showTechnical.Content = "Paridad / detalle"; detailTools.SelectedIndex = 0;
    }
    private void UpdateChannel()
    {
        bitCells = null;
        words.Items.Clear(); detail.Children.Clear(); technical.Children.Clear(); reconstructed.Children.Clear(); counters.Children.Clear(); selectedHeading.Text = explanation.Text = diagnostics.Text = scope.Text = "";
        if (result is null || Selected is not { } selected) return;
        summary.Text = (result.UsesHamming ? $"Hamming activo · borrados {(result.ErasureRecoveryEnabled ? "habilitados" : "deshabilitados")}" : "Hamming desactivado · no aplicable") +
            " · CRC " + (selected.Reception.CrcValid is true ? "válido" : selected.Reception.CrcValid is false ? "inválido" : "no comprobado");
        var stats = selected.Hamming;
        if (!result.UsesHamming || stats is null)
        {
            SelectTransformation();
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
        scope.Text = $"{stats.Traces.Count} trazas disponibles · ¹palabras decodificables del bloque experimental esperado. Contadores no aditivos.";
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
            var item = new ListBoxItem { Content = text, Foreground = StatusColor(trace.State), Tag = trace, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 2, 0, 2) };
            System.Windows.Automation.AutomationProperties.SetName(item, $"Palabra {trace.WordIndex + 1}, {section?.Name ?? "sección no disponible"}, {StateLabel(trace.State)}");
            words.Items.Add(item);
        }
        var saved = selectedWords.TryGetValue(selected.Channel, out int index) ? words.Items.OfType<ListBoxItem>().FirstOrDefault(i => ((Hamming74WordTrace)i.Tag).WordIndex == index) : null;
        words.SelectedItem = saved ?? words.Items.OfType<ListBoxItem>().FirstOrDefault(i => ((Hamming74WordTrace)i.Tag).State != Hamming74TraceState.Unchanged) ?? words.Items.OfType<ListBoxItem>().FirstOrDefault();
        if (words.Items.Count == 0) detail.Children.Add(Label("Ninguna palabra disponible. No se inventan registros para símbolos ausentes.", 20));
        LayoutPanels();
        WorkspaceMotion.Reveal(words);
    }
    private void ShowWord()
    {
        detail.Children.Clear(); technical.Children.Clear(); reconstructed.Children.Clear(); selectedHeading.Text = explanation.Text = "";
        if (Selected is not { } selected || (words.SelectedItem as ListBoxItem)?.Tag is not Hamming74WordTrace trace) return;
        selectedWords[selected.Channel] = trace.WordIndex;
        selectedHeading.Text = $"Palabra {trace.WordIndex + 1} · {HammingTeaching.Section(selected.Reception, trace)?.Name ?? "Sección no disponible"} · {StateLabel(trace.State)}";
        technical.Children.Add(Label(HammingTeaching.Association(selected.Reception, trace)));
        string reference = trace.EncodedBitOffset >= 0 && trace.EncodedBitOffset + 7 <= selected.TransmittedReferenceBits.Length
            ? selected.TransmittedReferenceBits.Substring(trace.EncodedBitOffset, 7) : "No disponible";
        technical.Children.Add(Label($"Referencia experimental codificada: {reference}. Solo se usa para comparar; no interviene en la decodificación.", 12));
        string operation = trace.State switch { Hamming74TraceState.BitFlipped => $"Inversión en posición {trace.FlippedPosition} (síndrome {trace.Syndrome})",
            Hamming74TraceState.ErasuresRecovered => $"Candidata única · {trace.RecoveredPositions.Count} borrados recuperados",
            Hamming74TraceState.Unchanged => $"Sin modificaciones · síndrome {trace.Syndrome}", _ => "Sin reconstrucción" };
        var treatment = Label($"SÍMBOLOS RECIBIDOS → {operation} → PALABRA RESULTANTE", 13);
        treatment.TextWrapping = TextWrapping.Wrap;
        treatment.ToolTip = operation; detail.Children.Add(treatment);
        technical.Children.Add(Label(operation));
        var cells = new UniformGrid { Columns = 7, MinWidth = 350, HorizontalAlignment = HorizontalAlignment.Stretch };
        bitCells = cells;
        string[] roles = ["P1", "P2", "D1", "P4", "D2", "D3", "D4"];
        var borders = new List<Border>();
        for (int i = 0; i < 7; i++)
        {
            var cell = new StackPanel(); bool parity = i is 0 or 1 or 3;
            var role = Label($"{i + 1} · {roles[i]}"); role.Foreground = parity ? (Brush)FindResource("HammingBrush") : (Brush)FindResource("ChannelABrush"); cell.Children.Add(role);
            char? received = i < trace.ReceivedBits.Length ? trace.ReceivedBits[i] : null;
            var input = Label(received?.ToString() ?? "—", 26); input.Tag = "symbol"; input.FontFamily = (FontFamily)FindResource("CodeFont");
            input.Foreground = received == '?' ? (Brush)FindResource("WarningBrush") : Brushes.LightGray; cell.Children.Add(input);
            cell.Children.Add(Label("↓", 12));
            bool recovered = trace.RecoveredPositions.Contains(i + 1), flipped = trace.FlippedPosition == i + 1;
            var output = Label((trace.ResultBits?[i].ToString() ?? "—") + (recovered ? "◆" : flipped ? "↻" : ""), 26);
            output.Tag = "symbol"; output.FontFamily = (FontFamily)FindResource("CodeFont");
            output.ToolTip = recovered ? $"Borrado reconstruido: ? → {trace.ResultBits?[i]}" : flipped ? $"Inversión intentada por síndrome: {received} → {trace.ResultBits?[i]}. Validar CRC." : "Posición sin modificación o sin resultado disponible.";
            output.Foreground = flipped ? (Brush)FindResource("ErrorBrush") : recovered ? (Brush)FindResource("HammingBrush") : Brushes.LightGray;
            cell.Children.Add(output);
            foreach (var text in cell.Children.OfType<TextBlock>()) { text.HorizontalAlignment = HorizontalAlignment.Center; text.Margin = new Thickness(0); text.LineHeight = text.FontSize >= 22 ? 30 : 18; }
            var border = new Border { Child = cell, Background = (Brush)FindResource("CardBrush"), BorderBrush = Brushes.SlateGray, BorderThickness = new Thickness(0, 0, 0, 2), Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(2), CornerRadius = new CornerRadius(4) };
            cells.Children.Add(border); borders.Add(border);
        }
        detail.Children.Add(cells);
        cells.ToolTip = "P: paridad · D: información · ↻: inversión intentada · ◆ violeta: borrado recuperado. El CRC verifica el mensaje.";
        technical.Children.Add(Label((string)cells.ToolTip, 12));
        var data = reconstructed; data.Children.Add(Label("↓ INFORMACIÓN  ", 14));
        if (trace.DataBits is null) data.Children.Add(Label("No disponible", 22));
        else foreach (char bit in trace.DataBits) data.Children.Add(new Border { Child = new TextBlock { Text = bit.ToString(), FontSize = 26, FontFamily = (FontFamily)FindResource("CodeFont") }, Padding = new Thickness(12, 0, 12, 0), BorderBrush = Brushes.SlateGray, BorderThickness = new Thickness(0, 0, 0, 1) });
        explanation.Text = HammingTeaching.Explain(trace) + " El mensaje solo se publica tras validar CRC y UTF-8.";
        explanation.TextWrapping = TextWrapping.Wrap;
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
        technical.Children.Add(parityChoice); technical.Children.Add(parityInfo);
        technical.Children.Add(new TextBlock { Text =
            $"Índice de palabra {trace.WordIndex} · offset codificado {trace.EncodedBitOffset} (base cero)\nRecibida: {trace.ReceivedBits}\nResultante: {trace.ResultBits ?? "No disponible"}\nDatos: {trace.DataBits ?? "No disponible"}\n" +
            $"Síndrome: {trace.Syndrome?.ToString() ?? "no calculado"} · inversión: {trace.FlippedPosition?.ToString() ?? "ninguna"} · posiciones recuperadas: {string.Join(", ", trace.RecoveredPositions)}\n{trace.Reason}" });
        SizeSymbols(); WorkspaceMotion.Reveal(inspector);
    }
    private static Brush StatusColor(Hamming74TraceState state) => state switch
    { Hamming74TraceState.BitFlipped => Brushes.Salmon, Hamming74TraceState.ErasuresRecovered => (Brush)Application.Current.FindResource("HammingBrush"),
        Hamming74TraceState.Unrecoverable or Hamming74TraceState.Incomplete => Brushes.Khaki, _ => Brushes.LightGray };
    private static string StateLabel(Hamming74TraceState state) => state switch
    { Hamming74TraceState.Unchanged => "= Sin cambios", Hamming74TraceState.BitFlipped => "! Inversión por síndrome", Hamming74TraceState.ErasuresRecovered => "+ Borrados recuperados",
        Hamming74TraceState.Incomplete => "… Incompleta", _ => "? Irrecuperable" };
}
