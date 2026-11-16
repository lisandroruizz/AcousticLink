using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace AcousticLink.App.Visualization;

public readonly record struct WaveformBucket(float Minimum, float Maximum);

// Solo representación: no transforma ni sustituye el audio seleccionado.
public sealed class WaveformView : FrameworkElement
{
    private WaveformBucket[] buckets = [];
    private double duration;
    private double scale = 1;
    private double displayFraction = 1;
    private bool useAutoScale = true;
    private double? selectedStart, selectedEnd;
    public double PeakAbsolute { get; private set; }
    public double VerticalLimit => scale;
    public bool UseAutoScale
    {
        get => useAutoScale;
        set { useAutoScale = value; UpdateScale(); }
    }
    public WaveformView() { Height = 180; }

    public void SetSelection(double? startSeconds, double? endSeconds)
    {
        selectedStart = startSeconds; selectedEnd = endSeconds;
        InvalidateVisual();
    }

    public static WaveformBucket[] Reduce(ReadOnlySpan<float> samples, int maximumBuckets = 1200)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBuckets);
        int count = Math.Min(samples.Length, maximumBuckets);
        var result = new WaveformBucket[count];
        for (int i = 0; i < count; i++)
        {
            int start = (int)((long)i * samples.Length / count);
            int end = (int)((long)(i + 1) * samples.Length / count);
            float min = float.PositiveInfinity, max = float.NegativeInfinity;
            for (int j = start; j < end; j++)
            {
                if (!float.IsFinite(samples[j])) throw new ArgumentException("Muestras no finitas.");
                min = Math.Min(min, samples[j]); max = Math.Max(max, samples[j]);
            }
            result[i] = new(min, max);
        }
        return result;
    }

    public void SetData(WaveformBucket[] data, double durationSeconds, double filledFraction = 1)
    {
        if (!double.IsFinite(durationSeconds) || durationSeconds < 0 || !double.IsFinite(filledFraction) || filledFraction < 0 || filledFraction > 1)
            throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        buckets = data;
        duration = durationSeconds;
        displayFraction = filledFraction;
        PeakAbsolute = data.Length == 0 ? 0 : data.Max(b => (double)Math.Max(Math.Abs(b.Minimum), Math.Abs(b.Maximum)));
        UpdateScale();
    }

    private void UpdateScale()
    {
        // Pico acumulado en la vista en vivo: no disminuye ante pausas o variaciones pequeñas.
        // El silencio usa ±1; para amplitudes pequeñas se conserva su magnitud real.
        scale = useAutoScale && PeakAbsolute > 0 ? PeakAbsolute * 1.1 : 1;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double left = 88, width = Math.Max(1, ActualWidth - left - 12), top = 12, height = 135;
        void Label(string value, double x, double y) => dc.DrawText(new FormattedText(value,
            CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12,
            Brushes.LightGray, VisualTreeHelper.GetDpi(this).PixelsPerDip), new Point(x, y));
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(11, 21, 37)), null, new Rect(RenderSize));
        if (buckets.Length == 0) { Label("Sin señal disponible", left, 65); return; }
        Label($"+{scale:G3}", 0, top); Label("0", 20, top + height / 2 - 8); Label($"−{scale:G3}", 0, top + height - 16);
        Label("0 s", left, 153); Label($"{duration:0.000} s", Math.Max(left, ActualWidth - 85), 153);
        dc.DrawLine(new Pen(Brushes.SlateGray, 1), new Point(left, top + height / 2), new Point(left + width, top + height / 2));
        if (duration > 0 && selectedStart is double start && selectedEnd is double end && end > start)
        {
            double x0 = left + width * Math.Clamp(start / duration, 0, 1);
            double x1 = left + width * Math.Clamp(end / duration, 0, 1);
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(72, 255, 207, 94)),
                new Pen(Brushes.Goldenrod, 1), new Rect(x0, top, Math.Max(1, x1 - x0), height));
        }
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(105, 183, 255)), 1);
        dc.PushClip(new RectangleGeometry(new Rect(left, top, width, height)));
        for (int i = 0; i < buckets.Length; i++)
        {
            double x = left + (i + 0.5) * width * displayFraction / buckets.Length;
            dc.DrawLine(pen, new Point(x, top + height / 2 * (1 - buckets[i].Maximum / scale)),
                new Point(x, top + height / 2 * (1 - buckets[i].Minimum / scale)));
        }
        dc.Pop();
    }
}
