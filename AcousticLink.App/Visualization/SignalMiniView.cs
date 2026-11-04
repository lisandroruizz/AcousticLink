using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace AcousticLink.App.Visualization;

// Dos escalas de una misma señal: muestras originales en detalle y resumen global compartido.
public sealed class SignalMiniView : FrameworkElement
{
    private CarrierEnergyMap? data, global;
    private string caption = "Sin señal · capture, importe WAV o transmita";
    private int first, last;
    public double StartSeconds { get; private set; }
    public double EndSeconds { get; private set; }
    public double CursorSeconds { get; private set; }
    public double DetailStartSeconds => data is null ? 0 : first / (double)data.Signal.SampleRate;
    public double DetailEndSeconds => data is null ? 0 : last / (double)data.Signal.SampleRate;
    public double TotalSeconds => global?.Signal.Duration ?? 0;
    public double VerticalLimit { get; private set; } = 1;
    public int DetailSampleCount => last - first;
    public bool IsLive { get; private set; }
    public MonitorSignalOrigin? Origin { get; private set; }
    public SignalMiniView() { Height = 212; }
    public void SetWaiting(MonitorSignalOrigin origin)
    { Origin = origin; IsLive = true; data = global = null; first = last = 0; caption = origin == MonitorSignalOrigin.Generated ? "Salida digital · esperando posición" : "Micrófono · esperando muestras"; InvalidateVisual(); }
    public void Update(CarrierEnergyMap? current, WaveformBucket[] values, double start, double end, bool live,
        double? position = null, CarrierEnergyMap? complete = null)
    {
        // values ya pertenece a la ventana del monitor grande. El global usa su resumen completo,
        // y el detalle lee como máximo 10 ms del buffer compuesto, nunca contribuciones separadas.
        data = current; global = complete ?? current;
        StartSeconds = start; EndSeconds = end; IsLive = live; Origin = current?.Signal.Origin;
        CursorSeconds = current is null ? 0 : Math.Clamp(position ?? (live ? current.Signal.Duration : start), 0, current.Signal.Duration);
        first = last = 0; VerticalLimit = 1;
        if (current is not null)
        {
            int count = current.Signal.Samples.Length, rate = current.Signal.SampleRate;
            int width = Math.Min(count, Math.Max(1, (int)Math.Round(rate * .01)));
            int at = Math.Clamp((int)Math.Round(CursorSeconds * rate), 0, count);
            first = Math.Clamp(at - width, 0, count - width); last = first + width;
            double peak = 0;
            foreach (float value in current.Signal.Samples.Span.Slice(first, width)) peak = Math.Max(peak, Math.Abs((double)value));
            VerticalLimit = peak > 0 ? peak * 1.1 : 1;
        }
        caption = current is null ? "Sin señal disponible" : $"{(Origin == MonitorSignalOrigin.Generated ? "Digital A+B · no es micrófono" : Origin == MonitorSignalOrigin.Microphone ? "Micrófono" : "WAV · estático")} · {(live ? "en vivo" : "detenido")}";
        InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(11, 21, 37)), null, new Rect(RenderSize));
        void Label(string text, double x, double y, double size = 11) => dc.DrawText(new FormattedText(text, CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, Brushes.LightGray, VisualTreeHelper.GetDpi(this).PixelsPerDip)
            { MaxTextWidth = Math.Max(1, ActualWidth - x - 6) }, new Point(x, y));
        Label(caption, 6, 4, 12);
        if (data is null || global is null || last <= first) return;
        double left = 52, width = Math.Max(1, ActualWidth - left - 10), top = 44, height = 65;
        Label($"Detalle · {DetailStartSeconds:0.0000}–{DetailEndSeconds:0.0000} s", 6, 25);
        Label($"+{VerticalLimit:G3}", 3, top); Label($"−{VerticalLimit:G3}", 3, top + height - 12);
        var pen = new Pen(Origin == MonitorSignalOrigin.Generated ? Brushes.MediumPurple : Brushes.LightSkyBlue, 1.2);
        dc.DrawLine(new Pen(Brushes.SlateGray, .5), new(left, top + height / 2), new(left + width, top + height / 2));
        Point? previous = null;
        for (int i = first; i < last; i++)
        {
            var point = new Point(left + (i - first) / (double)Math.Max(1, last - first - 1) * width,
                top + height / 2 * (1 - data.Signal.Samples.Span[i] / VerticalLimit));
            if (previous.HasValue) dc.DrawLine(pen, previous.Value, point);
            previous = point;
        }
        Label($"Global · posición {CursorSeconds:0.000} / {TotalSeconds:0.000} s", 6, 117);
        double gTop = 138, gHeight = 32, limit = global.Peak > 0 ? global.Peak * 1.1 : 1;
        dc.PushClip(new RectangleGeometry(new Rect(left, gTop, width, gHeight)));
        for (int i = 0; i < global.Overview.Count; i++)
        {
            var bucket = global.Overview[i];
            double fraction = global.OverviewSamplesPerBucket > 0 ? (i + .5) * global.OverviewSamplesPerBucket / global.Signal.Samples.Length : (i + .5) / global.Overview.Count;
            double x = left + fraction * width;
            dc.DrawLine(new Pen(Brushes.SlateGray, 1), new(x, gTop + gHeight / 2 * (1 - bucket.Maximum / limit)), new(x, gTop + gHeight / 2 * (1 - bucket.Minimum / limit)));
        }
        double cursorX = left + Math.Clamp(CursorSeconds / TotalSeconds, 0, 1) * width;
        cursorX = Math.Clamp(cursorX, left + .5, left + width - .5);
        dc.DrawLine(new Pen(Brushes.White, 1.5), new(cursorX, gTop), new(cursorX, gTop + gHeight));
        dc.Pop();
        if (data.Windows.Count > 0)
        {
            var frame = data.Windows[data.WindowAt(CursorSeconds)];
            for (int i = 0; i < 4; i++)
            {
                double x = 6 + i * ActualWidth / 4; Label(new[] { "A0", "A1", "B0", "B1" }[i], x, 194);
                double level = Math.Clamp((CarrierEnergyMap.Decibels(frame.Energy(i)) + 100) / 100, 0, 1);
                dc.DrawRectangle(Brushes.DarkSlateGray, null, new Rect(x + 23, 198, Math.Max(1, ActualWidth / 4 - 34), 5));
                dc.DrawRectangle(i < 2 ? Brushes.LightSkyBlue : Brushes.SandyBrown, null, new Rect(x + 23, 198, Math.Max(1, ActualWidth / 4 - 34) * level, 5));
            }
        }
        ToolTip = "Actividad A0=1200, A1=1900, B0=3000, B1=3700 Hz · escala −100…0 dB digital. Energía no equivale a bit válido.";
        Label("0 s", left, 173); Label($"{TotalSeconds:0.000} s", Math.Max(left, ActualWidth - 70), 173);
    }
}
