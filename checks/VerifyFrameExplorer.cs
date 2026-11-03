#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property PublishAot=false
#:property PublishTrimmed=false
#:project ../AcousticLink.App/AcousticLink.App.csproj

using System.Windows;
using System.Windows.Controls;
using AcousticLink.App;
using AcousticLink.App.Protocol;
using AcousticLink.App.Signals;
using AcousticLink.App.Visualization;

var codec = new MessageCodec();
foreach (bool hamming in new[] { false, true })
foreach (string text in new[] { "a", "ab", "abc", "Hola", "Mundo", "abcdef", "abcdefg", "abcdefgh", "ñ", "é🎵" })
{
    var block = codec.Encode(text);
    var h = hamming ? new Hamming74Codec().Decode(new Hamming74Codec().Encode(block)) : null;
    string received = hamming ? new Hamming74Codec().Encode(block) : block;
    var channel = new FdmTextChannelResult(FdmTextReceiveState.Valid, true, codec.EncodeBytes(text)[0], true, text, Sequence(received), "")
    { UsesHamming = hamming, HammingDiagnostics = h, HammingCoversCompleteBlock = hamming };
    var sections = FramePresentation.Sections(channel);
    int factorSize(int count) => hamming ? count / 4 * 7 : count;
    int length = channel.ByteLength!.Value;
    Require(sections[0].EncodedSize == 24 && sections[0].OriginalSize == 24, "Cabecera no protegida");
    Require(sections[1].EncodedStart == 0 && sections[1].EncodedSize == factorSize(8), "Longitud");
    Require(sections[2].EncodedStart == factorSize(8) && sections[2].EncodedSize == factorSize(length * 8), "Datos según bytes reales");
    Require(sections[3].EncodedStart == factorSize(8 + length * 8) && sections[3].EncodedSize == factorSize(32), "CRC");
    Require(sections.Skip(1).Sum(s => s.EncodedSize) == received.Length, "Sin cabecera ni relleno en bloque");
    var characters = FramePresentation.Characters(channel);
    Require(characters.SelectMany(c => c.Bytes).SequenceEqual(codec.EncodeBytes(text).Skip(1).Take(length)), "Bytes del contenido validado");
    Require(string.Concat(characters.Select(c => c.Bits.Replace(" ", ""))) == block.Substring(8, length * 8), "Bits UTF-8 sin cabecera, longitud ni CRC");
    Require(string.Concat(characters.Select(c => c.Character)) == text && characters.Last().ByteOffset + characters.Last().Bytes.Count == length, "Unicode multibyte");
    Require(FramePresentation.Characters(channel with { State = FdmTextReceiveState.CrcIncorrect, CrcValid = false }).Count == 0 &&
        FramePresentation.Characters(channel with { State = FdmTextReceiveState.InconclusiveBits, CrcValid = null }).Count == 0, "No inventa texto con CRC inválido/no comprobado");
    var unknown = FramePresentation.Sections(channel with { ByteLength = null });
    Require(unknown[2].EncodedStart is null && unknown[3].EncodedStart is null && unknown[1].EncodedSize == factorSize(8), "Longitud desconocida no delimita datos ni CRC");
}

foreach (int length in Enumerable.Range(1, 8))
for (int original = 0; original < 8 + length * 8 + 32; original++)
{
    var p = FramePresentation.Position(true, true, false, original, length);
    int code = original / 4 * 7 + new[] { 3, 5, 6, 7 }[original % 4] - 1;
    Require(p.OriginalIndex == original && p.EncodedIndex == code && p.FrameIndex == 24 + code && p.DataPosition == original % 4 + 1, "Índices originales y correspondencia codificada");
    Require(FramePresentation.Position(true, false, false, code, length).OriginalIndex == original, "Mapeo inverso de información");
    bool payload = original >= 8 && original < 8 + length * 8;
    Require(payload ? p.Utf8Byte == (original - 8) / 8 && p.Utf8Bit == (original - 8) % 8 + 1 : p.Utf8Byte is null, "Posición dentro de byte UTF-8");
}
foreach (int position in new[] { 0, 1, 3 })
    Require(FramePresentation.Position(true, false, false, position, 5).OriginalIndex is null, "La paridad no se inventa como bit original");
Require(FramePresentation.Position(true, false, true, 16, 5) is { FrameIndex: 16, WordIndex: null, EncodedIndex: null }, "Cabecera fuera del bloque Hamming");

