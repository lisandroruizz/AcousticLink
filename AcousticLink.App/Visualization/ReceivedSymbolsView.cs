using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace AcousticLink.App.Visualization;

public sealed class ReceivedSymbolsView : FrameworkElement
{
    private IReadOnlyList<ReceivedSymbolTrace> a = [], b = [];
    private int rate;
    private double start, end, cursor;
    public event Action<ReceivedSymbolTrace>? Selected;
    public ReceivedSymbolsView()
    {
        Height = 58;
        MouseLeftButtonDown += (_, e) =>
        {
            if (rate <= 0 || end <= start) return;
            double time = start + Math.Clamp((e.GetPosition(this).X - 88) / Math.Max(1, ActualWidth - 102), 0, 1) * (end - start);
            var selected = ReceivedSymbols.At(e.GetPosition(this).Y < 28 ? a : b, time, rate);
            if (selected is not null) Selected?.Invoke(selected);
        };
    }
    public void Update(IReadOnlyList<ReceivedSymbolTrace> channelA, IReadOnlyList<ReceivedSymbolTrace> channelB, int sampleRate, double from, double to, double time)
    { a = channelA; b = channelB; rate = sampleRate; start = from; end = to; cursor = time; InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc)
    {
        void Text(string text, double x, double y, Brush? brush = null) => dc.DrawText(new FormattedText(text, CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, brush ?? Brushes.LightGray, VisualTreeHelper.GetDpi(this).PixelsPerDip)
            { MaxTextWidth = Math.Max(1, ActualWidth - x - 4) }, new(x, y));
        double width = Math.Max(1, ActualWidth - 102);
        double X(double t) => 88 + (t - start) / (end - start) * width;
        for (int row = 0; row < 2; row++)
        {
            var symbols = row == 0 ? a : b; double y = row * 28;
            Brush color = row == 0 ? Brushes.LightSkyBlue : Brushes.SandyBrown;
            Text(row == 0 ? "Símbolos A" : "Símbolos B", 0, y + 5, color);
            if (symbols.Count == 0 || end <= start) { Text("Sin símbolos sincronizados disponibles", 88, y + 5); continue; }
            dc.PushClip(new RectangleGeometry(new Rect(88, y, width, 26)));
            foreach (var symbol in symbols)
            {
                double left = X(symbol.Symbol.StartSample / (double)rate), right = X(symbol.Symbol.EndSample / (double)rate);
                if (right < 88 || left > 88 + width) continue;
                bool selected = cursor * rate >= symbol.Symbol.StartSample && cursor * rate < symbol.Symbol.EndSample;
                var fill = new SolidColorBrush(symbol.Symbol.Detection.Bit.HasValue ? Color.FromRgb(30, 51, 73) : Color.FromRgb(85, 66, 30));
                dc.DrawRectangle(fill, new Pen(selected ? Brushes.White : color, selected ? 2 : .5), new Rect(left, y + 1, Math.Max(1, right - left), 24));
                if (right - left > 18) { dc.PushClip(new RectangleGeometry(new Rect(Math.Max(88, left), y, Math.Max(1, right - Math.Max(88, left)), 25))); Text(right - left > 55 ? $"{symbol.Number}: {symbol.Value}" : symbol.Value, left + 4, y + 5, symbol.Symbol.Detection.Bit.HasValue ? color : Brushes.Khaki); dc.Pop(); }
            }
            dc.Pop();
        }
        ToolTip = "Decisiones reales recibidas antes de Hamming/CRC, símbolos de 0,1 s; análisis central 20–80 %. Clic para seleccionar el intervalo. El mapa de energía usa ventanas distintas de 20 ms.";
    }
}
