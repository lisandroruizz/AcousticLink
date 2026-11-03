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
            var distribution = Descendants<BitDistributionView>(view).Single();
            Require(distribution.Bits.Count == comparison.ExpectedCount && distribution.GroupSize == 4, "Todas las posiciones originales sin doble conteo");
            var selector = Descendants<ComboBox>(view).Single();
            selector.SelectedIndex = 0;
            Require(Descendants<BitDistributionView>(view).Count() == 1 && Descendants<BitDistributionView>(view).Single().GroupSize == 7, "Alternar Antes/Después no duplica distribución");
            view.Show(channel, false);
            Require(Descendants<TextBlock>(view).Any(t => t.Text.Contains("no aplicable")), "Modo sin Hamming");
            view.Clear();
            Require(Descendants<TextBlock>(view).Single().Text == "Sin analizar", "Invalidación visual");
        }
        var partial = FdmExperimentAnalyzer.Compare("0000", "0?1");
        var metrics = new MetricsView();
        metrics.Show(new("A", "a", "0000", "0000", partial, null, null, reception, ""), false);
        var map = Descendants<BitDistributionView>(metrics).Single();
        Require(map.Bits.Select(b => b.State).SequenceEqual(new[] { DisplayBitState.Correct, DisplayBitState.Inconclusive, DisplayBitState.Incorrect, DisplayBitState.Missing }),
            "Correctos, errores, interrogantes y faltantes usan su denominador real");
        var incomplete = reception with { State = FdmTextReceiveState.InconclusiveBits, CrcValid = null, Text = null };
        metrics.Show(new("A", "a", "0000", "0000", FdmExperimentAnalyzer.Compare("0000", "00??"), null, null, incomplete, ""), false);
        Require(Descendants<TextBlock>(metrics).Any(t => t.Text == "Mensaje incompleto") &&
            !Descendants<TextBlock>(metrics).Any(t => t.Text == "Mensaje válido"), "BER cero y cobertura parcial no validan mensaje");
        var longComparison = FdmExperimentAnalyzer.Compare(new string('0', 140), "1?" + new string('0', 135));
        map.Show(longComparison, "B", true, false);
        Require(map.Bits.Count == 140 && map.GroupSize == 7 && Descendants<ListBox>(map).Single().Items.Count == 20, "Vista general completa, límites de siete símbolos");
        var groupList = Descendants<ListBox>(map).Single(); groupList.SelectedIndex = 19;
        Require(map.SelectedGroup == 19 && map.SelectedBit?.Index == 133 && Descendants<Button>(map).Count() == 7, "Amplía solo la palabra elegida");
        var selectedButton = Descendants<Button>(map).Last(); selectedButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(map.SelectedBit is { Index: 139, State: DisplayBitState.Missing }, "Inspector de bit faltante al final");
        var codec = new Hamming74Codec(); var codeword = codec.Encode("1011");
        var decoded = codec.Decode(codeword[..2] + "?" + codeword[3..], true);
        map.Show(FdmExperimentAnalyzer.Compare("1011", decoded.DataBits), "A", true, true, decoded.Traces);
        Require(Descendants<TextBlock>(map).Any(t => t.Text.Contains("◆")) && map.Bits.All(b => b.State == DisplayBitState.Correct), "Marca recuperación solo en reconstruidos, sin cambiar calidad ni denominadores");
        map.Show(FdmExperimentAnalyzer.Compare(codeword, codeword[..2] + "?" + codeword[3..]), "A", true, false, decoded.Traces);
        Require(!Descendants<TextBlock>(map).Any(t => t.Text.Contains("◆") && t.FontSize == 22) && map.Bits[2].State == DisplayBitState.Inconclusive, "Antes conserva el borrado auténtico");
        var diagnostic = new ExperimentDiagnosticView();
        var incompleteComparison = FdmExperimentAnalyzer.Compare("00000000", "01?0");
        var receiveState = reception with { State = FdmTextReceiveState.InconclusiveBits, Text = null, CrcValid = null };
        var testChannel = new FdmChannelExperiment("A", "referencia", "00000000", "00000000", incompleteComparison, null, null, receiveState, "Comparación de prueba");
        var synchronization = new AcousticLink.App.Signals.FdmSynchronizationResult(AcousticLink.App.Signals.FdmSynchronizationState.Failed, null, null, null, null, "Estado de prueba");
        var fixture = new FdmExperimentResult(DateTimeOffset.UnixEpoch, "Prueba de presentación", 48000, 1, false, synchronization, testChannel, testChannel with { Channel = "B" });
        diagnostic.Show(fixture, 48000);
        Require(Descendants<TextBlock>(diagnostic).Any(t => t.Text == "No comprobado") && Descendants<TextBlock>(diagnostic).Any(t => t.Text == "Mensaje incompleto"), "Diagnóstico no valida un mensaje incompleto");
        var strings = Descendants<TextBox>(diagnostic).Select(t => t.Text).ToArray();
        Require(strings.Contains("00000000") && strings.Contains("01?0"), "Referencia y observado diferentes, sin completar símbolos faltantes");
        Require(!Descendants<TextBlock>(diagnostic).Any(t => t.Text == "Referencia codificada" || t.Text == "Reconstruido · Hamming"), "Sin Hamming no se atribuyen palabras ni reconstrucciones");
        diagnostic.Show(fixture with { ChannelA = testChannel with { Reception = receiveState with { State = FdmTextReceiveState.CrcIncorrect, CrcValid = false } } });
        Require(Descendants<TextBlock>(diagnostic).Any(t => t.Text == "Inválido"), "Diagnóstico CRC inválido");
        diagnostic.Show(fixture with { ChannelA = testChannel with { Before = FdmExperimentAnalyzer.Compare("00000000", "????") } });
        Require(Descendants<TextBlock>(diagnostic).Any(t => t.Text == "No definida"), "BER indefinida no se convierte en cero");
        diagnostic.Show(fixture with { ChannelA = testChannel with { Before = null } });
        Require(!Descendants<TextBox>(diagnostic).Any(), "Sin alineación no se inventan fragmentos");
        diagnostic.Clear(); Require(Descendants<ComboBox>(diagnostic).All(c => !c.IsEnabled), "Invalidación del inspector técnico");
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
