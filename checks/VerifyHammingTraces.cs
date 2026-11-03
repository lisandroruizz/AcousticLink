#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property PublishAot=false
#:property PublishTrimmed=false
#:project ../AcousticLink.App/AcousticLink.App.csproj

using System.Windows;
using System.Windows.Controls;
using AcousticLink.App;
using AcousticLink.App.Protocol;
using AcousticLink.App.Experiments;
using AcousticLink.App.Signals;
using AcousticLink.App.Visualization;

var codec = new Hamming74Codec();
for (int nibble = 0; nibble < 16; nibble++)
{
    string data = Convert.ToString(nibble, 2).PadLeft(4, '0'), encoded = codec.Encode(data);
    var clean = codec.Decode(encoded).Traces.Single();
    foreach (int parity in new[] { 1, 2, 4 })
        Require(HammingTeaching.Coverage(parity).Aggregate(0, (sum, p) => sum ^ (encoded[p - 1] - '0')) == 0, "Relaciones didácticas de paridad par");
    Require(clean.ReceivedBits == encoded && clean.ResultBits == encoded && clean.DataBits == data && clean.Syndrome == 0, "Traza sin cambios");
    for (int p = 0; p < 7; p++)
    {
        string changed = Flip(encoded, p);
        var flipped = codec.Decode(changed).Traces.Single();
        Require(flipped.ReceivedBits == changed && flipped.ResultBits == encoded && flipped.DataBits == data &&
            flipped.Syndrome == p + 1 && flipped.FlippedPosition == p + 1 && flipped.RecoveredPositions.Count == 0 &&
            flipped.State == Hamming74TraceState.BitFlipped, "Inversión real diferenciada");
        for (int q = p; q < 7; q++)
        {
            string erased = Erase(encoded, p, q);
            var trace = codec.Decode(erased, true).Traces.Single();
            Require(trace.ReceivedBits == erased && trace.ResultBits == encoded && trace.DataBits == data &&
                trace.Syndrome is null && trace.FlippedPosition is null && trace.State == Hamming74TraceState.ErasuresRecovered &&
                trace.RecoveredPositions.SequenceEqual(new[] { p + 1, q + 1 }.Distinct()), "Traza de uno o dos borrados");
        }
    }
    var lost = codec.Decode(Erase(encoded, 0, 1, 2), true).Traces.Single();
    Require(lost.State == Hamming74TraceState.Unrecoverable && lost.ResultBits is null && lost.DataBits is null && lost.Syndrome is null, "Irrecuperable sin palabra ficticia");
}

var builder = new FdmTextFrameBuilder();
var frames = builder.Build("Hola", "Mundo", true);
string a = frames.ChannelA.TransmittedBits, b = frames.ChannelB.TransmittedBits;
var analyzer = new FdmExperimentAnalyzer();
float[] signal = Signal(Erase(a, 38, 39));
var off = analyzer.Analyze(signal, 48000, "Hola", "Mundo", true);
var on = analyzer.Analyze(signal, 48000, "Hola", "Mundo", true, recoverErasures: true);
Require(off.ChannelA.Hamming!.Traces[2].State == Hamming74TraceState.Unrecoverable &&
    on.ChannelA.Hamming!.Traces[2].State == Hamming74TraceState.ErasuresRecovered, "Opciones modifican traza real");
Require(off.ChannelA.Before == on.ChannelA.Before && on.ChannelA.After!.Coverage == 1 && on.ChannelA.Reception.CrcValid == true &&
    on.ChannelA.Reception.Text == "Hola" && on.ChannelB.Reception.Text == "Mundo", "BER previa, cobertura, CRC e independencia");
Require(on.ChannelA.Hamming!.Traces.Count == 18 && on.ChannelB.Hamming!.Traces.Count == 20, "No se incluye relleno FDM");
foreach (var channel in new[] { on.ChannelA, on.ChannelB })
foreach (var trace in channel.Hamming!.Traces)
{
    Require(trace.Channel == channel.Channel && trace.EncodedBitOffset == trace.WordIndex * 7 &&
        trace.ReceivedBits == channel.Before!.ObservedBits.Substring(trace.EncodedBitOffset, 7), "Índices, canal y entrada real");
    Require(trace.DataBits == channel.After!.ObservedBits.Substring(trace.WordIndex * 4, 4), "Salida coincide con métricas existentes");
}
var wrongLengthReference = analyzer.Analyze(signal, 48000, "Mundo", "Mundo", true, recoverErasures: true);
Require(wrongLengthReference.ChannelA.Hamming!.Traces.Count == 18, "Referencia más larga no añade relleno a trazas");
var receiverTraces = on.ChannelA.Reception.HammingDiagnostics!.Traces;
Require(receiverTraces.All(t => t.Channel == "A") && receiverTraces[2].ResultBits == on.ChannelA.Hamming!.Traces[2].ResultBits, "Mismo códec en receptor y análisis");
Require(HammingTeaching.Association(on.ChannelA.Reception, receiverTraces[2]).Contains("0x48"), "Byte validado del contenido real");
Require(!HammingTeaching.Association(on.ChannelA.Reception with { CrcValid = false, State = FdmTextReceiveState.CrcIncorrect }, receiverTraces[2]).Contains("0x48"), "No se inventa byte validado desde la referencia");
var noLength = analyzer.Analyze(Signal(Erase(a, 25)), 48000, "Hola", "Mundo", true);
Require(noLength.ChannelA.Hamming!.Traces.Count == 2 && noLength.ChannelA.Hamming.Traces[0].Syndrome is null, "Longitud no recuperada delimita detalle");
var truncated = analyzer.Analyze(Signal(a)[..(48000 * 5)], 48000, "Hola", "Mundo", true);
Require(truncated.ChannelA.Hamming!.Traces.Last().State == Hamming74TraceState.Incomplete &&
    truncated.ChannelA.Hamming.Traces.Last().ReceivedBits.Length < 7, "Truncación conserva solo símbolos reales");

