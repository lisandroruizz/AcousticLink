using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AcousticLink.App.Visualization;

// Observación posterior a la captura/generación. No decide bits ni escribe en el audio.
public sealed class SignalMonitorView : UserControl
{
    private readonly TextBlock source = Label("Sin señal disponible · capture, importe un WAV o prepare una emisión FDM.");
    private readonly TextBlock settings = Label("");
    private readonly TextBlock cursorInfo = Label("");
    private readonly TextBlock energies = Label("");
    private readonly TextBox from = new() { Width = 86, Text = "0" };
    private readonly TextBox to = new() { Width = 86, Text = "0" };
    private readonly ComboBox scale = new() { Width = 175, ItemsSource = new[] { "Ajustar visualización", "Escala original ±1" }, SelectedIndex = 0 };
    private readonly SignalMonitorPlot waveform;
    private readonly SignalMonitorPlot carriers;
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
        foreach (var height in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1.2, GridUnitType.Star), new GridLength(1, GridUnitType.Star), GridLength.Auto })
            root.RowDefinitions.Add(new RowDefinition { Height = height });
        root.Children.Add(source);
        var tools = new WrapPanel { Margin = new Thickness(0, 8, 0, 4) };
        tools.Children.Add(Label("Intervalo (s):")); tools.Children.Add(from); tools.Children.Add(to);
        void Button(string text, Action action)
        {
            var button = new Button { Content = text, Margin = new Thickness(4, 0, 0, 4), Padding = new Thickness(10, 5, 10, 5) };
            button.Click += (_, _) => action(); tools.Children.Add(button);
        }
        Button("Ampliar intervalo", () =>
        {
            if (map is null) return;
            if (double.TryParse(from.Text, out double a) && double.TryParse(to.Text, out double b))
            { try { SelectInterval(a, b); Zoom(a, b); } catch (ArgumentException) { cursorInfo.Text = "Intervalo inválido: use segundos dentro de la señal."; } }
            else cursorInfo.Text = "Ingrese tiempos numéricos en segundos.";
        });
        Button("＋", () => ChangeZoom(0.5)); Button("−", () => ChangeZoom(2));
        Button("Ver completa", () => { if (map is not null) Zoom(0, map.Signal.Duration); });
        scale.Margin = new Thickness(8, 0, 0, 4); tools.Children.Add(scale);
        scale.SelectionChanged += (_, _) => { waveform?.InvalidateVisual(); };
        Grid.SetRow(tools, 1); root.Children.Add(tools);
        waveform = new SignalMonitorPlot(this, false); carriers = new SignalMonitorPlot(this, true);
        Grid.SetRow(waveform, 2); root.Children.Add(waveform);
        Grid.SetRow(carriers, 3); root.Children.Add(carriers);
        var footer = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        footer.Children.Add(cursorInfo); footer.Children.Add(energies); footer.Children.Add(settings);
        Grid.SetRow(footer, 4); root.Children.Add(footer); Content = root;
        Unloaded += (_, _) => reduction?.Cancel();
        Loaded += (_, _) => { if (map is not null) _ = ReduceVisibleAsync(); };
    }
    private static TextBlock Label(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray, FontSize = 12, Margin = new Thickness(0, 2, 8, 2) };
    public void Clear(string message = "Sin señal disponible")
    {
        reduction?.Cancel(); map = null; source.Text = message; cursorInfo.Text = energies.Text = settings.Text = "";
        waveform.Buckets = []; carriers.ClearCache(); waveform.InvalidateVisual(); carriers.InvalidateVisual();
    }
    public void Show(CarrierEnergyMap data)
    {
        map = data; carriers.ClearCache();
        source.Text = $"{data.Signal.OriginLabel}\n{data.Signal.Description} · {data.Signal.SampleRate:N0} Hz · {data.Signal.Duration:0.000} s · pico {data.Peak:G5}";
        settings.Text = $"Ventana rectangular {1000.0 * data.WindowSamples / data.Signal.SampleRate:0.###} ms · paso {1000.0 * data.HopSamples / data.Signal.SampleRate:0.###} ms (última anclada al final). " +
            "Energía Goertzel sin media: 10 log₁₀(P/1), escala de color −100…0 dB digital; cero = −∞. No es dB SPL, espectrograma completo ni decisión de bits.";
        Zoom(0, data.Signal.Duration); SelectInterval(0, Math.Min(data.Signal.Duration, .1)); SetCursor(0);
    }
    public void SelectInterval(double a, double b)
    {
        if (map is null) return;
        _ = CarrierEnergyMap.Interval(a, b, map.Signal.SampleRate, map.Signal.Samples.Length);
        selectedStart = a; selectedEnd = b; from.Text = a.ToString("0.######"); to.Text = b.ToString("0.######");
        SetCursor(cursor);
    }
    public void Zoom(double a, double b)
    {
        if (map is null) return;
        var interval = CarrierEnergyMap.Interval(a, b, map.Signal.SampleRate, map.Signal.Samples.Length);
        start = interval.Start / (double)map.Signal.SampleRate; end = interval.End / (double)map.Signal.SampleRate;
        waveform.Buckets = []; // No mostrar la reducción del intervalo anterior bajo ejes nuevos.
        waveform.InvalidateVisual(); carriers.InvalidateVisual(); _ = ReduceVisibleAsync();
    }
    private void ChangeZoom(double factor)
    {
        if (map is null) return;
        double width = Math.Clamp((end - start) * factor, Math.Min(2.0 / map.Signal.SampleRate, map.Signal.Duration), map.Signal.Duration);
        double a = Math.Clamp(cursor - width / 2, 0, map.Signal.Duration - width); Zoom(a, a + width);
    }
    private async Task ReduceVisibleAsync()
    {
        reduction?.Cancel(); reduction?.Dispose(); reduction = new();
        var token = reduction.Token; var current = map; double a = start, b = end;
        if (current is null) return;
        try
        {
            var interval = CarrierEnergyMap.Interval(a, b, current.Signal.SampleRate, current.Signal.Samples.Length);
            var values = a == 0 && b == current.Signal.Duration ? current.Overview.ToArray() : await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                return WaveformView.Reduce(current.Signal.Samples.Span.Slice(interval.Start, interval.Count), 1600);
            }, token);
            if (token.IsCancellationRequested || !ReferenceEquals(current, map) || a != start || b != end) return;
            waveform.Buckets = values; waveform.InvalidateVisual();
        }
        catch (OperationCanceledException) { }
    }
    public void SetCursor(double seconds)
    {
        if (map is null) return;
        cursor = Math.Clamp(seconds, 0, map.Signal.Duration);
        var frame = map.Windows[map.WindowAt(cursor)];
        energies.Text = string.Join(" · ", Enumerable.Range(0, 4).Select(i => $"{CarrierEnergyMap.Frequencies[i]:0} Hz: {frame.Energy(i):G4} ({CarrierEnergyMap.Decibels(frame.Energy(i)):0.0} dB)"));
        cursorInfo.Text = $"Cursor {cursor:0.000000} s · selección {selectedStart:0.000000}–{selectedEnd:0.000000} s · energía de ventana [{frame.StartSample / (double)map.Signal.SampleRate:0.000}, {(frame.StartSample + frame.SampleCount) / (double)map.Signal.SampleRate:0.000}] s.";
        RefreshSelection();
    }
    private void RefreshSelection() { waveform.InvalidateVisual(); carriers.InvalidateVisual(); }

    private sealed class SignalMonitorPlot : FrameworkElement
    {
        private readonly SignalMonitorView owner;
        private readonly bool energy;
        private double? anchor;
        private DrawingGroup? energyDrawing;
        private (CarrierEnergyMap Map, double Start, double End, double Width, double Height)? drawingKey;
        public WaveformBucket[] Buckets { get; set; } = [];
        private const double Left = 88, Top = 24;
        public void ClearCache() { energyDrawing = null; drawingKey = null; }
        public SignalMonitorPlot(SignalMonitorView owner, bool energy)
        {
            this.owner = owner; this.energy = energy; MinHeight = 100; Focusable = true;
            ToolTip = "Clic: cursor · arrastrar: seleccionar intervalo en ambos gráficos · ampliar con la barra de herramientas.";
            MouseLeftButtonDown += (_, e) => { if (owner.map is null) return; Focus(); anchor = Time(e.GetPosition(this).X); CaptureMouse(); owner.SetCursor(anchor.Value); };
            MouseMove += (_, e) =>
            {
                if (anchor is not double a || owner.map is null) return;
                double b = Time(e.GetPosition(this).X); owner.SetCursor(b);
                if (Math.Abs(b - a) >= 1.0 / owner.map.Signal.SampleRate) owner.SelectInterval(Math.Min(a, b), Math.Max(a, b));
            };
            MouseLeftButtonUp += (_, _) => { anchor = null; ReleaseMouseCapture(); };
            LostMouseCapture += (_, _) => anchor = null;
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
            Label(energy ? "Energía de cuatro portadoras · A azul / B naranja" : "Amplitud digital · escala solo visual, muestras sin modificar", Left, 2);
            if (owner.map is not { } map) { Label("Sin señal disponible", Left, Top + 20); return; }
            Label($"{owner.start:0.######} s", Left, Top + h + 6);
            Label($"{owner.end:0.######} s", Math.Max(Left, ActualWidth - 108), Top + h + 6);
            for (int tick = 1; tick < 4; tick++) Label($"{owner.start + (owner.end - owner.start) * tick / 4:0.####} s", Left + w * tick / 4 - 15, Top + h + 6);
            double X(double t) => Left + (t - owner.start) / (owner.end - owner.start) * w;
            dc.PushClip(new RectangleGeometry(new Rect(Left, Top, w, h)));
            if (energy)
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
                for (int row = 0; row < 4; row++) for (int i = 0; i < columns; i++)
                {
                    double level = Math.Clamp((CarrierEnergyMap.Decibels(cells[row, i]) + 100) / 100, 0, 1);
                    var color = row < 2 ? Color.FromRgb(105, 183, 255) : Color.FromRgb(255, 179, 107);
                    var brush = new SolidColorBrush(Color.FromArgb((byte)(level * 255), color.R, color.G, color.B));
                    paint.DrawRectangle(brush, null, new Rect(Left + i * w / columns, Top + row * h / 4 + 1, w / columns + .1, Math.Max(1, h / 4 - 2)));
                }
                drawingKey = key;
                }
                dc.DrawDrawing(energyDrawing);
            }
            else
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
                        var point = new Point(X(i / (double)map.Signal.SampleRate), Y(map.Signal.Samples.Span[i]));
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
            if (energy) for (int i = 0; i < 4; i++) Label($"{(i < 2 ? "A" : "B")} {CarrierEnergyMap.Frequencies[i]:0} Hz", 2, Top + (i + .5) * h / 4 - 8, i < 2 ? Brushes.LightSkyBlue : Brushes.SandyBrown);
            else
            {
                double limit = owner.scale.SelectedIndex == 0 && map.Peak > 0 ? map.Peak * 1.1 : 1;
                Label($"+{limit:G3}", 2, Top); Label("0", 20, Top + h / 2 - 8); Label($"−{limit:G3}", 2, Top + h - 16);
            }
        }
    }
}
