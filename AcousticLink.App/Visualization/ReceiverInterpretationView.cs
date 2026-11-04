using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AcousticLink.App.Protocol;
using AcousticLink.App.Signals;

namespace AcousticLink.App.Visualization;

// Laboratorio de detección: sin generación de senoides ni decisiones paralelas.
public sealed class ReceiverInterpretationView : UserControl
{
    private readonly ComboBox channel = new() { ItemsSource = new[] { "Canal A · 1200 / 1900 Hz", "Canal B · 3000 / 3700 Hz" }, SelectedIndex = 0 };
    private readonly ComboBox symbols = new() { DisplayMemberPath = "Number" }, words = new() { DisplayMemberPath = "Label" };
    private readonly TextBlock context = Text("Sin audio"), window = Text(""), sync = Text(""), scope = Text("");
    private readonly DetectionWindowView waveform = new();
    private readonly DetectorEnergyView energy = new();
    private readonly TextBlock winner = Text(""), ratio = Text(""), criteria = Text(""), decision = Text("?", 40), reason = Text("");
    private readonly TextBlock position = Text(""), wordInfo = Text(""), decoded = Text("", 22), byteInfo = Text(""), message = Text("", 22), crc = Text("", 18);
    private readonly WrapPanel wordBits = new();
    private readonly System.Windows.Controls.Primitives.UniformGrid localSymbols = new() { Columns = 3 };
    public bool FollowLatest { get; set; }
    public SymbolWindowObservation? WindowObservation { get; private set; }
    private readonly TextBlock steps = Text(""), navigationStatus = Text("Sin símbolos disponibles"), field = Text("", 17), wordPosition = Text("", 13);
    private readonly Button previous = Nav("◀", "Símbolo anterior"), next = Nav("▶", "Símbolo siguiente"),
        firstUnknown = Nav("Primer ?", "Primer símbolo no concluyente"), nextUnknown = Nav("Siguiente ?", "Siguiente símbolo no concluyente"),
        problem = Nav("Hamming siguiente", "Siguiente palabra con inversión, borrados o problemas");
    private readonly TextBlock[] flowValues = Enumerable.Range(0, 6).Select(_ => Text("No disponible", 13)).ToArray();
    private CarrierEnergyMap? data;
    private double cursor;
    private long revision;
    private bool busy, updating;
    private ReceiverObservation? observation;
    private FdmTextReceiveResult? listedResult;
    private int listedChannel = -1;
    public FdmTextReceiveResult? Reception { get; private set; }
    public double ReceptionThrough { get; private set; }
    public ReceiverObservation? Observation => observation;
    public DemodulationSelection? Selection { get; private set; }
    public event Action<ReceiverObservation?>? ObservationUpdated;
    public event Action<double>? TimeSelected;
    private static string StateName(Hamming74TraceState state) => state switch { Hamming74TraceState.Unchanged => "Sin cambios", Hamming74TraceState.BitFlipped => "Inversión intentada", Hamming74TraceState.ErasuresRecovered => "Borrados recuperados", Hamming74TraceState.Unrecoverable => "Irrecuperable", _ => "Incompleta" };
    private sealed record WordChoice(Hamming74WordTrace Trace) { public string Label => $"Palabra {Trace.WordIndex + 1} · {StateName(Trace.State)}"; }
    public ReceiverInterpretationView()
    {
        var root = new Grid { Background = new SolidColorBrush(Color.FromRgb(11, 21, 37)), Margin = new Thickness(0, 4, 0, 0) };
        root.ColumnDefinitions.Add(new() { Width = new GridLength(.95, GridUnitType.Star), MinWidth = 180 });
        root.ColumnDefinitions.Add(new() { Width = new GridLength(1.1, GridUnitType.Star), MinWidth = 220 });
        root.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star), MinWidth = 220 });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Grid Zone(string title, int col)
        {
            var panel = new Grid { Margin = new Thickness(12, 6, 12, 8) };
            Add(panel, Text(title, 16));
            var scroll = new ScrollViewer { Content = panel, VerticalContentAlignment = VerticalAlignment.Stretch,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            Grid.SetRow(scroll, 1); Grid.SetColumn(scroll, col); root.Children.Add(scroll); return panel;
        }
        var navigation = new WrapPanel { Margin = new Thickness(12, 0, 12, 4) };
        channel.Width = 220; symbols.MinWidth = 60;
        symbols.ToolTip = "Seleccionar una decisión real conservada por el receptor";
        navigation.Children.Add(channel); navigation.Children.Add(previous); navigation.Children.Add(symbols); navigation.Children.Add(next);
        navigation.Children.Add(firstUnknown); navigation.Children.Add(nextUnknown); navigation.Children.Add(problem);
        navigationStatus.Margin = new Thickness(8, 8, 0, 0); navigation.Children.Add(navigationStatus);
        Grid.SetColumnSpan(navigation, 3); root.Children.Add(navigation);
        var left = Zone("1 · Muestras reales", 0);
        context.FontSize = 12; window.FontSize = 12; scope.FontSize = 12;
        context.Margin = sync.Margin = window.Margin = scope.Margin = new Thickness(0, 1, 0, 1);
        Add(left, context); Add(left, sync); Add(left, localSymbols); Add(left, window); Add(left, scope); Add(left, waveform, true);
        var center = Zone("2 · Detector BFSK", 1);
        var outcome = new Grid(); outcome.ColumnDefinitions.Add(new() { Width = new GridLength(76) }); outcome.ColumnDefinitions.Add(new());
        decision.FontSize = 46; Grid.SetColumn(reason, 1); outcome.Children.Add(decision); outcome.Children.Add(reason); Add(center, outcome);
        Add(center, energy, true); Add(center, criteria);
        Add(center, new Expander { Header = "Valores técnicos exactos", Content = new StackPanel { Children = { winner, ratio } }, Margin = new Thickness(0, 6, 0, 0) });
        var right = Zone("3 · Información reconstruida", 2);
        var resultHeader = new StackPanel(); message.FontSize = 20; crc.FontSize = 13; resultHeader.Children.Add(message); resultHeader.Children.Add(crc);
        words.MinWidth = 0; words.HorizontalAlignment = HorizontalAlignment.Stretch;
        Add(right, resultHeader); Add(right, field); Add(right, words); Add(right, wordPosition); Add(right, wordBits); Add(right, decoded);
        var diagnostics = new StackPanel(); diagnostics.Children.Add(position); diagnostics.Children.Add(wordInfo); diagnostics.Children.Add(byteInfo); diagnostics.Children.Add(steps);
        Add(right, new Expander { Header = "Byte, tratamiento y diagnóstico", Content = diagnostics }, true);
        var flow = new System.Windows.Controls.Primitives.UniformGrid { Columns = 6, Margin = new Thickness(12, 4, 12, 6) };
        string[] labels = ["MUESTRAS →", "ENERGÍAS →", "BIT RECIBIDO →", "HAMMING →", "CAMPO →", "CRC / UTF-8"];
        for (int i = 0; i < labels.Length; i++) { var item = new StackPanel { Margin = new Thickness(6, 2, 6, 2) }; var label = Text(labels[i], 10); label.Margin = new Thickness(0, 0, 0, 2); flowValues[i].Margin = new Thickness(0); flowValues[i].FontSize = 12; item.Children.Add(label); item.Children.Add(flowValues[i]); flow.Children.Add(item); }
        Grid.SetRow(flow, 2); Grid.SetColumnSpan(flow, 3); root.Children.Add(flow);
        Content = root;
        previous.Click += (_, _) => NavigateRelative(-1);
        next.Click += (_, _) => NavigateRelative(1);
        firstUnknown.Click += (_, _) => NavigateRelative(1, true, true);
        nextUnknown.Click += (_, _) => NavigateRelative(1, true);
        problem.Click += (_, _) => { if (DemodulationSelection.NextProblem(SelectedChannel, Selection?.Word?.EncodedBitOffset ?? Selection?.BlockBitIndex ?? -1) is { } symbol) Navigate(symbol); };
        channel.SelectionChanged += (_, _) => { if (!updating) { listedChannel = -1; Update(data, cursor, true); } };
        symbols.SelectionChanged += (_, _) => { if (!updating && symbols.SelectedItem is ReceivedSymbolTrace t) Navigate(t.Symbol); };
        words.SelectionChanged += (_, _) => { if (!updating && words.SelectedItem is WordChoice w) NavigateBlockBit(w.Trace.EncodedBitOffset); };
    }
    private static Button Nav(string label, string help) => new() { Content = label, ToolTip = help, Padding = new Thickness(7, 4, 7, 4), Margin = new Thickness(1, 2, 1, 2) };
    private static void Add(Grid grid, UIElement element, bool stretch = false)
    {
        int row = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new() { Height = stretch ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        Grid.SetRow(element, row); grid.Children.Add(element);
    }
    private void NavigateRelative(int direction, bool unknownOnly = false, bool fromStart = false)
    {
        if (data is not null && DemodulationSelection.Find(ReceivedSymbols.From(Reception, channel.SelectedIndex == 1, data.Signal.SampleRate),
            Selection?.Trace?.Number ?? 0, direction, unknownOnly, fromStart) is { } trace) Navigate(trace.Symbol);
    }
    private void SetNavigation(IReadOnlyList<ReceivedSymbolTrace> traces)
    {
        int current = Selection?.Trace?.Number ?? 0;
        navigationStatus.Text = traces.Count == 0 ? "Sin símbolos sincronizados disponibles" : $"Símbolo {(current == 0 ? "—" : current)} · {traces.Count} disponibles";
        previous.IsEnabled = DemodulationSelection.Find(traces, current, -1) is not null;
        next.IsEnabled = DemodulationSelection.Find(traces, current, 1) is not null;
        firstUnknown.IsEnabled = DemodulationSelection.Find(traces, current, 1, true, true) is not null;
        nextUnknown.IsEnabled = DemodulationSelection.Find(traces, current, 1, true) is not null;
        problem.IsEnabled = traces.Count > 0 && DemodulationSelection.NextProblem(SelectedChannel, Selection?.Word?.EncodedBitOffset ?? Selection?.BlockBitIndex ?? -1) is not null;
    }
    private void RefreshLocalSymbols(IReadOnlyList<ReceivedSymbolTrace> traces, FdmTextChannelResult? result)
    {
        localSymbols.Children.Clear();
        int selected = Selection?.Trace?.Number ?? 0;
        var current = traces.FirstOrDefault(t => t.Number == selected);
        var choices = new[] { DemodulationSelection.Find(traces, selected, -1), current, DemodulationSelection.Find(traces, selected, 1) };
        for (int i = 0; i < choices.Length; i++)
        {
            var trace = choices[i]; string label = i == 0 ? "Anterior" : i == 1 ? "Seleccionado" : "Siguiente";
            var button = Nav(trace is null ? "—" : $"{(i == 0 ? "←" : i == 1 ? "●" : "→")} {trace.Number}\n{trace.Value}", "Símbolo real recibido; seleccionar para inspeccionar su ventana");
            button.FontSize = 12; button.Padding = new Thickness(3, 2, 3, 2);
            button.IsEnabled = trace is not null;
            if (trace is not null)
            {
                int block = trace.Number - FdmBitstreamGenerator.HeaderBits - 1;
                var word = block >= 0 ? result?.HammingDiagnostics?.Traces.FirstOrDefault(w => w.WordIndex == block / 7) : null;
                string operation = word?.RecoveredPositions.Contains(block % 7 + 1) == true ? "Borrado recuperado" : word?.FlippedPosition == block % 7 + 1 ? "Inversión intentada" : "Sin modificación de esta posición";
                button.Foreground = operation == "Borrado recuperado" ? Brushes.MediumPurple : operation == "Inversión intentada" ? Brushes.SandyBrown : trace.Value == "?" ? Brushes.Khaki : Brushes.LightGray;
                button.ToolTip = $"{label} · Símbolo {trace.Number} · recibido {trace.Value} · {operation}";
                if (i == 1) { button.BorderThickness = new Thickness(2); button.BorderBrush = Brushes.LightSkyBlue; }
                button.Click += (_, _) => Navigate(trace.Symbol);
            }
            localSymbols.Children.Add(button);
        }
    }
    private static TextBlock Text(string value, double size = 12) => new() { Text = value, FontSize = size, Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
    private FdmTextChannelResult? SelectedChannel => channel.SelectedIndex == 1 ? Reception?.ChannelB : Reception?.ChannelA;
    private void Navigate(BfskSymbolResult symbol) { if (data is not null) TimeSelected?.Invoke((symbol.AnalysisStartSample + symbol.AnalysisSampleCount / 2.0) / data.Signal.SampleRate); }
    private void NavigateBlockBit(int bit) { if (DemodulationSelection.SymbolForBlockBit(SelectedChannel, bit) is { } s) Navigate(s); }
    public void SetReception(FdmTextReceiveResult? result, double through) { Reception = result; ReceptionThrough = through; Refresh(); }
    public async void Update(CarrierEnergyMap? current, double time, bool force = false)
    {
        if (!force && ReferenceEquals(data, current) && cursor == time) return;
        data = current; cursor = time; revision++; observation = null; Refresh();
        if (current is null || current.Signal.Origin == MonitorSignalOrigin.Generated) { ObservationUpdated?.Invoke(null); return; }
        // Un símbolo sincronizado ya conserva sus energías y decisión: no volver a detectarlo.
        if (Selection is not null || busy) return;
        busy = true;
        try
        {
            long version;
            do
            {
                version = revision; var snapshot = data; double timeSnapshot = cursor;
                if (snapshot is null || snapshot.Signal.Origin == MonitorSignalOrigin.Generated || Selection is not null) break;
                var value = await Task.Run(() => ReceiverObservation.Analyze(snapshot.Signal, timeSnapshot));
                if (Dispatcher.HasShutdownStarted) return;
                await Dispatcher.InvokeAsync(() => { if (version == revision) { observation = value; Refresh(); ObservationUpdated?.Invoke(value); } });
            } while (version != revision);
        }
        catch (OperationCanceledException) when (Dispatcher.HasShutdownStarted) { }
        catch (ArgumentException ex) { if (!Dispatcher.HasShutdownStarted) await Dispatcher.InvokeAsync(() => reason.Text = ex.Message); }
        finally { busy = false; }
    }
    private void Refresh()
    {
        updating = true;
        try
        {
            if (data is null || data.Signal.Origin == MonitorSignalOrigin.Generated)
            {
                context.Text = data is null ? "Capture o importe un WAV" : "Fuente digital: utilice el laboratorio del Emisor";
                waveform.Clear(); WindowObservation = null; localSymbols.Children.Clear(); energy.Set(null, false); Selection = null; listedResult = null; listedChannel = -1; SetNavigation([]); field.Text = wordPosition.Text = ""; foreach (var value in flowValues) value.Text = "No disponible"; symbols.ItemsSource = null; words.ItemsSource = null; wordBits.Children.Clear();
                window.Text = sync.Text = scope.Text = winner.Text = ratio.Text = criteria.Text = reason.Text = position.Text = wordInfo.Text = decoded.Text = byteInfo.Text = message.Text = crc.Text = steps.Text = ""; decision.Text = "?"; return;
            }
            bool b = channel.SelectedIndex == 1; var result = SelectedChannel;
            context.Text = $"{(data.Signal.Origin == MonitorSignalOrigin.ImportedWav ? "WAV importado" : "Micrófono")} · Canal {(b ? "B" : "A")} · {data.Signal.SampleRate:N0} Hz";
            context.ToolTip = data.Signal.Description;
            var traces = ReceivedSymbols.From(Reception, b, data.Signal.SampleRate).Where(t => t.Symbol.EndSample <= data.Signal.Samples.Length).ToArray();
            double inspectedTime = cursor;
            if (FollowLatest && traces.LastOrDefault() is { } latest)
                inspectedTime = (latest.Symbol.AnalysisStartSample + latest.Symbol.AnalysisSampleCount / 2.0) / data.Signal.SampleRate;
            Selection = DemodulationSelection.From(data.Signal, Reception, b, inspectedTime);
            if (Selection?.Trace?.Symbol.EndSample > data.Signal.Samples.Length) Selection = null;
            RefreshLocalSymbols(traces, result);
            if (!ReferenceEquals(listedResult, Reception) || listedChannel != channel.SelectedIndex)
            {
                symbols.ItemsSource = traces; words.ItemsSource = result?.HammingDiagnostics?.Traces.Select(t => new WordChoice(t)).ToArray(); listedResult = Reception; listedChannel = channel.SelectedIndex;
            }
            symbols.SelectedItem = Selection?.Trace; SetNavigation(traces); field.Text = Selection?.Field(result) ?? "Sin posición de trama";
            var d = Selection?.Detection ?? (b ? observation?.Detection.ChannelB : observation?.Detection.ChannelA);
            int start = Selection?.StartSample ?? (int)Math.Round((observation?.Start ?? 0) * data.Signal.SampleRate);
            int count = Selection?.SampleCount ?? (int)Math.Round(((observation?.End ?? 0) - (observation?.Start ?? 0)) * data.Signal.SampleRate);
            WindowObservation = SymbolWindowObservation.From(data.Signal, Selection, start, count);
            if (WindowObservation is { } measured)
            {
                waveform.Set(data.Signal.Samples.Span.Slice(measured.StartSample, measured.SampleCount),
                    measured.AnalysisStartSample - measured.StartSample, measured.AnalysisSampleCount, b);
                string kind = Selection is null ? "Exploración sin sincronización atribuida" : $"Símbolo {Selection.Trace!.Number}";
                window.Text = $"{kind}: {measured.StartSample / (double)measured.SampleRate:0.000000}–{(measured.StartSample + measured.SampleCount) / (double)measured.SampleRate:0.000} s\n" +
                    $"Detector: {1000.0 * measured.AnalysisSampleCount / measured.SampleRate:0.##} ms · {measured.AnalysisSampleCount} muestras";
                window.ToolTip = $"{kind}: {measured.StartSample / (double)measured.SampleRate:0.000000}–{(measured.StartSample + measured.SampleCount) / (double)measured.SampleRate:0.000000} s · {measured.SampleCount} muestras.\n" +
                    $"Detector: {measured.AnalysisStartSample / (double)measured.SampleRate:0.000000}–{(measured.AnalysisStartSample + measured.AnalysisSampleCount) / (double)measured.SampleRate:0.000000} s. Índices base cero: {measured.StartSample} y {measured.AnalysisStartSample}.";
                scope.Text = $"RMS {measured.AnalysisRms:G3} · pico {measured.AnalysisPeak:G3} (central)";
                scope.ToolTip = $"Símbolo completo: RMS {measured.Rms:G6} · pico {measured.Peak:G6}.\nVentana central: RMS {measured.AnalysisRms:G6} · pico {measured.AnalysisPeak:G6}.\n" +
                    (d?.Bit is int bit ? $"Decisión concluyente: {CarrierEnergyMap.Frequencies[(b ? 2 : 0) + bit]:0} Hz" : "Frecuencia no concluyente · decisión ?");
            }
            else { waveform.Clear(); window.Text = "Esperando muestras disponibles"; scope.Text = "Sin ventana analizada disponible"; }
            sync.Text = Selection is null ? "Ventana exploratoria · sin posición de trama" : Reception?.Synchronization.IsAmbiguous == true ? "Sincronización ambigua" : result?.HeaderValid == true ? "Cabecera validada" : "Sin cabecera validada";
            if (Selection is not null) sync.Text += d?.Bit is int detected ? $" · {CarrierEnergyMap.Frequencies[(b ? 2 : 0) + detected]:0} Hz" : " · decisión ?";
            energy.Set(d, b);
            if (d is null) { winner.Text = ratio.Text = criteria.Text = "Ventana pendiente"; decision.Text = "?"; reason.Text = "Sin decisión disponible"; }
            else
            {
                winner.Text = d.Energy0 == d.Energy1 ? "Sin candidato dominante" : $"Mayor energía: {CarrierEnergyMap.Frequencies[(b ? 2 : 0) + (d.Energy1 > d.Energy0 ? 1 : 0)]:0} Hz";
                double? r = DemodulationSelection.EnergyRatio(d); ratio.Text = $"Ganadora {Math.Max(d.Energy0, d.Energy1):G6} · perdedora {Math.Min(d.Energy0, d.Energy1):G6}\nRelación Eganadora/Eperdedora: {(r.HasValue ? r.Value.ToString("G6") : "No definida")}";
                var diag = d.FdmDiagnostics;
                string State(bool? accepted) => accepted.HasValue ? accepted.Value ? "SÍ" : "NO" : "No evaluado";
                criteria.Text = diag is null ? "Traza de umbrales no disponible" : $"Energía suficiente: {State(diag.EnergyAccepted)}\nSeparación suficiente: {State(diag.RatioAccepted)} · duración: {State(diag.DurationAccepted)}";
                if (diag is not null) ratio.Text += $"\nUmbrales reales: duración ≥ {diag.MinimumDurationSeconds:G6} s\nEnergía ≥ {diag.MinimumToneEnergy:G6} · relación ≥ {diag.MinimumEnergyRatio:G6}";
                decision.Text = d.Bit?.ToString() ?? "?"; decision.Foreground = d.Bit.HasValue ? (b ? Brushes.SandyBrown : Brushes.LightSkyBlue) : Brushes.Khaki;
                reason.Text = Selection is null ? "Exploración acústica; no es un bit de trama. " + d.Reason : d.Reason;
            }
            wordBits.Children.Clear(); words.SelectedItem = words.Items.Cast<WordChoice>().FirstOrDefault(w => w.Trace.WordIndex == Selection?.Word?.WordIndex);
            position.Text = Selection is null ? "Sin símbolo sincronizado seleccionado" : $"Símbolo {Selection.Trace!.Number} → {Selection.Trace.Section}\n{(Selection.BlockBitIndex is int index ? $"Posición {index + 1} del bloque recibido (antes de Hamming)" : "Cabecera: fuera de Hamming y del bloque de mensaje")}";
            var word = Selection?.Word;
            words.Visibility = word is not null ? Visibility.Visible : Visibility.Collapsed;
            wordPosition.Text = word is null ? "" : $"Posición {Selection!.BlockBitIndex!.Value % 7 + 1}/7 · byte del bloque {word.WordIndex / 2 + 1} · nibble {(word.WordIndex % 2 == 0 ? "alto" : "bajo")}";
            if (word is not null)
            {
                string[] roles = ["P1", "P2", "D1", "P4", "D2", "D3", "D4"];
                for (int i = 0; i < 7; i++)
                {
                    int bit = word.EncodedBitOffset + i; char received = i < word.ReceivedBits.Length ? word.ReceivedBits[i] : '—';
                    var button = new Button { Content = $"{roles[i]}\n{received}→{(word.ResultBits is { Length: 7 } output ? output[i] : '—')}", Margin = new Thickness(1), Padding = new Thickness(3), FontSize = 12,
                        ToolTip = $"Posición {i + 1} · {(word.RecoveredPositions.Contains(i + 1) ? "borrado recuperado" : word.FlippedPosition == i + 1 ? "inversión intentada" : "recibido")}", Foreground = word.RecoveredPositions.Contains(i + 1) ? Brushes.MediumPurple : word.FlippedPosition == i + 1 ? Brushes.SandyBrown : Brushes.LightGray };
                    if (Selection?.BlockBitIndex == bit) { button.BorderBrush = b ? Brushes.SandyBrown : Brushes.LightSkyBlue; button.BorderThickness = new Thickness(2); button.FontWeight = FontWeights.Bold; }
                    button.Click += (_, _) => NavigateBlockBit(bit); wordBits.Children.Add(button);
                }
                wordInfo.Text = $"Palabra {word.WordIndex + 1} · {StateName(word.State)}\nSíndrome: {word.Syndrome?.ToString() ?? "No aplicable"} · inversión: {word.FlippedPosition?.ToString() ?? "ninguna"}\nBorrados recuperados: {word.RecoveredPositions.Count}\n{word.Reason}";
                decoded.Text = word.DataBits is null ? "Nibble no recuperado" : $"Nibble decodificado: {word.DataBits}";
                position.Text += $"\nPalabra {word.WordIndex + 1} · posición {Selection!.BlockBitIndex!.Value % 7 + 1}/7\n{field.Text} · byte del bloque {word.WordIndex / 2 + 1}, nibble {(word.WordIndex % 2 == 0 ? "alto" : "bajo")} (MSB-first)";
            }
            else { wordInfo.Text = Selection?.Trace is not null && Selection.BlockBitIndex is null ? "Cabecera: no está protegida por Hamming" : result?.UsesHamming == true ? "Sin traza Hamming para esta posición; puede limitarse al campo de longitud." : "Hamming no aplicado"; decoded.Text = result?.HeaderValid == true && result?.Block is { } pendingBlock ? $"{pendingBlock.Symbols.Count} bits recibidos · {(result.ByteLength is null ? $"longitud pendiente: faltan {Math.Max(0, (result.UsesHamming ? 14 : 8) - pendingBlock.Symbols.Count)} bits para reunir el campo" : $"bloque pendiente: faltan {Math.Max(0, (result.UsesHamming ? (8 + result.ByteLength.Value * 8 + 32) / 4 * 7 : 8 + result.ByteLength.Value * 8 + 32) - pendingBlock.Symbols.Count)} bits")}" : "Sin bloque sincronizado · seleccione un símbolo validado o ejecute Recibir mensajes."; }
            if (word is null && result?.UsesHamming == true && Selection?.BlockBitIndex is int pendingIndex && result.Block is { } partial)
            {
                int first = pendingIndex / 7 * 7, receivedCount = Math.Min(7, partial.Symbols.Count - first);
                string[] roles = ["P1", "P2", "D1", "P4", "D2", "D3", "D4"];
                for (int i = 0; i < receivedCount; i++)
                {
                    int bit = first + i; var button = Nav($"{roles[i]}\n{partial.Symbols[bit].Detection.Bit?.ToString() ?? "?"} → —", $"Palabra {first / 7 + 1} · posición {i + 1}/7 · recibido, todavía sin reconstrucción");
                    button.Click += (_, _) => NavigateBlockBit(bit); wordBits.Children.Add(button);
                }
                decoded.Text = $"Palabra {first / 7 + 1}: {receivedCount}/7 símbolos · nibble no disponible. " + (receivedCount < 7 ? $"Faltan {7 - receivedCount} símbolos para completar la palabra." : result.Reason);
            }
            byteInfo.Text = "Asociación a byte: no disponible";
            if ((Selection?.OriginalBitIndex ?? (Selection?.Word is { } selectedWord ? selectedWord.WordIndex * 4 : (int?)null)) is int original)
            {
                int byteIndex = original / 8;
                byteInfo.Text = Selection?.OriginalBitIndex is null ? $"Paridad: sin bit original directo · palabra del byte {original / 8 + 1}" : $"Bit original {original + 1} · byte del bloque {byteIndex + 1} · posición {original % 8 + 1}/8";
                if (result is not null)
                {
                    string byteField = byteIndex == 0 ? "Byte de longitud" : result.ByteLength is not int length || length is < 1 or > 8 ? "Campo del byte no determinado" : byteIndex <= length ? $"Byte {byteIndex} del contenido UTF-8" : byteIndex <= length + 4 ? $"Byte {byteIndex - length} del campo CRC-32" : "Fuera del bloque";
                    byteInfo.Text += $"\n{byteField}";
                    if (word is not null) byteInfo.Text += $" · palabras {byteIndex * 2 + 1}–{byteIndex * 2 + 2} (nibbles alto y bajo)";
                    string? originals = result.UsesHamming ? result.HammingDiagnostics?.DataBits : result.Block?.Bits;
                    if (originals is not null && originals.Length >= (byteIndex + 1) * 8)
                    {
                        string bits = originals.Substring(byteIndex * 8, 8);
                        byteInfo.Text += bits.All(c => c is '0' or '1') ? $"\nByte {(result.UsesHamming ? "reconstruido" : "recibido")}: {bits} · 0x{Convert.ToByte(bits, 2):X2} (CRC: {(result.CrcValid == true ? "válido" : result.CrcValid == false ? "inválido" : "no comprobado")})" : "\nByte no reconstruido completamente";
                    }
                    if (!result.UsesHamming)
                    {
                        for (int i = 0; i < 8; i++)
                        {
                            int bit = byteIndex * 8 + i;
                            if (DemodulationSelection.SymbolForBlockBit(result, bit) is not { } raw) break;
                            var button = new Button { Content = $"{i + 1}\n{raw.Detection.Bit?.ToString() ?? "?"}", Padding = new Thickness(4), Margin = new Thickness(1), ToolTip = $"Bit recibido {bit + 1} del bloque; clic para localizar su ventana" };
                            button.Click += (_, _) => NavigateBlockBit(bit); wordBits.Children.Add(button);
                        }
                    }
                }
                if (result is not null && byteIndex >= 1)
                {
                    var character = FramePresentation.Characters(result).FirstOrDefault(c => byteIndex - 1 >= c.ByteOffset && byteIndex - 1 < c.ByteOffset + c.Bytes.Count);
                    if (character is not null) byteInfo.Text += $"\nUTF-8 validado: {character.Character} · 0x{character.Bytes[byteIndex - 1 - character.ByteOffset]:X2}\n{character.Bits}";
                }
            }
            crc.Text = $"CRC: {(result?.CrcValid == true ? "Válido" : result?.CrcValid == false ? "Inválido" : "No comprobado")}";
            crc.Foreground = result?.CrcValid == true ? Brushes.LightGreen : result?.CrcValid == false ? Brushes.Salmon : Brushes.Khaki;
            message.Text = result?.State == FdmTextReceiveState.Valid && result.CrcValid == true ? result.Text : "Sin texto validado";
            flowValues[0].Text = count > 0 ? $"{count} muestras reales" : "No disponible";
            flowValues[1].Text = d is null ? "No disponible" : $"E0 {d.Energy0:G3} · E1 {d.Energy1:G3}";
            flowValues[2].Text = d is null ? "No disponible" : $"Recibido: {d.Bit?.ToString() ?? "?"}";
            flowValues[3].Text = result?.UsesHamming != true ? "No aplicado" : word is null ? "Traza no disponible" : StateName(word.State);
            flowValues[4].Text = field.Text;
            flowValues[5].Text = $"{crc.Text} · {(result?.State == FdmTextReceiveState.Valid ? "UTF-8 válido" : "UTF-8 no validado")}";
            steps.Text = $"Cabecera: {(result?.HeaderValid == true ? "validada" : "no validada")} → longitud: {result?.ByteLength?.ToString() ?? "no recuperada"} bytes\nBits → {(result?.UsesHamming == true ? "Hamming → " : "")}CRC → UTF-8\n{result?.Reason ?? "Sin resultado de recepción"}";
        }
        finally { updating = false; }
    }
}

internal sealed class DetectorEnergyView : FrameworkElement
{
    private BfskDetectionResult? detection; private bool b;
    public DetectorEnergyView() { MinHeight = 105; }
    public void Set(BfskDetectionResult? result, bool channelB) { detection = result; b = channelB; InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc)
    {
        if (detection is not {} d) return;
        void Text(string text, double y) => dc.DrawText(new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, Brushes.LightGray, VisualTreeHelper.GetDpi(this).PixelsPerDip), new Point(0, y));
        if (d.FdmDiagnostics?.DurationAccepted == false) { Text("Fragmento corto: energías no calculadas", 10); return; }
        double min = d.FdmDiagnostics?.MinimumToneEnergy ?? 0, max = Math.Max(min, Math.Max(d.Energy0, d.Energy1)) * 1.1;
        if (max == 0) max = 1;
        for (int i = 0; i < 2; i++)
        {
            double energy = i == 0 ? d.Energy0 : d.Energy1, y = i * Math.Max(42, (ActualHeight - 25) / 2);
            Text($"{CarrierEnergyMap.Frequencies[(b ? 2 : 0) + i]:0} Hz → {i} · E={energy:G6}", y);
            double width = Math.Max(1, ActualWidth - 8);
            dc.DrawRectangle(Brushes.DarkSlateGray, null, new Rect(0, y + 23, width, 15));
            dc.DrawRectangle(b ? Brushes.SandyBrown : Brushes.LightSkyBlue, null, new Rect(0, y + 23, energy / max * width, 15));
            if (min > 0) dc.DrawLine(new Pen(Brushes.Khaki, 2), new(min / max * width, y + 20), new(min / max * width, y + 41));
        }
        Text($"Escala lineal 0–{max:G3} · marca amarilla: E mínima", ActualHeight - 17);
    }
}
internal sealed class DetectionWindowView : FrameworkElement
{
    private WaveformBucket[] values = [];
    private double analysisFrom, analysisTo;
    private int sampleCount;
    private bool channelB;
    public DetectionWindowView() { MinHeight = 90; ToolTip = "Onda completa del símbolo. El área sombreada es la ventana realmente utilizada por el detector; escala solo visual."; }
    public void Clear() { values = []; InvalidateVisual(); }
    public void Set(ReadOnlySpan<float> samples, int analysisOffset, int analysisCount, bool b)
    {
        values = WaveformView.Reduce(samples, 400); sampleCount = samples.Length;
        analysisFrom = analysisOffset / (double)sampleCount; analysisTo = (analysisOffset + analysisCount) / (double)sampleCount;
        channelB = b; InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        if (values.Length == 0) return;
        double peak = values.Max(b => Math.Max(Math.Abs(b.Minimum), Math.Abs(b.Maximum))), scale = peak > 0 ? peak * 1.1 : 1;
        double half = Math.Max(25, (ActualHeight - 28) / 2), width = ActualWidth;
        var accent = channelB ? Colors.SandyBrown : Colors.LightSkyBlue;
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(35, accent.R, accent.G, accent.B)), null, new Rect(analysisFrom * width, 0, (analysisTo - analysisFrom) * width, half * 2));
        var limits = new Pen(new SolidColorBrush(accent), 1);
        dc.DrawLine(limits, new(analysisFrom * width, 0), new(analysisFrom * width, half * 2));
        dc.DrawLine(limits, new(analysisTo * width, 0), new(analysisTo * width, half * 2));
        for (int i = 0; i < values.Length; i++)
        {
            double x = (i + .5) / values.Length * width;
            dc.DrawLine(new Pen(Brushes.LightGray, 1), new(x, half * (1 - values[i].Maximum / scale)), new(x, half * (1 - values[i].Minimum / scale)));
        }
        dc.DrawText(new FormattedText($"±{scale:G3} · {sampleCount} muestras · sombreado: detector", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, Brushes.LightGray, VisualTreeHelper.GetDpi(this).PixelsPerDip) { MaxTextWidth = Math.Max(1, ActualWidth), Trimming = TextTrimming.CharacterEllipsis, MaxLineCount = 1 }, new Point(0, ActualHeight - 20));
    }
}
