using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using AcousticLink.App.Protocol;

namespace AcousticLink.App.Visualization;

public sealed class FrameExplorerView : UserControl
{
    public bool FitWorkspace { get; set; }
    private readonly ComboBox channels = new() { ItemsSource = new[] { "Canal A", "Canal B" }, SelectedIndex = 0 };
    private readonly ComboBox representation = new() { ItemsSource = new[] { "Símbolos recibidos", "Bits originales reconstruidos" }, SelectedIndex = 0 };
    private readonly TextBlock scope = new();
    private readonly UniformGrid segments = new() { Columns = 4, Margin = new Thickness(0, 4, 0, 4) };
    private readonly StackPanel detail = new();
    private readonly StackPanel groupDetail = new();
    private readonly WrapPanel groupStrip = new();
    private readonly TextBlock bitDetail = new();
    private readonly BitMapView map = new() { ShowConventionalRoles = true };
    private readonly TabControl views = new() { Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
    private readonly TabItem frameTab = new() { Header = "Trama" }, textTab = new() { Header = "Texto / UTF-8" }, hammingTab = new() { Header = "Hamming" };
    private readonly Grid workspace = new();
    private readonly ScrollViewer detailScroll = new();
    private readonly Grid detailPane = new();
    private readonly Utf8ContentView utf8 = new();
    private readonly Dictionary<(string, FramePart, bool, bool), int> selections = new();
    private readonly TextBlock technicalBitDetail = new();
    private FdmTextChannelResult? textResult;
    private Window? host;
    private FdmTextReceiveResult? result;
    private FramePart selectedPart = FramePart.Length;
    private FrameSection? section;
    private int groupSize, sectionOffset, sectionSize;
    private bool after, header;
    public event Action<int, int>? ContentByteSelected;
    private FdmTextChannelResult Channel => channels.SelectedIndex == 1 ? result!.ChannelB : result!.ChannelA;
    private string ChannelName => channels.SelectedIndex == 1 ? "B" : "A";

    public FrameExplorerView()
    {
        Height = 450;
        var panel = new Grid();
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); panel.RowDefinitions.Add(new RowDefinition());
        var top = new StackPanel(); var context = new DockPanel();
        DockPanel.SetDock(channels, Dock.Left); channels.Margin = new Thickness(0, 0, 12, 0);
        context.Children.Add(channels); context.Children.Add(scope); top.Children.Add(context);
        top.Children.Add(new TextBlock { Text = "Navegación de secciones · anchos no proporcionales", FontSize = 12 }); top.Children.Add(segments);
        panel.Children.Add(top); Grid.SetRow(views, 1); panel.Children.Add(views); Content = panel;
        views.ItemContainerStyle = (Style)FindResource("WorkspaceToolTab");
        map.CompactSingleGroup = true;
        channels.HorizontalAlignment = HorizontalAlignment.Left; channels.MinWidth = 140;
        representation.HorizontalAlignment = HorizontalAlignment.Left; representation.MaxWidth = 340;
        representation.Margin = new Thickness(0, 4, 0, 12);
        scope.FontSize = 13; scope.Foreground = (Brush)FindResource("MutedBrush");
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.34, GridUnitType.Star) }); workspace.ColumnDefinitions.Add(new ColumnDefinition());
        workspace.RowDefinitions.Add(new RowDefinition()); workspace.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var groups = new ScrollViewer { Content = groupStrip, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        detailPane.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); detailPane.RowDefinitions.Add(new RowDefinition());
        detailPane.Margin = new Thickness(12, 0, 0, 0); detailPane.Children.Add(representation);
        detail.VerticalAlignment = VerticalAlignment.Center;
        detailScroll.Content = detail; detailScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Grid.SetRow(detailScroll, 1); detailPane.Children.Add(detailScroll);
        Grid.SetColumn(detailPane, 1); workspace.Children.Add(groups); workspace.Children.Add(detailPane);
        workspace.SizeChanged += (_, _) => LayoutWorkspace();
        SizeChanged += (_, _) => LayoutWorkspace();
        views.Items.Add(frameTab); views.Items.Add(textTab); views.Items.Add(hammingTab); views.SelectedIndex = 0; frameTab.Content = workspace;
        views.SelectionChanged += (_, e) =>
        {
            if (!ReferenceEquals(e.Source, views)) return;
            frameTab.Content = textTab.Content = hammingTab.Content = null;
            if (views.SelectedItem == textTab) { textTab.Content = utf8; UpdateText(); }
            else { ((TabItem)views.SelectedItem).Content = workspace; Render(); }
            WorkspaceMotion.Reveal(views);
        };
        utf8.ExploreBytesRequested += (offset, count) => SelectContentBytes(channels.SelectedIndex, offset, count);
        Loaded += (_, _) => { host = Window.GetWindow(this); if (host is not null) { host.SizeChanged += ResizeHost; ResizeHost(null, null); } };
        Unloaded += (_, _) => { if (host is not null) host.SizeChanged -= ResizeHost; host = null; };
        channels.SelectionChanged += (_, _) => { Render(); WorkspaceMotion.Reveal(views); };
        representation.SelectionChanged += (_, _) => { Render(); WorkspaceMotion.Reveal(detail); };
        map.PositionSelected += ShowBit; Clear();
    }
    private void ResizeHost(object? sender, SizeChangedEventArgs? e)
    {
        if (FitWorkspace) Height = double.NaN;
        else if (host is not null) Height = Math.Clamp(host.ActualHeight * 0.58, 340, 560);
    }
    private void LayoutWorkspace()
    {
        bool narrow = ActualWidth < 720, hasGroups = groupStrip.Children.Count > 0;
        workspace.ColumnDefinitions[0].Width = narrow ? new GridLength(1, GridUnitType.Star)
            : new GridLength(hasGroups ? Math.Clamp(workspace.ActualWidth * 0.24, 150, 260) : 0);
        workspace.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        workspace.RowDefinitions[0].Height = narrow ? new GridLength(hasGroups ? 100 : 0) : new GridLength(1, GridUnitType.Star);
        workspace.RowDefinitions[1].Height = narrow ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        Grid.SetColumn(detailPane, narrow ? 0 : 1); Grid.SetRow(detailPane, narrow ? 1 : 0);
    }
    private void UpdateText()
    {
        if (result is not null && !ReferenceEquals(textResult, Channel)) { utf8.Show(Channel, ChannelName); textResult = Channel; }
    }
    public void OpenText(int channelIndex) { channels.SelectedIndex = channelIndex; views.SelectedItem = textTab; UpdateText(); }
    public void Clear(string message = "Sin recepción analizada")
    {
        result = null; textResult = null; section = null; scope.Text = message; segments.Children.Clear(); detail.Children.Clear(); groupStrip.Children.Clear(); selections.Clear(); utf8.Clear(message);
        channels.IsEnabled = false; representation.IsEnabled = false;
    }
    public void Show(FdmTextReceiveResult reception)
    {
        bool changed = !ReferenceEquals(result, reception);
        result = reception;
        if (changed)
        {
            selections.Clear(); selectedPart = FramePart.Length;
            var interesting = Channel.HammingDiagnostics?.Traces.FirstOrDefault(t => t.State != Hamming74TraceState.Unchanged);
            if (interesting is not null)
                selectedPart = FramePresentation.Sections(Channel).FirstOrDefault(s => s.Part != FramePart.Header && s.EncodedStart.HasValue &&
                    interesting.EncodedBitOffset >= s.EncodedStart && interesting.EncodedBitOffset < s.EncodedStart + s.EncodedSize)?.Part ?? FramePart.Length;
        }
        channels.IsEnabled = true; Render();
    }
    public void SelectContentBytes(int channelIndex, int byteOffset, int byteCount)
    {
        if (result is null) return;
        views.SelectedItem = frameTab;
        channels.SelectedIndex = channelIndex; selectedPart = FramePart.Payload;
        representation.SelectedIndex = 1; Render();
        if (section is null || section.OriginalStart is null) return;
        // La vista original agrupa ocho bits: un byte completo, incluso con Hamming.
        SelectGroup(8 + byteOffset * 8);
        map.HighlightRange(8 + byteOffset * 8, byteCount * 8);
    }
    private static Brush SectionColor(FramePart part) => part switch
    {
        FramePart.Length => Brushes.Plum, FramePart.Payload => Brushes.LightSkyBlue,
        FramePart.Crc => Brushes.LightGreen, _ => Brushes.SlateGray
    };
    private void Render()
    {
        if (result is null) return;
        bool textActive = views.SelectedItem == textTab;
        if (textActive) UpdateText();
        var channel = Channel;
        bool hammingView = views.SelectedItem == hammingTab;
        if (hammingView && selectedPart == FramePart.Header) selectedPart = FramePart.Length;
        var sections = FramePresentation.Sections(channel);
        section = sections.Single(s => s.Part == selectedPart);
        header = selectedPart == FramePart.Header;
        if ((header || !channel.UsesHamming || hammingView) && representation.SelectedIndex != 0) { representation.SelectedIndex = 0; return; }
        representation.IsEnabled = !header && channel.UsesHamming && !hammingView;
        after = !header && representation.SelectedIndex == 1;
        bool knownLength = channel.ByteLength is >= 1 and <= FdmTextFrameBuilder.MaximumUtf8Bytes;
        scope.Text = (channel.UsesHamming ? "Cabecera sin Hamming → longitud + datos + CRC protegidos → Hamming → CRC → UTF-8."
            : "Cabecera → longitud + datos + CRC → CRC → UTF-8 (sin Hamming).") + "\n" +
            (knownLength ? $"Longitud leída: {channel.ByteLength} bytes · {(channel.CrcValid is true ? "confirmada por CRC" : "aún no confirmada por CRC")}."
                : "Longitud no recuperada: Datos y CRC sin límites comprobados; no se usan referencias experimentales.");
        segments.Children.Clear(); detail.Children.Clear();
        groupStrip.Children.Clear();
        LayoutWorkspace();
        foreach (var item in sections)
        {
            var caption = new StackPanel();
            caption.Children.Add(new TextBlock { Text = item.Name, FontWeight = FontWeights.SemiBold });
            caption.Children.Add(new TextBlock { Text = item.Part == FramePart.Header ? $"Preámbulo {AcousticLink.App.Signals.BfskFrameBuilder.Preamble.Length} + marcador {AcousticLink.App.Signals.BfskFrameBuilder.StartMarker.Length} bits"
                : item.EncodedSize.HasValue ? channel.UsesHamming ? $"{item.OriginalSize} → {item.EncodedSize} bits" : $"{item.EncodedSize} bits" : "Tamaño no disponible" });
            var button = new Button { Content = caption, HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(8),
                BorderBrush = SectionColor(item.Part), BorderThickness = new Thickness(selectedPart == item.Part ? 3 : 1), Margin = new Thickness(2), Tag = item.Part };
            button.IsEnabled = !hammingView || item.Part != FramePart.Header;
            button.Click += (_, _) => { selectedPart = item.Part; if (views.SelectedItem == textTab) views.SelectedItem = frameTab; else Render(); }; segments.Children.Add(button);
        }
        if (textActive) return;
        detail.Children.Add(new TextBlock { Text = section.Purpose });
        if (hammingView && !channel.UsesHamming) { detail.Children.Add(new TextBlock { Text = "Hamming desactivado: no hay palabras de código para explorar." }); return; }
        if (section.EncodedStart is null || section.EncodedSize is null) { detail.Children.Add(new TextBlock { Text = "Rango no disponible: falta una longitud válida." }); return; }
        int frameStart = header ? 0 : FramePresentation.HeaderSize + section.EncodedStart.Value;
        var rangeDetails = new Expander { Header = "Rango de sección", Content = new TextBlock { Text = $"Trama transmitida [{frameStart}, {frameStart + section.EncodedSize}) · índices base cero, fin excluido." } };
        sectionOffset = (after ? section.OriginalStart : section.EncodedStart)!.Value;
        sectionSize = (after ? section.OriginalSize : section.EncodedSize)!.Value;
        groupSize = channel.UsesHamming && !header && !after ? 7 : 8;
        representation.ToolTip = after ? "Bytes originales reconstruidos; no son decisiones directas del micrófono. D1–D4 son posiciones de información."
            : groupSize == 7 ? "Palabras Hamming realmente recibidas." : "Bytes recibidos sin agrupación Hamming.";
        var groups = groupStrip; groups.Children.Clear();
        for (int start = sectionOffset; start < sectionOffset + sectionSize; start += groupSize)
        {
            int offset = start;
            var trace = groupSize == 7 ? channel.HammingDiagnostics?.Traces.FirstOrDefault(t => t.WordIndex == start / 7) : null;
            if (hammingView && trace is null) continue;
            string title = groupSize == 7 ? $"Palabra {start / 7 + 1}" : $"Byte {(start - sectionOffset) / 8 + 1} · {section.Name}";
            string state = trace is not null ? TraceState(trace.State) : SymbolAt(start).HasValue ? "Disponible" : "Sin datos";
            var caption = new StackPanel(); caption.Children.Add(new TextBlock { Text = title });
            caption.Children.Add(new TextBlock { Text = state, FontSize = 12 });
            var button = new Button { Content = caption, Tag = offset, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(2), BorderBrush = SectionColor(selectedPart),
                ToolTip = $"{section.Name} · [{start}, {Math.Min(start + groupSize, sectionOffset + sectionSize)})" };
            button.Click += (_, _) => SelectGroup(offset); groups.Children.Add(button);
        }
        detail.Children.Add(groupDetail);
        detail.Children.Add(rangeDetails);
        LayoutWorkspace();
        if (groups.Children.Count == 0) { groupDetail.Children.Clear(); groupDetail.Children.Add(new TextBlock { Text = "No hay trazas reales de esta sección. La recepción puede haberse detenido en longitud." }); return; }
        var key = (ChannelName, selectedPart, after, hammingView);
        int initial = groups.Children.OfType<Button>().First().Tag is int first ? first : sectionOffset;
        var interesting = groups.Children.OfType<Button>().FirstOrDefault(b => b.Tag is int offset && channel.HammingDiagnostics?.Traces.Any(t => t.WordIndex == offset / (after ? 4 : 7) && t.State != Hamming74TraceState.Unchanged) == true);
        if (selections.TryGetValue(key, out int saved) && groups.Children.OfType<Button>().Any(b => Equals(b.Tag, saved))) initial = saved;
        else if (interesting?.Tag is int special) initial = special;
        SelectGroup(initial);
    }
    private char? ReceivedAt(int index)
    {
        string? raw = header ? (channels.SelectedIndex == 1 ? result!.Synchronization.ChannelB : result!.Synchronization.ChannelA)?.Header.Bits : Channel.Block?.Bits;
        if (raw is not null && index < raw.Length) return raw[index];
        var trace = !header ? Channel.HammingDiagnostics?.Traces.FirstOrDefault(t => t.WordIndex == index / 7) : null;
        return trace is not null && index % 7 < trace.ReceivedBits.Length ? trace.ReceivedBits[index % 7] : null;
    }
    private char? SymbolAt(int index)
    {
        if (!after) return ReceivedAt(index);
        string? original = Channel.HammingDiagnostics?.DataBits;
        return original is not null && index < original.Length ? original[index] : null;
    }
    private void SelectGroup(int start)
    {
        groupDetail.Children.Clear();
        if (result is null || section is null) return;
        WorkspaceMotion.Reveal(groupDetail);
        selections[(ChannelName, selectedPart, after, views.SelectedItem == hammingTab)] = start;
        foreach (var button in groupStrip.Children.OfType<Button>()) button.BorderThickness = new Thickness(button.Tag is int offset && offset == start ? 3 : 1);
        groupDetail.Children.Add(new TextBlock { Text = $"{section.Name} · {(groupSize == 7 ? $"palabra {start / 7 + 1}" : $"byte {(start - sectionOffset) / 8 + 1}")}", FontSize = 16,
            ToolTip = $"{(after ? "Bloque original" : header ? "Cabecera" : "Bloque transmitido")} [{start}, {Math.Min(start + groupSize, sectionOffset + sectionSize)}) · índices base cero" });
        int size = Math.Min(groupSize, sectionOffset + sectionSize - start);
        var values = Enumerable.Range(start, size).Select(SymbolAt).ToArray();
        var traces = Channel.HammingDiagnostics?.Traces ?? Array.Empty<Hamming74WordTrace>();
        if (values.All(v => !v.HasValue)) { groupDetail.Children.Add(new TextBlock { Text = "No disponible: estructura conocida, pero no hay símbolos reales de este grupo." }); return; }
        string available = new(values.TakeWhile(v => v.HasValue).Select(v => v!.Value).ToArray());
        map.WordTraces = header ? Array.Empty<Hamming74WordTrace>() : traces;
        map.ShowSection(available, ChannelName, Channel.UsesHamming && !header, after, start, size,
            after ? "Bits originales reconstruidos" : "Símbolos reales recibidos");
        groupDetail.Children.Add(map);
        bitDetail.Text = "Seleccioná un símbolo para inspeccionarlo."; groupDetail.Children.Add(bitDetail);
        technicalBitDetail.Text = "Sin bit seleccionado.";
        groupDetail.Children.Add(new Expander { Header = "Índices y correspondencias", Content = technicalBitDetail });
        int firstWord = after ? start / 4 : start / 7;
        int endWord = after ? (start + size + 3) / 4 : (start + size + 6) / 7;
        if (Channel.UsesHamming && !header)
        {
            var real = traces.Where(t => t.WordIndex >= firstWord && t.WordIndex < endWord).ToArray();
            string text = real.Length == 0 ? "No hay traza real de estas palabras." : string.Join("\n", real.Select(t =>
                $"Palabra {t.WordIndex + 1}: {TraceState(t.State)} · {t.ReceivedBits} → {t.ResultBits ?? "no disponible"} · datos {t.DataBits ?? "no disponibles"}\nSíndrome {t.Syndrome?.ToString() ?? "no calculado"} · inversión {t.FlippedPosition?.ToString() ?? "ninguna"} · borrados {string.Join(", ", t.RecoveredPositions)}. {t.Reason}"));
            groupDetail.Children.Add(new Expander { Header = "Diagnóstico de palabra(s)", Content = new TextBlock { Text = text } });
        }
    }
    private void ShowBit(DisplayBit bit)
    {
        if (result is null) return;
        WorkspaceMotion.Reveal(bitDetail);
        var p = FramePresentation.Position(Channel.UsesHamming, after, header, bit.Index, Channel.ByteLength);
        char? received = ReceivedAt(header ? bit.Index : p.EncodedIndex!.Value);
        char? reconstructed = p.OriginalIndex is { } original && Channel.HammingDiagnostics?.DataBits is { } data && original < data.Length ? data[original]
            : !Channel.UsesHamming ? received : null;
        var trace = p.WordIndex is { } word ? Channel.HammingDiagnostics?.Traces.FirstOrDefault(t => t.WordIndex == word) : null;
        string state = bit.Observed is null ? "Faltante" : bit.Observed == '?' ? after ? "Información original no reconstruida"
            : trace?.RecoveredPositions.Contains(p.CodePosition ?? 0) == true ? "Recibido '?'; borrado recuperado por Hamming" : "Recibido no concluyente" :
            trace?.RecoveredPositions.Contains(p.CodePosition ?? 0) == true ? "Recuperado de borrado" :
            trace?.FlippedPosition == p.CodePosition && trace?.FlippedPosition is not null ? "Inversión por síndrome (verificar CRC)" : "Concluyente; sin referencia experimental";
        string selected = header ? $"Cabecera: índice {bit.Index}, posición {bit.Index + 1}"
            : after ? $"Bloque original: índice {bit.Index}, posición {bit.Index + 1}; D{p.DataPosition} de cuatro en palabra {p.WordIndex + 1}"
            : $"Bloque transmitido: índice {bit.Index}, posición {bit.Index + 1}";
        bitDetail.Text = $"Bit {(after ? bit.Index % 4 + 1 : groupSize == 7 ? bit.Index % 7 + 1 : bit.Index % 8 + 1)} de {(after ? 4 : groupSize)} · recibido: {received?.ToString() ?? "—"} → reconstruido: {reconstructed?.ToString() ?? "—"}\n{state}";
        technicalBitDetail.Text = $"Canal {ChannelName} · {section!.Name} · {selected}\nRecibido: {received?.ToString() ?? "No disponible"} · reconstruido: {reconstructed?.ToString() ?? "No disponible"} · {state}\n" +
            (p.WordIndex.HasValue ? $"Correspondencia: palabra {p.WordIndex + 1}, posición de código {p.CodePosition}/7 · índice codificado {p.EncodedIndex} · índice original {p.OriginalIndex?.ToString() ?? "No disponible (paridad)"}\n" : "") +
            $"Índice absoluto en trama transmitida: {p.FrameIndex} (posición {p.FrameIndex + 1})\n" +
            (p.Utf8Byte.HasValue ? $"Contenido UTF-8: byte {p.Utf8Byte + 1}, bit MSB {p.Utf8Bit}/8" : "Byte UTF-8 relacionado: No disponible");
        if (p.Utf8Byte is { } index && FramePresentation.Characters(Channel).Any(c => index >= c.ByteOffset && index < c.ByteOffset + c.Bytes.Count))
        {
            if (!ReferenceEquals(textResult, Channel)) { utf8.Show(Channel, ChannelName); textResult = Channel; }
            utf8.HighlightByte(index);
            ContentByteSelected?.Invoke(channels.SelectedIndex, index);
        }
    }
    private static string TraceState(Hamming74TraceState state) => state switch
    {
        Hamming74TraceState.Unchanged => "Sin cambios", Hamming74TraceState.BitFlipped => "Inversión por síndrome",
        Hamming74TraceState.ErasuresRecovered => "Borrados recuperados", Hamming74TraceState.Incomplete => "Incompleta", _ => "Irrecuperable"
    };
}
