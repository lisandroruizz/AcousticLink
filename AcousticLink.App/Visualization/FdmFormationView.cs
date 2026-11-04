using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace AcousticLink.App.Visualization;

public sealed class FdmFormationView : FrameworkElement
{
    private CarrierEnergyMap? data;
    private double start, end, cursor, timelineTop = double.PositiveInfinity;
    private WaveformBucket[][] curves = [];
    private CancellationTokenSource? preparation;
    public event Action<double>? TimeSelected;
    public FdmFormationView()
    {
        MouseLeftButtonDown += (_, e) =>
        {
            if (data is null) return;
            double fraction = Math.Clamp((e.GetPosition(this).X - 110) / Math.Max(1, ActualWidth - 125), 0, 1);
            double time = e.GetPosition(this).Y >= timelineTop && data.Signal.Transmission is { } info ? Math.Min(data.Signal.Duration, fraction * info.BitsA.Length * info.SymbolSeconds) : start + fraction * (end - start);
            TimeSelected?.Invoke(time); e.Handled = true;
        };
        ToolTip = "Clic: cursor común. Use el navegador y los controles de zoom del monitor. Los colores identifican canales y campos, no recepción válida.";
        Unloaded += (_, _) => preparation?.Cancel();
    }
    public async void Update(CarrierEnergyMap? current, double a, double b, double time)
    {
        bool changed = !ReferenceEquals(data, current) || start != a || end != b;
        data = current; start = a; end = b; cursor = time; InvalidateVisual();
        if (!changed) return;
        preparation?.Cancel(); preparation?.Dispose(); preparation = new(); var token = preparation.Token;
        curves = [];
        if (current is null || current.Signal.Origin != MonitorSignalOrigin.Generated || current.Signal.ChannelA.IsEmpty || current.Signal.ChannelB.IsEmpty) return;
        try
        {
            var span = CarrierEnergyMap.Interval(a, b, current.Signal.SampleRate, current.Signal.Samples.Length);
            var reduced = await Task.Run(() => new[] { current.Signal.ChannelA, current.Signal.ChannelB, current.Signal.Samples }
                .Select(samples => { token.ThrowIfCancellationRequested(); return WaveformView.Reduce(samples.Span.Slice(span.Start, span.Count), 1600); }).ToArray(), token);
            if (token.IsCancellationRequested || !ReferenceEquals(current, data) || a != start || b != end) return;
            curves = reduced; InvalidateVisual();
        }
        catch (OperationCanceledException) { }
    }
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(11, 21, 37)), null, new Rect(RenderSize));
        void Text(string text, double x, double y, Brush? brush = null, double size = 12) => dc.DrawText(new FormattedText(text,
            CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush ?? Brushes.LightGray,
            VisualTreeHelper.GetDpi(this).PixelsPerDip) { MaxTextWidth = Math.Max(1, ActualWidth - x - 6) }, new Point(x, y));
        if (data is null) { Text("Sin señal disponible", 12, 16); return; }
        if (data.Signal.Origin != MonitorSignalOrigin.Generated || data.Signal.ChannelA.IsEmpty || data.Signal.ChannelB.IsEmpty)
        { Text("A y B originales no están disponibles para esta fuente. Consulte la onda recibida y el mapa de energía.", 12, 16); return; }
        var info = data.Signal.Transmission;
        double inspected = Math.Clamp(cursor, start, Math.Max(start, end - 1.0 / data.Signal.SampleRate));
        Text($"Contribuciones digitales verificadas · {start:0.000000}–{end:0.000000} s · cursor {cursor:0.000000} s", 10, 4);
        double left = 110, width = Math.Max(1, ActualWidth - 125), top = 50;
        double rowHeight = Math.Max(36, (ActualHeight - top - 95) / 3);
        double X(double seconds) => left + (seconds - start) / (end - start) * width;
        double limit = Math.Max(data.Peak * 1.1, 1e-30);
        if (data.Peak == 0) limit = 1;
        Text($"Escala común ±{limit:G3} · solo visual", 10, 25);
        Brush[] colors = [Brushes.LightSkyBlue, Brushes.SandyBrown, Brushes.MediumPurple];
        for (int row = 0; row < 3; row++)
        {
            double y = top + row * rowHeight, mid = y + rowHeight / 2;
            Text(row == 0 ? "Canal A" : row == 1 ? "Canal B" : "A+B", 8, y + 2, colors[row], 14);
            if (info is not null && row < 2)
            { var tone = info.Tone(row == 1, inspected); Text($"{tone.Bit} · {tone.Frequency:0} Hz", 8, y + 22, colors[row]); }
            dc.DrawLine(new Pen(Brushes.SlateGray, .5), new(left, mid), new(left + width, mid));
            dc.PushClip(new RectangleGeometry(new Rect(left, y, width, rowHeight)));
            if (curves.Length == 3)
            {
                var values = curves[row]; Point? previous = null;
                bool exact = values.Length == CarrierEnergyMap.Interval(start, end, data.Signal.SampleRate, data.Signal.Samples.Length).Count;
                for (int i = 0; i < values.Length; i++)
                {
                    double x = left + (i + .5) * width / values.Length;
                    double Y(float value) => mid - value / limit * rowHeight * .43;
                    var point = new Point(x, Y(values[i].Maximum));
                    if (exact && previous.HasValue) dc.DrawLine(new Pen(colors[row], 1), previous.Value, point);
                    else dc.DrawLine(new Pen(colors[row], 1), point, new(x, Y(values[i].Minimum)));
                    previous = point;
                }
            }
            if (info is not null)
            {
                int first = (int)Math.Ceiling(start / info.SymbolSeconds);
                for (int symbol = first; symbol * info.SymbolSeconds < end; symbol++)
                    dc.DrawLine(new Pen(Brushes.SlateGray, .7) { DashStyle = DashStyles.Dash }, new(X(symbol * info.SymbolSeconds), y), new(X(symbol * info.SymbolSeconds), y + rowHeight));
            }
            dc.DrawLine(new Pen(Brushes.White, 1), new(X(cursor), y), new(X(cursor), y + rowHeight)); dc.Pop();
        }
        double baseline = top + 3 * rowHeight;
        Text($"{start:0.000000} s", left, baseline); Text($"{end:0.000000} s", Math.Max(left, ActualWidth - 100), baseline);
        if (info is not null) ToolTip = string.Join("\n", new[] { false, true }.SelectMany(b => info.Segments(b).Select(f => $"{(b ? "B" : "A")} · {f.Name}: símbolos {f.FirstSymbol + 1}–{f.EndSymbol} · {f.FirstSymbol * info.SymbolSeconds:0.000}–{f.EndSymbol * info.SymbolSeconds:0.000} s{(f.Protected ? " · codificado Hamming" : " · sin Hamming")}")));
        if (info is null) { Text("Metadatos de símbolos no disponibles.", 10, baseline + 24); return; }
        Text($"Símbolo {info.SymbolAt(inspected) + 1} · {info.SymbolSeconds:0.###} s · tramas completas del Emisor; marco = zoom, clic = explorar", 10, baseline + 19);
        timelineTop = baseline + 39;
        double FullX(double time) => left + time / (info.BitsA.Length * info.SymbolSeconds) * width;
        Brush[] sections = [Brushes.SlateGray, Brushes.SteelBlue, Brushes.MediumPurple, Brushes.DodgerBlue, Brushes.SeaGreen, Brushes.DimGray];
        for (int channel = 0; channel < 2; channel++)
        {
            double y = baseline + 39 + channel * 23; Text(channel == 0 ? "Trama A" : "Trama B", 8, y, colors[channel]);
            var fields = info.Segments(channel == 1);
            if (fields.Count == 0) { Text("Secuencia de prueba · sin campos UTF-8 registrados", left, y); continue; }
            dc.PushClip(new RectangleGeometry(new Rect(left, y, width, 21)));
            for (int i = 0; i < fields.Count; i++)
            {
                var field = fields[i]; double a = Math.Max(left, FullX(field.FirstSymbol * info.SymbolSeconds)), b = Math.Min(left + width, FullX(field.EndSymbol * info.SymbolSeconds));
                if (b <= a) continue;
                dc.DrawRectangle(sections[i], new Pen(Brushes.LightGray, .5), new Rect(a, y, b - a, 21));
                if (b - a > 25) { dc.PushClip(new RectangleGeometry(new Rect(a, y, b - a, 21))); Text(field.Name + (field.Protected ? " · H74" : ""), a + 3, y + 2, Brushes.White, 11); dc.Pop(); }
            }
            dc.DrawRectangle(null, new Pen(Brushes.White, 2), new Rect(FullX(start), y + 1, Math.Max(2, FullX(end) - FullX(start)), 19));
            dc.DrawLine(new Pen(Brushes.White, 1.5), new(FullX(cursor), y), new(FullX(cursor), y + 21)); dc.Pop();
        }
    }
}
