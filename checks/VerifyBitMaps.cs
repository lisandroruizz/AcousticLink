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
using AcousticLink.App.Signals;
using AcousticLink.App.Visualization;

string observed = "011?", expected = "00101";
var bits = BitMapView.Describe(observed, expected);
Require(bits[0] is { Observed: '0', State: DisplayBitState.Correct } && bits[1] is { Observed: '1', State: DisplayBitState.Incorrect } &&
    bits[2] is { Observed: '1', State: DisplayBitState.Correct } && bits[3].State == DisplayBitState.Inconclusive &&
    bits[4] is { Observed: null, Expected: '1', State: DisplayBitState.Missing }, "Valores y estados por posición");
var comparison = FdmExperimentAnalyzer.Compare(expected, observed);
Require(bits.Count(b => b.State == DisplayBitState.Incorrect) == comparison.IncorrectBits &&
    bits.Count(b => b.State == DisplayBitState.Inconclusive) == comparison.InconclusiveBits &&
    bits.Count(b => b.State == DisplayBitState.Missing) == comparison.MissingBits, "Mismos contadores del analizador/CSV");
Require(BitMapView.Describe(observed).All(b => b.Expected is null && b.State is DisplayBitState.Observed or DisplayBitState.Inconclusive), "El receptor no recibe referencias experimentales");
Require(observed == "011?" && expected == "00101", "Cadenas originales intactas");

Exception? failure = null;
var thread = new Thread(() =>
{
    try
    {
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown }; app.InitializeComponent();
        var map = new BitMapView(); map.ShowComparison(comparison, "A", true, false);
        var buttons = Children<Button>(map).ToArray();
        Require(buttons.Length == 5, "Incluye posiciones faltantes de la referencia");
        buttons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(Texts(map).Any(t => t.Contains("Canal A · bit 2") && t.Contains("esperado: 0") && t.Contains("Incorrecto")), "Inspección de índice, canal y referencia");
        var codec = new Hamming74Codec(); string encoded = codec.Encode("1011");
        var before = FdmExperimentAnalyzer.Compare(encoded, encoded);
        map.ShowComparison(before, "A", true, false);
        Require(Children<Button>(map).Count() == 7 && Texts(map).Contains("Palabra 1"), "Agrupación de siete recibidos");
        Children<Button>(map).Last().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(Texts(map).Any(t => t.Contains("posición 7 de 7")), "Posición dentro de palabra codificada");
        map.ShowComparison(FdmExperimentAnalyzer.Compare("1011", "1011"), "B", true, true);
        Require(Children<Button>(map).Count() == 4 && Texts(map).Any(t => t.Contains("no decisiones directas")), "Después distingue información reconstruida");
        Children<Button>(map).First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(Texts(map).Any(t => t.Contains("Canal B") && t.Contains("D1 de 4") && t.Contains("posición 3 del código de 7")), "Correspondencia de datos, no palabras falsas de siete");
        map.ShowObserved("01?", false, "B");
        Require(!Texts(map).Any(t => t.StartsWith("Palabra")) && Children<Button>(map).All(b => ((DisplayBit)b.Tag).Expected is null), "Sin Hamming y sin comparación en receptor");
        map.ShowComparison(FdmExperimentAnalyzer.Compare(new string('0', 182), new string('0', 180) + "??"), "A", true, false);
        Require(Children<Button>(map).Count() == 182 && Texts(map).Contains("Palabra 26"), "Bloque máximo protegido, sin recortar mapa");

        var reception = new ReceptionView();
        var sync = new FdmSynchronizationResult(FdmSynchronizationState.Failed, null, null, null, null, "Sin cabecera");
        reception.Show(new(FdmTextReceiveState.HeaderNotSynchronized, false, null, null, null, null, "Sin cabecera"), sync, "A");
        Require(!Children<BitMapView>(reception).Any() && Texts(reception).Contains("Mensaje no recuperado") &&
            reception.DiagnosticText.Contains("CRC: NO COMPROBADO"), "Sin sincronía no inventa bits ni CRC");
        var actual = new BfskSequenceResult(Array.AsReadOnly(new[]
        {
            new BfskSymbolResult(0, 0, 4800, 960, 2880, new BfskDetectionResult(1, 0, 0, "")),
            new BfskSymbolResult(1, 4800, 9600, 5760, 2880, new BfskDetectionResult(0, 0, null, ""))
        }));
        reception.Show(new(FdmTextReceiveState.InconclusiveBits, true, 1, null, null, actual, "No concluyente"), sync, "B");
        Require(!Children<BitMapView>(reception).Any(), "El mapa extenso vive en el explorador compartido, no en cada tarjeta");
        var inspector = new ReceptionInspectorView { Channel = "B", Result = reception.Result };
        Require(Texts(inspector).Contains("No comprobado") && !Texts(inspector).Contains("Mensaje válido"), "Inspector distingue CRC no comprobado y recepción incompleta");
        foreach (bool? crc in new bool?[] { true, false, null })
        {
            inspector.Result = reception.Result! with { State = crc is true ? FdmTextReceiveState.Valid : crc is false ? FdmTextReceiveState.CrcIncorrect : FdmTextReceiveState.InconclusiveBits,
                CrcValid = crc, Text = crc is true ? "ñ" : null };
            Require(Texts(inspector).Contains(ReceptionInspectorView.CrcLabel(crc)), "Tres estados CRC sin inventar datos");
        }
        inspector.Result = null;
        Require(Texts(inspector).Contains("Sin analizar"), "Inspector vacío ofrece una siguiente acción");
        reception.Clear(); Require(!Children<BitMapView>(reception).Any() && reception.Result is null && reception.SummaryText == "Sin analizar", "Invalidación elimina bits y proyecciones visuales");
        app.Shutdown();
    }
    catch (Exception ex) { failure = ex; }
});
thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
if (failure is not null) throw new InvalidOperationException("Mapa de bits", failure);
Console.WriteLine("Mapas: 0/1, error, '?', faltante, referencia aislada, grupos, Antes/Después, selección, alcance y estados del receptor: OK");
static IEnumerable<string> Texts(DependencyObject root) => Children<TextBlock>(root).Select(t => t.Text);
static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject
{
    foreach (object item in LogicalTreeHelper.GetChildren(root)) if (item is DependencyObject child)
    {
        if (child is T match) yield return match;
        foreach (var nested in Children<T>(child)) yield return nested;
    }
}
static void Require(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
