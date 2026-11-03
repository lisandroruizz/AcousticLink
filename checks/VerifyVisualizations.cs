#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property PublishAot=false
#:property PublishTrimmed=false
#:project ../AcousticLink.App/AcousticLink.App.csproj

using System.Windows;
using System.Windows.Controls;
using AcousticLink.App;
using AcousticLink.App.Experiments;
using AcousticLink.App.Protocol;
using AcousticLink.App.Visualization;

var samples = new float[48000 * 30];
samples[719999] = -0.9f; samples[720000] = 0.7f;
var buckets = WaveformView.Reduce(samples);
Require(buckets.Length == 1200 && buckets.Min(b => b.Minimum) == -0.9f && buckets.Max(b => b.Maximum) == 0.7f, "Picos preservados en audio de 30 s");
Require(samples[719999] == -0.9f && samples[720000] == 0.7f && samples.Sum(v => (double)v) == (double)-0.9f + 0.7f, "Muestras originales intactas");
Require(WaveformView.Reduce([]).Length == 0 && WaveformView.Reduce([0, 1], 1200).Length == 2, "Audio ausente y corto");
try { WaveformView.Reduce([float.NaN]); throw new Exception("NaN aceptado"); } catch (ArgumentException) { }

Exception? failure = null;
var thread = new Thread(() =>
{
    try
    {
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        var reception = new FdmTextChannelResult(FdmTextReceiveState.Valid, true, 1, true, "a", null, "CRC válido");
        foreach (string observed in new[] { "0000", "0010", "????", "" })
        {
            var comparison = FdmExperimentAnalyzer.Compare("0000", observed);
            var channel = new FdmChannelExperiment("A", "a", "0000", "0000", comparison, comparison, null, reception, "");
            var view = new MetricsView();
            view.Show(channel, true);
            var rows = Descendants<TextBlock>(view).Select(t => t.Text).ToArray();
            var bars = Descendants<ProgressBar>(view).ToArray();
            Require(bars.All(b => b.Minimum == 0 && b.Maximum == 1), "Escala correcta");
            Require(comparison.Ber.HasValue ? bars.Length == 2 && bars[0].Value == comparison.Ber : bars.Length == 1 && rows.Contains("No definida"),
                "BER cero, positiva o no definida sin barra ficticia");
            Require(bars.Last().Value == comparison.Coverage, "Misma cobertura que el resultado experimental");
            var compositions = Descendants<Grid>(view).ToArray();
            Require(compositions.All(g => g.ColumnDefinitions.Sum(c => c.Width.Value) == comparison.ExpectedCount), "Partición sin doble conteo");
            var selector = Descendants<ComboBox>(view).Single();
            selector.SelectedIndex = 0;
            Require(Descendants<Grid>(view).Count() == 1, "Alternar Antes/Después no duplica distribución");
            view.Show(channel, false);
            Require(((StackPanel)view.Content).Children.OfType<TextBlock>().Any(t => t.Text.Contains("no aplicable")), "Modo sin Hamming");
            view.Clear();
            Require(((StackPanel)view.Content).Children.Count == 1 && ((TextBlock)((StackPanel)view.Content).Children[0]).Text == "Sin analizar", "Invalidación visual");
        }
        var partial = FdmExperimentAnalyzer.Compare("0000", "0?1");
        var metrics = new MetricsView();
        metrics.Show(new("A", "a", "0000", "0000", partial, null, null, reception, ""), false);
        Require(Descendants<Grid>(metrics).Single().ColumnDefinitions.All(c => c.Width.Value == 1),
            "Correctos, errores, interrogantes y faltantes usan su denominador real");
        var incomplete = reception with { State = FdmTextReceiveState.InconclusiveBits, CrcValid = null, Text = null };
        metrics.Show(new("A", "a", "0000", "0000", FdmExperimentAnalyzer.Compare("0000", "00??"), null, null, incomplete, ""), false);
        Require(Descendants<TextBlock>(metrics).Any(t => t.Text == "Mensaje incompleto") &&
            !Descendants<TextBlock>(metrics).Any(t => t.Text == "Mensaje válido"), "BER cero y cobertura parcial no validan mensaje");
        var waveform = new WaveformView();
        waveform.SetData(buckets, 30);
        waveform.Measure(new Size(800, 180)); waveform.Arrange(new Rect(0, 0, 800, 180));
        waveform.SetData([], 0);
        app.Shutdown();
    }
    catch (Exception ex) { failure = ex; }
});
thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
if (failure is not null) throw new InvalidOperationException("Visualizaciones", failure);
Console.WriteLine("Forma de onda real acotada, picos, ausencia de señal, BER/cobertura y composición: OK");
static void Require(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
{
    foreach (object item in LogicalTreeHelper.GetChildren(root))
        if (item is DependencyObject child)
        {
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
}
