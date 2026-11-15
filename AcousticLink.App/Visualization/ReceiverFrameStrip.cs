using System.Globalization;
using System.Windows;
using System.Windows.Media;
using AcousticLink.App.Protocol;

namespace AcousticLink.App.Visualization;

public sealed class ReceiverFrameStrip : FrameworkElement
{
    private bool singleChannel;
    public bool SingleChannel { get => singleChannel; set { singleChannel = value; Height = value ? 30 : 58; InvalidateVisual(); } }
    public FdmTextReceiveResult? Reception { get; set; }
    private CarrierEnergyMap? data;
    private double cursor;
    public ReceiverFrameStrip() { Height = 58; }
    public void Update(CarrierEnergyMap? signal, double time) { data = signal; cursor = time; InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc)
    {
        void Text(string text, double x, double y, Brush? brush = null) => dc.DrawText(new FormattedText(text,
            CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, brush ?? Brushes.LightGray,
            VisualTreeHelper.GetDpi(this).PixelsPerDip) { MaxTextWidth = Math.Max(1, ActualWidth - x - 4) }, new(x, y));
        Brush[] colors = [Brushes.SlateGray, Brushes.SteelBlue, Brushes.MediumPurple, Brushes.DodgerBlue, Brushes.SeaGreen, Brushes.DimGray];
        var fieldsA = ReceptionTimeline.Fields(Reception, false); var fieldsB = ReceptionTimeline.Fields(Reception, true);
        double total = Math.Max(data?.Signal.Duration ?? 0, fieldsA.Concat(fieldsB).Select(f => f.End).DefaultIfEmpty(0).Max());
        for (int row = 0; row < (SingleChannel ? 1 : 2); row++)
        {
            var fields = row == 0 ? fieldsA : fieldsB; double y = row * 28;
            var channel = row == 0 ? Reception?.ChannelA : Reception?.ChannelB;
            Text($"{(row == 0 ? "A" : "B")} CRC {(channel?.CrcValid == true ? "OK" : channel?.CrcValid == false ? "falla" : "—")}", 0, y + 4, row == 0 ? Brushes.LightSkyBlue : Brushes.SandyBrown);
            if (data is null || fields.Count == 0) { Text("Cabecera no localizada · campos no disponibles", 85, y + 4); continue; }
            double width = Math.Max(1, ActualWidth - 87);
            foreach (var (field, index) in fields.Select((f, i) => (f, i)))
            {
                double x = 85 + field.Start / total * width, w = (field.End - field.Start) / total * width;
                dc.PushOpacity(field.End <= data.Signal.Duration ? .85 : .35);
                dc.DrawRectangle(colors[Math.Min(index, colors.Length - 1)], new Pen(Brushes.LightGray, .5), new Rect(x, y, w, 24));
                dc.PushClip(new RectangleGeometry(new Rect(x, y, w, 24))); Text(field.Name + (field.Encoded ? " · H74" : ""), x + 3, y + 4, Brushes.White); dc.Pop(); dc.Pop();
            }
            double position = 85 + Math.Clamp(cursor / total, 0, 1) * width;
            dc.DrawLine(new Pen(Brushes.White, 1.5), new(position, y), new(position, y + 24));
        }
        ToolTip = "Colores = campos, no validez. Campos ubicados por el receptor; atenuado = aún no observado. Datos/CRC dependen de la longitud leída. Relleno esperado no equivale a relleno verificado. No se usa la trama conocida del Emisor.";
    }
}
