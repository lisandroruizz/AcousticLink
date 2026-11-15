using AcousticLink.App.Protocol;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AcousticLink.App.Visualization;

// Observación de muestras reales. La energía se calcula después de finalizar; no decide bits.
public sealed class SignalMonitorView : UserControl
{
    private readonly TextBlock source = Label("Sin señal disponible · capture, importe un WAV o prepare una emisión.");
    private bool singleChannel;
    public bool SingleChannel
    {
        get => singleChannel;
        set
        {
            singleChannel = value;
            for (int i = 2; i < energyValues.Children.Count; i++) energyValues.Children[i].Visibility = value ? Visibility.Collapsed : Visibility.Visible;
            energyValues.Columns = value ? 2 : 4;
            ((TabItem)signalTools.Items[1]).Visibility = value || ReceiverMode ? Visibility.Collapsed : Visibility.Visible;
            contribution.Visibility = value || ReceiverMode ? Visibility.Collapsed : Visibility.Visible;
            receptionProgress.SingleChannel = value;
            interpretation.SingleChannel = value;
            receptionStrip.SingleChannel = value;
            symbolStrip.SingleChannel = value;
            carriers.ClearCache(); carriers.InvalidateVisual();
            if (map is not null) SetCursor(cursor);
        }
    }
    private readonly TextBlock inspectionContext = Label("Inspección posterior");
    private readonly Button labReplay = new() { Content = "Recorrer WAV", Padding = new Thickness(8, 3, 8, 3), FontSize = 12 };
    private readonly Button labFollow = new() { Content = "Pausar seguimiento / volver al vivo", Padding = new Thickness(8, 3, 8, 3), FontSize = 12 };
    private readonly ReceptionProgressView receptionProgress = new() { Compact = true, Visibility = Visibility.Collapsed };
    public ReceptionActivity? ReceptionActivity => receptionProgress.Activity;
    public void SetReceptionActivity(ReceptionActivity activity) { receptionProgress.Show(activity); receptionProgress.Visibility = ReceiverMode ? Visibility.Visible : Visibility.Collapsed; }
    public void ClearReceptionActivity() { receptionProgress.Clear(); receptionProgress.Visibility = Visibility.Collapsed; }
    private readonly TextBlock settings = Label("");
    private readonly TextBlock cursorInfo = Label("");
    private readonly TextBlock energies = Label("");
    private readonly System.Windows.Controls.Primitives.UniformGrid energyValues = new() { Columns = 4 };
    private readonly TextBox from = new() { Padding = new Thickness(6, 2, 6, 2), VerticalContentAlignment = VerticalAlignment.Center, MinHeight = 30, Height = 32, FontSize = 12, Width = 86, Text = "0" };
    private readonly TextBox to = new() { Padding = new Thickness(6, 2, 6, 2), VerticalContentAlignment = VerticalAlignment.Center, MinHeight = 30, Height = 32, FontSize = 12, Width = 86, Text = "0" };
    private readonly ComboBox scale = new() { Width = 185, MinHeight = 30, Height = 32, FontSize = 12, ItemsSource = new[] { "Ajustar visualización", "Escala original ±1" }, SelectedIndex = 0 };
    private readonly ReceiverInterpretationView interpretation = new();
    private readonly ReceivedSymbolsView symbolStrip = new();
    private IReadOnlyList<ReceivedSymbolTrace> receivedA = [], receivedB = [];
    public IReadOnlyList<ReceivedSymbolTrace> ReceivedChannelA => receivedA;
    public IReadOnlyList<ReceivedSymbolTrace> ReceivedChannelB => receivedB;
    public string SymbolDecisionText => decisions.Text;
    public void ReportObservationFailure(string message) { decisions.Text = "Error de observación: " + message; }
    private readonly ReceiverFrameStrip receptionStrip = new();
    private readonly StackPanel overviewPane = new();
    private readonly StackPanel footer = new() { Margin = new Thickness(0, 6, 0, 0) };
    private bool receiverMode;
    public bool ReceiverMode
    {
        get => receiverMode;
        set { receiverMode = value; ((TabItem)signalTools.Items[1]).Visibility = value ? Visibility.Collapsed : Visibility.Visible; contribution.Visibility = value ? Visibility.Collapsed : Visibility.Visible; ((TabItem)signalTools.Items[2]).Visibility = value ? Visibility.Visible : Visibility.Collapsed; if (value) { source.TextWrapping = TextWrapping.NoWrap; source.TextTrimming = TextTrimming.CharacterEllipsis;
            from.Width = to.Width = 65; scale.Width = 165;
            foreach (var button in ((Grid)Content).Children.OfType<WrapPanel>().SelectMany(p => p.Children.OfType<Button>()))
            {
                string text = button.Content?.ToString() ?? "";
                button.ToolTip = text;
                button.Content = text switch { "Aplicar vista" => "Aplicar", "Ampliar selección" => "Selección", "Recorrer análisis (sin audio)" => "Recorrer WAV", "Ver completa" => "Completa", _ => button.Content };
            }
        } }
    }
    private readonly TextBlock decisions = Label("A: ? · B: ? · ventana sin evaluar");
    private readonly System.Windows.Threading.DispatcherTimer inspectionTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly System.Diagnostics.Stopwatch inspectionClock = new();
    private double inspectionStart;
    public bool InspectionRunning => inspectionTimer.IsEnabled;
    public void SetReception(FdmTextReceiveResult? result, double through = 0)
    {
        if (result is not null && interpretation.Reception is not null && through < interpretation.ReceptionThrough) return;
        receptionStrip.Reception = result; receptionStrip.InvalidateVisual(); interpretation.SetReception(result, through);
        receivedA = ReceivedSymbols.From(result, false, map?.Signal.SampleRate ?? 48000);
        receivedB = SingleChannel ? [] : ReceivedSymbols.From(result, true, map?.Signal.SampleRate ?? 48000);
        if (map is not null) SetCursor(cursor); else RefreshSelection();
    }
    public void StartInspection()
    {
        if (live || map is null) return;
        inspectionStart = cursor >= map.Signal.Duration ? 0 : cursor; inspectionClock.Restart(); inspectionTimer.Start(); inspectionContext.Text = "Recorrer WAV · sin reproducción ni recepción nueva"; mode.Text = "Recorrido de análisis · sin reproducción de audio";
    }
    public void StopInspection() { if (inspectionTimer.IsEnabled) mode.Text = "Recorrido de análisis pausado · sin audio"; inspectionTimer.Stop(); inspectionClock.Stop(); inspectionContext.Text = live ? "Captura activa · observación acústica" : "Inspección posterior · recorrido detenido"; }
    private readonly FdmFormationView formation = new();
    private readonly TabControl signalTools = new() { Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
    private readonly SignalMonitorPlot waveform;
    private readonly SignalMonitorPlot carriers;
    private readonly OverviewNavigator navigator;
    private readonly Button followButton = new() { Content = "Volver al vivo / pausar", Visibility = Visibility.Collapsed, MinHeight = 30, Height = 32, Padding = new Thickness(8, 3, 8, 3), FontSize = 12 };
    private readonly TextBlock mode = Label("Seleccionar intervalo");
    private readonly ComboBox contribution = new() { Width = 160, MinHeight = 30, Height = 32, FontSize = 12, ItemsSource = new[] { "Compuesta A+B", "Canal A", "Canal B" }, SelectedIndex = 0 };
    private ReadOnlyMemory<float> channelA, channelB;
    public bool FollowingCapture { get; private set; }
    private bool live;
    private bool fromBeginning;
    public double LiveWindowSeconds { get; set; } = 3;
    public bool IsLive => live;
    public event Action<CarrierEnergyMap?, WaveformBucket[], double, double, bool>? FrameDisplayed;
    public void BeginLive(string description) { Clear(description); }
    public void FollowFromStart() { if (!live || map is null) return; fromBeginning = true; FollowingCapture = true; ApplyFollow(); }
    private void ApplyFollow() { if (map is null) return; mode.Text = fromBeginning ? "En vivo · desde el inicio" : $"En vivo · últimos {LiveWindowSeconds} s"; Zoom(signalTools.SelectedIndex == 1 && map.Signal.Origin == MonitorSignalOrigin.Generated ? Math.Max(0, map.Signal.Duration - .01) : fromBeginning ? 0 : Math.Max(0, map.Signal.Duration - LiveWindowSeconds), map.Signal.Duration); }
    private ReadOnlyMemory<float> DisplaySamples => contribution.SelectedIndex == 1 && !channelA.IsEmpty ? channelA : contribution.SelectedIndex == 2 && !channelB.IsEmpty ? channelB : map!.Signal.Samples;
    public void SetContributions(ReadOnlyMemory<float> a, ReadOnlyMemory<float> b)
    { channelA = a; channelB = b; contribution.IsEnabled = !a.IsEmpty && !b.IsEmpty; contribution.SelectedIndex = 0; }
    public void SuspendFollowing() { StopInspection(); FollowingCapture = false; inspectionContext.Text = live ? "Captura activa · inspección histórica (el audio continúa)" : "Inspección posterior"; mode.Text = live ? "Seguimiento visual pausado · el audio continúa" : "Inspección manual"; }
    public void Pan(double seconds) { if (map is null) return; SuspendFollowing(); CenterAt((start + end) / 2 + seconds); }
    public void CenterAt(double seconds) { if (map is null) return; double width = end - start; double a = Math.Clamp(seconds - width / 2, 0, Math.Max(0, map.Signal.Duration - width)); a = Math.Round(a * map.Signal.SampleRate) / map.Signal.SampleRate; Zoom(a, Math.Min(map.Signal.Duration, a + width)); }
    public void ZoomAt(double seconds, double factor)
    {
        if (map is null) return; SuspendFollowing();
        double fraction = Math.Clamp((seconds - start) / (end - start), 0, 1);
        double width = Math.Clamp((end - start) * factor, Math.Min(2.0 / map.Signal.SampleRate, map.Signal.Duration), map.Signal.Duration);
        double a = Math.Clamp(seconds - fraction * width, 0, map.Signal.Duration - width); Zoom(a, a + width); SetCursor(seconds);
    }
    public void SetWindowDuration(double seconds) { if (map is null) return; SuspendFollowing(); double width = Math.Min(seconds, map.Signal.Duration); double a = Math.Clamp(cursor - width / 2, 0, map.Signal.Duration - width); Zoom(a, a + width); }
    public void FollowCapture() { if (!live || map is null) return; FollowingCapture = true; fromBeginning = false; ApplyFollow(); }
    public void UpdateLive(CarrierEnergyMap data)
    {
        if (ReceiverMode && data.Signal.Origin == MonitorSignalOrigin.Generated) return;
        StopInspection();
        followButton.Visibility = Visibility.Visible; labFollow.IsEnabled = true;
        bool first = !live; double a = start, b = end; map = data; live = true;
        channelA = data.Signal.ChannelA; channelB = data.Signal.ChannelB;
        contribution.IsEnabled = !channelA.IsEmpty && !channelB.IsEmpty;
        if (!contribution.IsEnabled) contribution.SelectedIndex = 0;
        source.Text = $"{(data.Signal.Origin == MonitorSignalOrigin.Generated ? "Señal digital emitida · salida, no micrófono" : "Micrófono · capturando")} · {data.Signal.Duration:0.000} s · {data.Signal.SampleRate} Hz";
        settings.Text = data.Signal.Description + " · energía incremental 20/10 ms · escala −100…0 dB digital, no decisiones de bits";
        carriers.ClearCache();
        if (first) { FollowingCapture = true; fromBeginning = false; }
        if (FollowingCapture) ApplyFollow(); else Zoom(a, Math.Min(b, data.Signal.Duration));
        SetCursor(FollowingCapture ? data.Signal.Duration : cursor); navigator.InvalidateVisual();
    }
    public void FinishLive(CarrierEnergyMap data)
    {
        if (ReceiverMode && data.Signal.Origin == MonitorSignalOrigin.Generated) return;
        if (!live) { Show(data); return; }
        double a = start, b = end; bool followed = FollowingCapture;
        if (ReceiverMode && followed && interpretation.Selection?.Trace is { } lastSymbol)
            cursor = (lastSymbol.Symbol.AnalysisStartSample + lastSymbol.Symbol.AnalysisSampleCount / 2.0) / data.Signal.SampleRate;
        live = false; FollowingCapture = false; map = data; followButton.Visibility = Visibility.Collapsed; labFollow.IsEnabled = false;
        channelA = data.Signal.ChannelA; channelB = data.Signal.ChannelB; contribution.IsEnabled = !channelA.IsEmpty && !channelB.IsEmpty;
        source.Text = data.Signal.OriginLabel + " · detenida · " + data.Signal.Description;
        mode.Text = "Operación finalizada · inspección del audio conservado";
        if (followed) Zoom(0, data.Signal.Duration); else Zoom(a, Math.Min(b, data.Signal.Duration));
        SetCursor(cursor); carriers.ClearCache();
    }
    private CancellationTokenSource? reduction;
    private CarrierEnergyMap? map;
    private double start, end, selectedStart, selectedEnd, cursor;
    public CarrierEnergyMap? Data => map;
    public double ViewStart => start;
    public double ViewEnd => end;
    public double CursorSeconds => cursor;
    public double SelectionStart => selectedStart;
    public double SelectionEnd => selectedEnd;
    public string CursorDescription => energies.Text;

    public SignalMonitorView()
    {
        var root = new Grid { Margin = new Thickness(4) };
        foreach (var height in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1.2, GridUnitType.Star), new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto })
            root.RowDefinitions.Add(new RowDefinition { Height = height });
        root.Children.Add(source);
        Grid.SetRow(receptionProgress, 1); root.Children.Add(receptionProgress);
        inspectionTimer.Tick += (_, _) => { if (map is null) { StopInspection(); return; } double time = Math.Min(map.Signal.Duration, inspectionStart + inspectionClock.Elapsed.TotalSeconds); SetCursor(time); double a = Math.Max(0, time - LiveWindowSeconds); if (time > 0) Zoom(a, Math.Max(time, Math.Min(map.Signal.Duration, 1.0 / map.Signal.SampleRate))); if (time >= map.Signal.Duration) StopInspection(); };
        interpretation.ObservationUpdated += value =>
        {
            if (value is null) { decisions.Text = "A: ? · B: ? · sin ventana"; return; }
            string Describe(AcousticLink.App.Signals.BfskDetectionResult d, bool b) => d.Bit is int bit ? $"{bit} · {CarrierEnergyMap.Frequencies[(b ? 2 : 0) + bit]:0} Hz" : "? · " + d.Reason;
            if (receiverMode || receivedA.Count + receivedB.Count > 0) { UpdateSymbolSelection(); return; }
            decisions.Text = $"Ventana exploratoria, no símbolo sincronizado · A: {Describe(value.Detection.ChannelA, false)}   |   B: {Describe(value.Detection.ChannelB, true)} · ventana {value.Start:0.000}–{value.End:0.000} s";
        };
        for (int i = 0; i < 4; i++) energyValues.Children.Add(Label(new[] { "A0 · 1200 Hz", "A1 · 1900 Hz", "B0 · 3000 Hz", "B1 · 3700 Hz" }[i] + " · sin datos"));
        var tools = new WrapPanel { Margin = new Thickness(0, 8, 0, 4) };
        tools.Children.Add(Label("Vista (s):")); tools.Children.Add(from); tools.Children.Add(to);
        void Button(string text, Action action)
        {
            var button = new Button { Content = text, Margin = new Thickness(4, 0, 0, 4), MinHeight = 30, Height = 32, FontSize = 12, Padding = new Thickness(8, 3, 8, 3) };
            button.Click += (_, _) => action(); tools.Children.Add(button);
        }
        Button("Aplicar vista", () =>
        {
            if (map is null) return;
            if (double.TryParse(from.Text, out double a) && double.TryParse(to.Text, out double b))
            { try { SuspendFollowing(); Zoom(a, b); } catch (ArgumentException) { cursorInfo.Text = "Intervalo inválido: use segundos dentro de la señal."; } }
            else cursorInfo.Text = "Ingrese tiempos numéricos en segundos.";
        });
        Button("Ampliar selección", () => { if (map is null || selectedEnd <= selectedStart) return; SuspendFollowing(); Zoom(selectedStart, selectedEnd); });
        Button("＋", () => ChangeZoom(0.5)); Button("−", () => ChangeZoom(2));
        Button("◀", () => Pan(-(end - start) * .75)); Button("▶", () => Pan((end - start) * .75));
        var presets = new ComboBox { Width = 110, MinHeight = 30, Height = 32, FontSize = 12, ItemsSource = new[] { "Ventana…", "2 ms", "5 ms", "10 ms", "20 ms", "100 ms", "1 s" }, SelectedIndex = 0 };
        presets.SelectionChanged += (_, _) => { double[] widths = [0, .002, .005, .01, .02, .1, 1]; if (presets.SelectedIndex > 0) SetWindowDuration(widths[presets.SelectedIndex]); }; tools.Children.Add(presets);
        followButton.Click += (_, _) => { if (FollowingCapture) SuspendFollowing(); else FollowCapture(); }; tools.Children.Add(followButton);
        var liveMode = new ComboBox { Width = 140, ItemsSource = new[] { "Último 1 s", "Últimos 2 s", "Últimos 3 s", "Últimos 5 s", "Desde el inicio" }, SelectedIndex = 2, FontSize = 12, MinHeight = 30, Height = 32 };
        liveMode.SelectionChanged += (_, _) => { if (liveMode.SelectedIndex == 4) FollowFromStart(); else { double[] seconds = [1, 2, 3, 5]; LiveWindowSeconds = seconds[liveMode.SelectedIndex]; FollowCapture(); } }; tools.Children.Add(liveMode);
        Button("Recorrer análisis (sin audio)", () => { if (InspectionRunning) StopInspection(); else StartInspection(); });
        Button("Ver completa", () => { if (map is not null) { SuspendFollowing(); Zoom(0, map.Signal.Duration); } });
        scale.Margin = new Thickness(8, 0, 0, 4); tools.Children.Add(scale);
        contribution.IsEnabled = false; tools.Children.Add(contribution);
        contribution.SelectionChanged += (_, _) => { if (map is not null) { waveform?.InvalidateVisual(); _ = ReduceVisibleAsync(); } };
        scale.SelectionChanged += (_, _) => { waveform?.InvalidateVisual(); };
        var labToolbar = new WrapPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 2) };
        labFollow.Click += (_, _) => { if (FollowingCapture) SuspendFollowing(); else FollowCapture(); };
        labToolbar.Children.Add(labFollow); labReplay.Click += (_, _) => { if (InspectionRunning) StopInspection(); else StartInspection(); }; labToolbar.Children.Add(labReplay); labToolbar.Children.Add(inspectionContext);
        Grid.SetRow(labToolbar, 2); root.Children.Add(labToolbar);
        Grid.SetRow(tools, 2); root.Children.Add(tools);
        waveform = new SignalMonitorPlot(this, false); carriers = new SignalMonitorPlot(this, true);
        var signalPane = new Grid { MinHeight = 250 }; signalPane.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star), MinHeight = 80 }); signalPane.RowDefinitions.Add(new() { Height = new GridLength(1.3, GridUnitType.Star), MinHeight = 112 }); waveform.MinHeight = 80;
        signalPane.RowDefinitions.Add(new() { Height = GridLength.Auto }); signalPane.Children.Add(waveform); Grid.SetRow(carriers, 1); signalPane.Children.Add(carriers); Grid.SetRow(symbolStrip, 2); signalPane.Children.Add(symbolStrip);
        symbolStrip.Selected += symbol => { SuspendFollowing(); SelectInterval(symbol.Symbol.StartSample / (double)map!.Signal.SampleRate, symbol.Symbol.EndSample / (double)map.Signal.SampleRate); SetCursor((symbol.Symbol.AnalysisStartSample + symbol.Symbol.AnalysisSampleCount / 2.0) / map.Signal.SampleRate); };
        signalTools.Items.Add(new TabItem { Header = "Señal y detección", Content = new ScrollViewer { Content = signalPane, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } });
        signalTools.Items.Add(new TabItem { Header = "Cómo se forma FDM", Content = formation });
        signalTools.Items.Add(new TabItem { Header = "Cómo la interpreta", Content = interpretation });
        Grid.SetRow(signalTools, 3); Grid.SetRowSpan(signalTools, 2); root.Children.Add(signalTools);
        formation.TimeSelected += time => { SuspendFollowing(); SetCursor(time); if (time < start || time >= end) CenterAt(time); };
        signalTools.SelectionChanged += (_, e) => { if (!ReferenceEquals(e.OriginalSource, signalTools)) return; bool lab = receiverMode && signalTools.SelectedIndex == 2; tools.Visibility = lab ? Visibility.Collapsed : Visibility.Visible; overviewPane.Visibility = lab ? Visibility.Collapsed : Visibility.Visible; labToolbar.Visibility = lab ? Visibility.Visible : Visibility.Collapsed; footer.Visibility = lab ? Visibility.Collapsed : Visibility.Visible; cursorInfo.Visibility = settings.Visibility = signalTools.SelectedIndex != 0 ? Visibility.Collapsed : Visibility.Visible;  if (signalTools.SelectedIndex == 1 && map?.Signal.Origin == MonitorSignalOrigin.Generated) { if (live && FollowingCapture) ApplyFollow(); else SetWindowDuration(.01); } formation.Update(map, start, end, cursor); };
        navigator = new OverviewNavigator(this) { Height = 36, Margin = new Thickness(0, 3, 0, 0) }; overviewPane.Children.Add(navigator); overviewPane.Children.Add(receptionStrip); Grid.SetRow(overviewPane, 5); root.Children.Add(overviewPane);

        footer.Children.Add(Label("Izq: seleccionar · central / Mayús+arrastre: desplazar · Ctrl+rueda: zoom · Mayús+rueda: desplazar · doble clic: centrar")); footer.Children.Add(mode); footer.Children.Add(decisions); footer.Children.Add(cursorInfo); footer.Children.Add(energyValues); footer.Children.Add(settings); footer.Children.Add(Label("FDM: dos señales BFSK simultáneas en bandas distintas. Brillo = energía estimada; no confirma un bit válido."));
        var secondaryDetails = new StackPanel();
        foreach (var child in footer.Children.Cast<UIElement>().ToArray()) { footer.Children.Remove(child); if (!ReferenceEquals(child, decisions) && !ReferenceEquals(child, energyValues)) secondaryDetails.Children.Add(child); }
        footer.Children.Add(decisions); footer.Children.Add(energyValues);
        footer.Children.Add(new Expander { Header = "Detalles de observación", Content = secondaryDetails, IsExpanded = false });
        Grid.SetRow(footer, 6); root.Children.Add(footer); Content = root;
        ((TabItem)signalTools.Items[2]).Visibility = receiverMode ? Visibility.Visible : Visibility.Collapsed;
        interpretation.TimeSelected += time => { SuspendFollowing(); SetCursor(time); if (interpretation.Selection?.Trace is { } trace && map is not null) SelectInterval(trace.Symbol.StartSample / (double)map.Signal.SampleRate, trace.Symbol.EndSample / (double)map.Signal.SampleRate); if (time < start || time >= end) CenterAt(time); };
        Unloaded += (_, _) => { reduction?.Cancel(); StopInspection(); };
        Loaded += (_, _) => { if (map is not null) _ = ReduceVisibleAsync(); };
    }
    private static TextBlock Label(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray, FontSize = 12, Margin = new Thickness(0, 2, 8, 2) };
    public void Clear(string message = "Sin señal disponible")
    {
        labFollow.IsEnabled = false; ClearReceptionActivity(); StopInspection(); SetReception(null); interpretation.Update(null, 0); receptionStrip.Update(null, 0); decisions.Text = "A: ? · B: ? · sin ventana"; followButton.Visibility = Visibility.Collapsed; reduction?.Cancel(); live = false; FollowingCapture = false; SetContributions(default, default); map = null; navigator.InvalidateVisual(); source.Text = message; cursorInfo.Text = energies.Text = settings.Text = "";
        formation.Update(null, 0, 0, 0);
        foreach (TextBlock value in energyValues.Children) value.Text = "Sin datos";
        waveform.Buckets = []; carriers.ClearCache(); waveform.InvalidateVisual(); carriers.InvalidateVisual(); FrameDisplayed?.Invoke(null, [], 0, 0, false);
    }
    public void Show(CarrierEnergyMap data)
    {
        if (ReceiverMode && data.Signal.Origin == MonitorSignalOrigin.Generated) return;
        bool sameSource = map?.Signal.SourceId is not null && ReferenceEquals(map.Signal.SourceId, data.Signal.SourceId) && map.Signal.SampleRate == data.Signal.SampleRate;
        double inspectedTime = cursor;
        if (map is not null && !sameSource && (!ReferenceEquals(map.Signal.SourceId, data.Signal.SourceId) || !map.Signal.Samples.Equals(data.Signal.Samples))) SetReception(null);
        StopInspection();
        followButton.Visibility = Visibility.Collapsed; labFollow.IsEnabled = false; live = false; FollowingCapture = false; map = data; SetContributions(data.Signal.ChannelA, data.Signal.ChannelB); carriers.ClearCache();
        source.Text = $"{data.Signal.OriginLabel}\n{data.Signal.Description} · {data.Signal.SampleRate:N0} Hz · {data.Signal.Duration:0.000} s · pico {data.Peak:G5}";
        if (receiverMode) source.Text = source.Text.Replace("\n", " · ");
        settings.Text = $"Ventana rectangular {1000.0 * data.WindowSamples / data.Signal.SampleRate:0.###} ms · paso {1000.0 * data.HopSamples / data.Signal.SampleRate:0.###} ms (última anclada al final). " +
            "Energía Goertzel sin media: 10 log₁₀(P/1), escala de color −100…0 dB digital; cero = −∞. No es dB SPL, espectrograma completo ni decisión de bits.";
        Zoom(0, data.Signal.Duration); SelectInterval(0, Math.Min(data.Signal.Duration, .1)); SetCursor(sameSource ? Math.Clamp(inspectedTime, 0, data.Signal.Duration) : 0);
    }
    public void SelectInterval(double a, double b)
    {
        if (map is null) return;
        _ = CarrierEnergyMap.Interval(a, b, map.Signal.SampleRate, map.Signal.Samples.Length);
        selectedStart = a; selectedEnd = b;
        SetCursor(cursor);
    }
    public void Zoom(double a, double b)
    {
        if (map is null) return;
        var interval = CarrierEnergyMap.Interval(a, b, map.Signal.SampleRate, map.Signal.Samples.Length);
        start = interval.Start / (double)map.Signal.SampleRate; end = interval.End / (double)map.Signal.SampleRate;
        from.Text = start.ToString("0.#########"); to.Text = end.ToString("0.#########"); navigator.InvalidateVisual();
        waveform.Buckets = []; // No mostrar la reducción del intervalo anterior bajo ejes nuevos.
        waveform.InvalidateVisual(); carriers.InvalidateVisual(); if (signalTools.SelectedIndex == 1) formation.Update(map, start, end, cursor); _ = ReduceVisibleAsync();
    }
    private void ChangeZoom(double factor) => ZoomAt(cursor, factor);
    private async Task ReduceVisibleAsync()
    {
        reduction?.Cancel(); reduction?.Dispose(); reduction = new();
        var token = reduction.Token; var current = map; double a = start, b = end;
        if (current is null) return;
        try
        {
            var interval = CarrierEnergyMap.Interval(a, b, current.Signal.SampleRate, current.Signal.Samples.Length);
            var samples = DisplaySamples;
            var values = current.OverviewSamplesPerBucket == 0 && contribution.SelectedIndex == 0 && a == 0 && b == current.Signal.Duration ? current.Overview.ToArray() : await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                return WaveformView.Reduce(samples.Span.Slice(interval.Start, interval.Count), 1600);
            }, token);
            if (token.IsCancellationRequested || !ReferenceEquals(current, map) || a != start || b != end) return;
            waveform.Buckets = values; waveform.InvalidateVisual(); FrameDisplayed?.Invoke(current, values, a, b, live);
        }
        catch (OperationCanceledException) { }
    }
    public void SetCursor(double seconds)
    {
        if (map is null) return;
        cursor = Math.Clamp(seconds, 0, map.Signal.Duration);
        if (map.Windows.Count == 0) { cursorInfo.Text = $"Cursor {cursor:0.000000} s · grabando"; RefreshSelection(); return; }
        var frame = map.Windows[map.WindowAt(cursor)];
        for (int i = 0; i < 4; i++) ((TextBlock)energyValues.Children[i]).Text = $"{new[] { "A0", "A1", "B0", "B1" }[i]} · {CarrierEnergyMap.Frequencies[i]:0} Hz\n{frame.Energy(i):G4} · {CarrierEnergyMap.Decibels(frame.Energy(i)):0.0} dB";
        energies.Text = string.Join(" · ", Enumerable.Range(0, SingleChannel ? 2 : 4).Select(i => $"{CarrierEnergyMap.Frequencies[i]:0} Hz: {frame.Energy(i):G4} ({CarrierEnergyMap.Decibels(frame.Energy(i)):0.0} dB)"));
        cursorInfo.Text = $"Cursor {cursor:0.000000} s · selección {selectedStart:0.000000}–{selectedEnd:0.000000} s · energía de ventana [{frame.StartSample / (double)map.Signal.SampleRate:0.000}, {(frame.StartSample + frame.SampleCount) / (double)map.Signal.SampleRate:0.000}] s.";
        if (map.Signal.BfskFrameBits is { } bits && bits.Length > 0)
        {
            int symbol = Math.Clamp((int)(cursor / AcousticLink.App.Signals.BfskSequenceDecoder.BitDurationSeconds), 0, bits.Length - 1);
            cursorInfo.Text += $" · Símbolo BFSK generado #{symbol + 1}/{bits.Length}: {bits[symbol]} · " +
                (bits[symbol] == '0' ? "1200 Hz" : "1900 Hz");
        }
        RefreshSelection();
    }
    private void RefreshSelection() { labReplay.IsEnabled = !live && map is not null; inspectionContext.Text = live ? FollowingCapture ? "Captura activa · seguimiento visual" : "Captura activa · inspección histórica (recepción continúa)" : InspectionRunning ? "Recorrer WAV · sin reproducción ni recepción nueva" : "Inspección posterior"; UpdateSymbolSelection(); bool follow = live && FollowingCapture; bool changedFollow = interpretation.FollowLatest != follow; interpretation.FollowLatest = follow; interpretation.Update(map, cursor, changedFollow); receptionStrip.Update(map, cursor); waveform.InvalidateVisual(); carriers.InvalidateVisual(); if (signalTools.SelectedIndex == 1) formation.Update(map, start, end, cursor); }

    private void UpdateSymbolSelection()
    {
        if (map is null) { symbolStrip.Update([], [], 0, 0, 0, 0); return; }
        int rate = map.Signal.SampleRate;
        symbolStrip.Update(receivedA, receivedB, rate, start, end, cursor);
        var a = ReceivedSymbols.At(receivedA, cursor, rate); var b = ReceivedSymbols.At(receivedB, cursor, rate);
        if (a is null && b is null) { if (receiverMode) decisions.Text = "Sin símbolo sincronizado en este instante · energía ≠ decisión de símbolo"; return; }
        string Describe(ReceivedSymbolTrace? symbol) => symbol is null ? "No disponible" : $"#{symbol.Number}: {symbol.Value} · {symbol.Section} · {(symbol.Frequency is double f ? f.ToString("0") + " Hz" : "inconcluso")}";
        decisions.Text = SingleChannel ? $"BFSK {Describe(a)}" : $"A {Describe(a)}   |   B {Describe(b)}";
        cursorInfo.Text = $"Cursor {cursor:0.000000} s · mapa: ventanas de 20 ms · tarjetas: decisiones reales del símbolo (soporte exacto en ayuda)";
        foreach (var pair in new[] { (a, 0), (b, 2) })
        {
            if (pair.Item1 is not { } symbol) continue;
            var d = symbol.Symbol.Detection;
            for (int i = 0; i < 2; i++) ((TextBlock)energyValues.Children[pair.Item2 + i]).Text = $"{CarrierEnergyMap.Frequencies[pair.Item2 + i]:0} Hz · símbolo {symbol.Number}\n{(i == 0 ? d.Energy0 : d.Energy1):G4}";
        }
        ToolTip = string.Join("\n", new[] { a, b }.Where(x => x is not null).Select(x => $"{(x!.ChannelB ? "B" : "A")} símbolo {x.Number} · intervalo [{x.Symbol.StartSample / (double)rate:0.000}, {x.Symbol.EndSample / (double)rate:0.000}] s · analizado [{x.Symbol.AnalysisStartSample / (double)rate:0.000}, {(x.Symbol.AnalysisStartSample + x.Symbol.AnalysisSampleCount) / (double)rate:0.000}] s · {x.Symbol.Detection.Reason}"));
    }

    private sealed class OverviewNavigator : FrameworkElement
    {
        private readonly SignalMonitorView owner; private double? anchor; private double initial;
        public OverviewNavigator(SignalMonitorView owner)
        {
            this.owner = owner; ToolTip = "Navegador completo: clic para centrar, arrastre la ventana para desplazar";
            MouseLeftButtonDown += (_, e) => { if (owner.map is null) return; owner.SuspendFollowing(); double t = Time(e.GetPosition(this).X); if (t < owner.start || t > owner.end) owner.CenterAt(t); anchor = t; initial = owner.start; CaptureMouse(); e.Handled = true; };
            MouseMove += (_, e) => { if (anchor is not double a || owner.map is null) return; double width = owner.end - owner.start; double s = Math.Clamp(initial + Time(e.GetPosition(this).X) - a, 0, owner.map.Signal.Duration - width); owner.CenterAt(s + width / 2); };
            MouseLeftButtonUp += (_, _) => { anchor = null; ReleaseMouseCapture(); }; LostMouseCapture += (_, _) => anchor = null;
        }
        private double Time(double x) => Math.Clamp(x / Math.Max(1, ActualWidth), 0, 1) * (owner.map?.Signal.Duration ?? 0);
        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(12, 24, 40)), null, new Rect(RenderSize));
            if (owner.map is not {} data) return;
            double peak = data.Peak > 0 ? data.Peak * 1.1 : 1, height = Math.Max(1, ActualHeight - 17);
            for (int i = 0; i < data.Overview.Count; i++) { var b = data.Overview[i]; double x = data.OverviewSamplesPerBucket > 0 ? Math.Min(ActualWidth, (i + .5) * data.OverviewSamplesPerBucket / data.Signal.Samples.Length * ActualWidth) : (i + .5) * ActualWidth / data.Overview.Count; dc.DrawLine(new Pen(Brushes.SlateGray, 1), new(x, height / 2 * (1 - b.Maximum / peak)), new(x, height / 2 * (1 - b.Minimum / peak))); }
            double left = owner.start / data.Signal.Duration * ActualWidth, width = (owner.end - owner.start) / data.Signal.Duration * ActualWidth;
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(65, 105, 183, 255)), new Pen(Brushes.LightSkyBlue, 1.5), new Rect(left, 0, Math.Max(1, width), height));
            dc.DrawText(new FormattedText($"0 s · señal completa · {data.Signal.Duration:0.000} s", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, Brushes.LightGray, VisualTreeHelper.GetDpi(this).PixelsPerDip), new Point(2, height));
        }
    }
    private sealed class SignalMonitorPlot : FrameworkElement
    {
        private readonly SignalMonitorView owner;
        private readonly bool energy;
        private double? anchor;
        private double panX, panStart; private bool panning;
        private DrawingGroup? energyDrawing;
        private (CarrierEnergyMap Map, double Start, double End, double Width, double Height)? drawingKey;
        public WaveformBucket[] Buckets { get; set; } = [];
        private const double Left = 88, Top = 24;
        public void ClearCache() { energyDrawing = null; drawingKey = null; }
        public SignalMonitorPlot(SignalMonitorView owner, bool energy)
        {
            this.owner = owner; this.energy = energy; MinHeight = 100; Focusable = true;
            ToolTip = "Izquierdo: seleccionar · central o Mayús+arrastre: desplazar · Ctrl+rueda: zoom · Mayús+rueda: desplazar · doble clic: centrar";
            MouseDown += (_, e) =>
            {
                if (owner.map is null) return;
                if (e.ChangedButton == MouseButton.Middle || (e.ChangedButton == MouseButton.Left && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)))
                { owner.SuspendFollowing(); panning = true; panX = e.GetPosition(this).X; panStart = owner.start; CaptureMouse(); Cursor = Cursors.ScrollWE; owner.mode.Text = "Desplazar"; e.Handled = true; }
                else if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2) { owner.SuspendFollowing(); owner.CenterAt(Time(e.GetPosition(this).X)); e.Handled = true; }
                else if (e.ChangedButton == MouseButton.Left) { owner.SuspendFollowing(); owner.mode.Text = "Seleccionar intervalo"; Focus(); anchor = Time(e.GetPosition(this).X); CaptureMouse(); owner.SetCursor(anchor.Value); e.Handled = true; }
            };
            MouseWheel += (_, e) =>
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { owner.ZoomAt(Time(e.GetPosition(this).X), e.Delta > 0 ? .75 : 1.0 / .75); owner.mode.Text = "Ampliar · Ctrl+rueda"; e.Handled = true; }
                else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) { owner.Pan(-e.Delta / 120.0 * (owner.end - owner.start) / 5); e.Handled = true; }
            };
            MouseUp += (_, _) => { if (panning) { panning = false; ReleaseMouseCapture(); Cursor = Cursors.Arrow; } };

            MouseMove += (_, e) =>
            {
                if (panning && owner.map is not null) { double width = owner.end - owner.start; double a0 = Math.Clamp(panStart - (e.GetPosition(this).X - panX) / Math.Max(1, ActualWidth - Left - 14) * width, 0, owner.map.Signal.Duration - width); owner.CenterAt(a0 + width / 2); return; }
                if (anchor is not double a || owner.map is null) return;
                double b = Time(e.GetPosition(this).X); owner.SetCursor(b);
                if (Math.Abs(b - a) >= 1.0 / owner.map.Signal.SampleRate) owner.SelectInterval(Math.Min(a, b), Math.Max(a, b));
            };
            MouseLeftButtonUp += (_, _) => { anchor = null; ReleaseMouseCapture(); };
            LostMouseCapture += (_, _) => { anchor = null; panning = false; Cursor = Cursors.Arrow; };
            KeyDown += (_, e) =>
            {
                if (e.Key is Key.Left or Key.Right && owner.map is not null)
                { owner.SetCursor(owner.cursor + (e.Key == Key.Right ? 1 : -1) * (owner.end - owner.start) / 100); e.Handled = true; }
            };
        }
        private double Time(double x) => owner.start + Math.Clamp((x - Left) / Math.Max(1, ActualWidth - Left - 14), 0, 1) * (owner.end - owner.start);
        protected override void OnRender(DrawingContext dc)
        {
            double w = Math.Max(1, ActualWidth - Left - 14), h = Math.Max(1, ActualHeight - Top - 28);
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(11, 21, 37)), null, new Rect(RenderSize));
            void Label(string value, double x, double y, Brush? brush = null) => dc.DrawText(new FormattedText(value,
                CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, brush ?? Brushes.LightGray,
                VisualTreeHelper.GetDpi(this).PixelsPerDip), new Point(x, y));
            Label(energy ? owner.SingleChannel ? "Energía BFSK · 1200 / 1900 Hz" : "Energía de cuatro portadoras · A azul / B naranja" : "Amplitud digital · escala solo visual, muestras sin modificar", Left, 2);
            if (owner.map is not { } map) { Label("Sin señal disponible", Left, Top + 20); return; }
            if (energy && map.Windows.Count == 0) Label("Capturando: mapa de energía disponible al finalizar", Left, Top + 8);
            Label($"{owner.start:0.######} s", Left, Top + h + 6);
            Label($"{owner.end:0.######} s", Math.Max(Left, ActualWidth - 108), Top + h + 6);
            for (int tick = 1; tick < 4; tick++) Label($"{owner.start + (owner.end - owner.start) * tick / 4:0.####} s", Left + w * tick / 4 - 15, Top + h + 6);
            double X(double t) => Left + (t - owner.start) / (owner.end - owner.start) * w;
            dc.PushClip(new RectangleGeometry(new Rect(Left, Top, w, h)));
            if (energy && map.Windows.Count > 0)
            {
                var key = (map, owner.start, owner.end, w, h);
                if (drawingKey != key)
                {
                energyDrawing = new DrawingGroup();
                using var paint = energyDrawing.Open();
                // Como máximo una celda por píxel y portadora; conservar el máximo si se agregan ventanas.
                int columns = Math.Max(1, Math.Min(1600, (int)w));
                var cells = new double[4, columns];
                for (int frameIndex = 0; frameIndex < map.Windows.Count; frameIndex++)
                {
                    var frame = map.Windows[frameIndex];
                    double center = (frame.StartSample + frame.SampleCount / 2.0) / map.Signal.SampleRate;
                    double Center(int i) => (map.Windows[i].StartSample + map.Windows[i].SampleCount / 2.0) / map.Signal.SampleRate;
                    // Celdas hasta el punto medio entre centros; el cursor informa siempre el soporte real de la ventana.
                    double a = Math.Max(owner.start, frameIndex == 0 ? 0 : (Center(frameIndex - 1) + center) / 2);
                    double b = Math.Min(owner.end, frameIndex == map.Windows.Count - 1 ? map.Signal.Duration : (center + Center(frameIndex + 1)) / 2);
                    if (b <= a) continue;
                    int first = Math.Clamp((int)((a - owner.start) / (owner.end - owner.start) * columns), 0, columns - 1);
                    int last = Math.Clamp((int)Math.Ceiling((b - owner.start) / (owner.end - owner.start) * columns), first + 1, columns);
                    for (int i = first; i < last; i++) for (int row = 0; row < 4; row++) cells[row, i] = Math.Max(cells[row, i], frame.Energy(row));
                }
                int rows = owner.SingleChannel ? 2 : 4;
                for (int row = 0; row < rows; row++) for (int i = 0; i < columns; i++)
                {
                    double level = Math.Clamp((CarrierEnergyMap.Decibels(cells[row, i]) + 100) / 100, 0, 1);
                    var color = row < 2 ? Color.FromRgb(105, 183, 255) : Color.FromRgb(255, 179, 107);
                    var brush = new SolidColorBrush(Color.FromArgb((byte)(level * 255), color.R, color.G, color.B));
                    paint.DrawRectangle(brush, null, new Rect(Left + i * w / columns, Top + row * h / rows + 1, w / columns + .1, Math.Max(1, h / rows - 2)));
                }
                drawingKey = key;
                }
                dc.DrawDrawing(energyDrawing);
            }
            else if (!energy)
            {
                double limit = owner.scale.SelectedIndex == 0 && map.Peak > 0 ? map.Peak * 1.1 : 1;
                double Y(double value) => Top + h / 2 * (1 - value / limit);
                var pen = new Pen(Brushes.LightSkyBlue, 1);
                dc.DrawLine(new Pen(Brushes.SlateGray, .5), new(Left, Y(0)), new(Left + w, Y(0)));
                var range = CarrierEnergyMap.Interval(owner.start, owner.end, map.Signal.SampleRate, map.Signal.Samples.Length);
                if (range.Count <= 1600)
                {
                    Point? previous = null;
                    for (int i = range.Start; i < range.End; i++)
                    {
                        var point = new Point(X(i / (double)map.Signal.SampleRate), Y(owner.DisplaySamples.Span[i]));
                        if (previous.HasValue) dc.DrawLine(pen, previous.Value, point);
                        dc.DrawEllipse(Brushes.LightSkyBlue, null, point, 1.5, 1.5); previous = point;
                    }
                }
                else for (int i = 0; i < Buckets.Length; i++)
                {
                    double x = Left + (i + .5) * w / Buckets.Length;
                    dc.DrawLine(pen, new(x, Y(Buckets[i].Maximum)), new(x, Y(Buckets[i].Minimum)));
                }
            }
            double s = Math.Clamp(X(owner.selectedStart), Left, Left + w), e = Math.Clamp(X(owner.selectedEnd), Left, Left + w);
            if (e > s) dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(32, 220, 235, 255)), new Pen(Brushes.SlateGray, .5), new Rect(s, Top, e - s, h));
            if (owner.cursor >= owner.start && owner.cursor <= owner.end) dc.DrawLine(new Pen(Brushes.White, 1), new(X(owner.cursor), Top), new(X(owner.cursor), Top + h));
            dc.Pop();
            if (energy) for (int i = 0; i < (owner.SingleChannel ? 2 : 4); i++) Label($"{(i < 2 ? "A" : "B")} {CarrierEnergyMap.Frequencies[i]:0} Hz", 2, Top + (i + .5) * h / (owner.SingleChannel ? 2 : 4) - 8, i < 2 ? Brushes.LightSkyBlue : Brushes.SandyBrown);
            else
            {
                double limit = owner.scale.SelectedIndex == 0 && map.Peak > 0 ? map.Peak * 1.1 : 1;
                Label($"+{limit:G3}", 2, Top); Label("0", 20, Top + h / 2 - 8); Label($"−{limit:G3}", 2, Top + h - 16);
            }
        }
    }
}