// Integración con el emisor/receptor reales: no se construyen los bits desde el texto recuperado.
var reception = new FdmTextReceiver().Receive(new FdmBitstreamGenerator().GenerateTextFrames("Hola", "Mundo", true), 48000, useHamming: true);
Require(reception.ChannelA.HammingDiagnostics!.WordCount == 18 && reception.ChannelB.HammingDiagnostics!.WordCount == 20, "Ejemplo sin relleno en diagnósticos");
Exception? failure = null;
var thread = new Thread(() =>
{
    try
    {
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown }; app.InitializeComponent();
        var view = new FrameExplorerView(); view.Show(reception);
        Button Section(FramePart part) => Children<Button>(view).Single(b => b.Tag is FramePart p && p == part);
        Section(FramePart.Header).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(Children<Button>(view).Count(b => b.Tag is int) == 3 && Children<Button>(view).Count(b => b.Tag is DisplayBit) == 8, "Tres bytes de cabecera, un detalle real a la vez");
        Children<Button>(view).Single(b => b.Tag is int i && i == 16).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(Children<Button>(view).Where(b => b.Tag is DisplayBit).Select(b => ((DisplayBit)b.Tag).Observed).SequenceEqual(reception.Synchronization.ChannelA!.Header.Bits[16..24].Select(c => (char?)c)), "Marcador real, no patrón supuesto");
        Section(FramePart.Crc).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(Children<Button>(view).Count(b => b.Tag is DisplayBit) == 7 &&
            Children<Button>(view).Count(b => b.Tag is int) == 8, "CRC: ocho grupos, solo una palabra desplegada");
        view.SelectContentBytes(1, 0, 1);
        Require(Children<Button>(view).Count(b => b.Tag is DisplayBit) == 8 &&
            Children<TextBlock>(view).Any(t => t.Text.Contains("reconstruido") || t.Text.Contains("Salida real de Hamming")), "Cambio a B y selección de contenido reconstruido");
        var card = new ReceptionView();
        card.TextRequested += () => view.OpenText(0);
        card.Show(reception.ChannelA, reception.Synchronization, "A");
        Children<Button>(card).Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var tabs = Children<TabControl>(view).Single();
        Require(tabs.Items.Count == 3 && ((TabItem)tabs.SelectedItem).Header.ToString() == "Texto / UTF-8", "Acceso directo a vista estable");
        var textView = Children<Utf8ContentView>(view).Single();
        Require(Children<WrapPanel>(textView).Any(w => w.Children.OfType<Border>().Count(b => b.Tag is ValidatedCharacter) == 4), "Tarjetas horizontales compartidas");
        view.SelectContentBytes(0, 1, 1);
        var byteBits = Children<Button>(view).Where(b => b.Tag is DisplayBit).ToArray();
        Require(byteBits.Length == 8 && byteBits.All(b => b.BorderThickness.Left == 3), "Seleccionar un byte resalta sus ocho bits originales");
        byteBits[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        view.OpenText(0);
        Require(Children<Border>(textView).Single(b => b.Tag is ValidatedCharacter c && c.ByteOffset == 1).BorderThickness.Left == 3, "Bit de contenido resalta carácter validado");
        Children<Button>(textView).Single(b => Equals(b.Content, "Localizar este byte en Trama")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(((TabItem)tabs.SelectedItem).Header.ToString() == "Trama", "Navegación texto hacia trama");
        var noLength = reception with { ChannelA = reception.ChannelA with { ByteLength = null, State = FdmTextReceiveState.InconclusiveLength, Text = null, CrcValid = null } };
        view.Show(noLength); Children<ComboBox>(view).First(c => c.Items.Contains("Canal A")).SelectedIndex = 0;
        Section(FramePart.Payload).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(!Children<BitMapView>(view).Any(), "Sin longitud no dibuja estructura ficticia");
        var oldDiagnostic = reception.ChannelA.HammingDiagnostics!;
        var shortDiagnostic = new Hamming74DecodeResult(oldDiagnostic.DataBits[..8], Array.AsReadOnly(oldDiagnostic.Words.Take(2).ToArray()))
        { Traces = Array.AsReadOnly(oldDiagnostic.Traces.Take(2).ToArray()) };
        view.Show(noLength with { ChannelA = noLength.ChannelA with { HammingDiagnostics = shortDiagnostic } });
        tabs.SelectedIndex = 2;
        Require(Children<Button>(view).Count(b => b.Tag is int) == 2, "Hamming muestra solo las dos trazas reales de longitud");
        tabs.SelectedIndex = 0;
        var damaged = reception.ChannelA.Block!.Bits.ToCharArray(); damaged[14] = '?';
        var changed = reception with { ChannelA = reception.ChannelA with { Block = Sequence(new string(damaged)),
            HammingDiagnostics = new Hamming74Codec().Decode(new string(damaged), true), ErasureRecoveryEnabled = true } };
        view.Show(changed);
        Require(Children<Button>(view).Single(b => b.Tag is int i && i == 14).BorderThickness.Left == 3, "Primera selección prioriza una palabra interesante real");
        Children<Button>(view).Single(b => b.Tag is int i && i == 21).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        tabs.SelectedIndex = 2; tabs.SelectedIndex = 0;
        Require(Children<Button>(view).Single(b => b.Tag is int i && i == 21).BorderThickness.Left == 3, "Navegar no reemplaza una selección explícita");
        view.Measure(new Size(1200, 500)); view.Arrange(new Rect(0, 0, 1200, 500)); view.UpdateLayout();
        var workspace = (Grid)((TabItem)tabs.SelectedItem).Content;
        Require(Grid.GetColumn(workspace.Children.OfType<Grid>().Single()) == 1, "Ancho disponible se usa en dos columnas");
        view.Measure(new Size(600, 500)); view.Arrange(new Rect(0, 0, 600, 500)); view.UpdateLayout();
        Require(Grid.GetRow(workspace.Children.OfType<Grid>().Single()) == 1, "Ventana pequeña adapta selección y detalle");
        view.Clear(); Require(!Children<BitMapView>(view).Any(), "Invalidación elimina trama anterior");
        app.Shutdown();
    }
    catch (Exception ex) { failure = ex; }
});
thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
if (failure is not null) throw new InvalidOperationException("Explorador de trama", failure);
Console.WriteLine("Trama 1–8 bytes, Unicode, rangos, Hamming sí/no, CRC, selección y bytes validados: OK");
static BfskSequenceResult Sequence(string bits) => new(Array.AsReadOnly(bits.Select((bit, i) => new BfskSymbolResult(i, i, i + 1, i, 1,
    new BfskDetectionResult(0, 0, bit == '?' ? null : bit - '0', ""))).ToArray()));
static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject
{
    foreach (object item in LogicalTreeHelper.GetChildren(root)) if (item is DependencyObject child)
    {
        if (child is T match) yield return match;
        foreach (var nested in Children<T>(child)) yield return nested;
    }
}
static void Require(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