Exception? failure = null;
var thread = new Thread(() =>
{
    try
    {
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown }; app.InitializeComponent();
        var view = new HammingTraceView(); view.Show(on);
        var channel = Children<ComboBox>(view).Single(c => c.Items.Contains("Canal A"));
        var list = Children<ListBox>(view).Single();
        Require(list.Items.Count == 18 && list.SelectedIndex == 2, "Vista A prioriza borrados recuperados"); list.SelectedIndex = 3;
        channel.SelectedIndex = 1; Require(list.Items.Count == 20, "Vista B independiente");
        channel.SelectedIndex = 0; Require(list.SelectedIndex == 3, "Conserva selección explícita por canal");
        var parityChoice = Children<ComboBox>(view).Single(c => c.Items.Contains("P1: posiciones 1, 3, 5, 7"));
        parityChoice.SelectedIndex = 1;
        Require(Children<TextBlock>(view).Any(t => t.Text.Contains("no ejecutó esta visualización paso a paso")), "Paridad no se presenta como ejecución real");
        view.Measure(new Size(1200, 500)); view.Arrange(new Rect(0, 0, 1200, 500)); view.UpdateLayout();
        var workspace = ((Grid)view.Content).Children.OfType<Grid>().Single();
        Require(Grid.GetColumn(workspace.Children.OfType<ScrollViewer>().Single()) == 1, "Distribución de escritorio en columnas");
        view.Measure(new Size(650, 500)); view.Arrange(new Rect(0, 0, 650, 500)); view.UpdateLayout();
        Require(Grid.GetRow(workspace.Children.OfType<ScrollViewer>().Single()) == 1, "Adaptación estrecha sin cortar detalle");
        view.Show(off); channel.SelectedIndex = 0;
        Require(((ListBoxItem)list.Items[2]).Tag is Hamming74WordTrace { State: Hamming74TraceState.Unrecoverable }, "Reanálisis reemplaza trazas");
        view.Show(noLength); Require(list.Items.Count == 2 && Children<TextBlock>(view).Any(t => t.Text.Contains("solo hay detalles del campo de longitud")), "Explica diferencia de alcance con longitud desconocida");
        view.Show(truncated); Require(((Hamming74WordTrace)((ListBoxItem)list.SelectedItem).Tag).State == Hamming74TraceState.Incomplete &&
            Children<TextBlock>(view).Any(t => t.Text == "No disponible"), "Detalle truncado no inventa reconstrucción");
        view.Show(on with { UsesHamming = false }); Require(list.Items.Count == 0 && !channel.IsEnabled, "Sin Hamming no hay detalle");
        view.Clear(); Require(list.Items.Count == 0, "Invalidación limpia palabras");
        app.Shutdown();
    }
    catch (Exception ex) { failure = ex; }
});
thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
if (failure is not null) throw new InvalidOperationException("Vista Hamming", failure);
Console.WriteLine("Trazas reales: 16 palabras, inversiones, borrados, estados, canal/índices, métricas, CRC, relleno, truncación y selección WPF: OK");

float[] Signal(string changedA)
{
    var audio = new FdmBitstreamGenerator().GenerateTextFrames("Hola", "Mundo", true);
    for (int i = 0; i < a.Length; i++)
    {
        if (a[i] == changedA[i]) continue;
        var symbol = new FdmToneMixer().Generate(changedA[i] == '1', b[i] == '1', 0.1);
        if (changedA[i] == '?')
            for (int j = 0; j < 4800; j++) symbol[j] = (float)(0.075 * Math.Sin(2 * Math.PI * (b[i] == '0' ? 3000 : 3700) * j / 48000));
        symbol.CopyTo(audio, i * 4800);
    }
    var padded = new float[11376 + audio.Length + 9600]; audio.CopyTo(padded, 11376); return padded;
}
static string Erase(string value, params int[] positions) { var bits = value.ToCharArray(); foreach (int p in positions) bits[p] = '?'; return new(bits); }
static string Flip(string value, int p) => value[..p] + (value[p] == '0' ? "1" : "0") + value[(p + 1)..];
static void Require(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject
{
    foreach (object item in LogicalTreeHelper.GetChildren(root)) if (item is DependencyObject child)
    {
        if (child is T match) yield return match;
        foreach (var nested in Children<T>(child)) yield return nested;
    }
}
