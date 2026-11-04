#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property PublishAot=false
#:property PublishTrimmed=false
#:project ../AcousticLink.App/AcousticLink.App.csproj

using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AcousticLink.App;
using AcousticLink.App.Experiments;
using AcousticLink.App.Protocol;
using AcousticLink.App.Visualization;

Exception? failure = null;
string? temporaryWav = null;
var thread = new Thread(() =>
{
    try
    {
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown }; app.InitializeComponent();
        var window = new MainWindow { Width = 1366, Height = 768, Left = -10000, Top = -10000, ShowInTaskbar = false };
        // Aislar la prueba de la preferencia personal; no escribirla ni abrir el micrófono.
        ((System.Windows.Controls.CheckBox)window.FindName("AutoListenCheckBox")).IsChecked = false;
        window.Show();
        var tabs = (TabControl)window.FindName("MainSectionsTabs");
        tabs.SelectedIndex = 3;
        var analysis = (Grid)((TabItem)tabs.Items[3]).Content;
        var inner = (TabControl)window.FindName("AnalysisTools");
        Require(inner.Items.Count == 3 && ((TabItem)inner.Items[0]).Header.ToString() == "Resumen", "Navegación interna");
        var before = FdmExperimentAnalyzer.Compare("00000000", "00?001");
        var after = FdmExperimentAnalyzer.Compare("0000", "00??");
        var reception = new FdmTextChannelResult(FdmTextReceiveState.InconclusiveBits, true, 4, null, null, null, "Incompleto");
        var channel = new FdmChannelExperiment("A", "a", "0000", "00000000", before, after, null, reception, "");
        ((MetricsView)window.FindName("MetricsAView")).Show(channel, true);
        ((MetricsView)window.FindName("MetricsBView")).Show(channel with { Channel = "B" }, true);
        Pump();
        var summary = (Grid)((TabItem)inner.Items[0]).Content;
        Require(summary.ActualWidth > 1000 && summary.ActualHeight > 250, "Resumen usa ancho y alto disponibles");
        // El bloque de configuración está cerrado al entrar; los resultados son la vista principal.
        Require(((System.Windows.Controls.Primitives.ToggleButton)window.FindName("AnalysisSettingsToggle")).IsChecked == false, "Configuración secundaria");
        foreach (var size in new[] { new Size(1366, 768), new Size(1920, 1080), new Size(1366 / 1.25, 768 / 1.25) })
        {
            window.Width = size.Width; window.Height = size.Height; inner.SelectedIndex = 0; Pump();
            Require(summary.ActualWidth > size.Width - 280 && summary.ActualHeight > 100, "Área de trabajo sin desbordamiento horizontal en tamaños de escritorio y equivalencia lógica del 125 %");
        }
        foreach (TabItem tab in inner.Items) { inner.SelectedItem = tab; Pump(); }
        Require(tabs.Items.Count == 4, "Se preservan las cuatro secciones principales");
        // Resultados de integración sintética, etiquetados como tales en las capturas opcionales.
        window.Width = 1366; window.Height = 768;
        var samples = new AcousticLink.App.Signals.FdmBitstreamGenerator().GenerateTextFrames("Hola", "Mundo", true);
        var measured = new FdmExperimentAnalyzer().Analyze(samples, 48000, "Hola", "Mundo", true, audioSource: "Ensayo sintético de interfaz", recoverErasures: true);
        var received = new FdmTextReceiveResult(measured.Synchronization, measured.ChannelA.Reception, measured.ChannelB.Reception);
        ((MetricsView)window.FindName("MetricsAView")).Show(measured.ChannelA, true);
        ((MetricsView)window.FindName("MetricsBView")).Show(measured.ChannelB, true);
        ((HammingTraceView)window.FindName("HammingWordTraceView")).Show(measured);
        ((ExperimentDiagnosticView)window.FindName("TechnicalDiagnosticView")).Show(measured, samples.Length);
        ((ReceptionView)window.FindName("ReceptionAView")).Show(received.ChannelA, received.Synchronization, "A");
        ((ReceptionView)window.FindName("ReceptionBView")).Show(received.ChannelB, received.Synchronization, "B");
        ((FrameExplorerView)window.FindName("ReceivedFrameExplorer")).Show(received);
        temporaryWav = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AcousticLink-ui-synthetic-" + Guid.NewGuid() + ".wav");
        var captured = new AcousticLink.App.Audio.CapturedAudio(48000, 1, 30);
        captured.Append(samples, samples.Length); captured.Complete(AcousticLink.App.Audio.CaptureStopReason.Manual); captured.SaveWav(temporaryWav);
        var selection = (AcousticLink.App.Audio.AudioRecordingSelection)typeof(MainWindow).GetField("audioSelection", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(window)!;
        selection.ImportWav(temporaryWav);
        foreach (string name in new[] { "UseHammingReceiveCheckBox", "ExperimentHammingCheckBox", "RecoverReceiveErasuresCheckBox", "RecoverExperimentErasuresCheckBox" }) ((System.Windows.Controls.Primitives.ToggleButton)window.FindName(name)).IsChecked = true;
        typeof(MainWindow).GetMethod("RefreshCapture", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window, null);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (((WaveformView)window.FindName("ReceivedWaveform")).PeakAbsolute == 0 && DateTime.UtcNow < deadline) { Pump(); Thread.Sleep(10); }
        Require(((WaveformView)window.FindName("ReceivedWaveform")).PeakAbsolute > 0, "WAV importado alimenta la misma forma de onda real");
        ((TextBlock)window.FindName("WaveformInfoText")).Text = "Ensayo sintético de interfaz · 48.000 Hz · no captura física";
        ((TextBlock)window.FindName("GeneralStateText")).Text = "Comprobación sintética";
        ((TextBlock)window.FindName("AnalysisConfigurationWarning")).Text = "Ensayo sintético · resultados reales del analizador · no captura física";
        tabs.SelectedIndex = 2; Pump();
        var receiverTools = (TabControl)window.FindName("ReceiverTools");
        Require(((FrameExplorerView)window.FindName("ReceivedFrameExplorer")).FitWorkspace && receiverTools.Items.Count == 3, "Explorador utiliza la altura del workspace");
        var toggle = (System.Windows.Controls.Primitives.ToggleButton)window.FindName("ReceiverInspectorToggle");
        toggle.IsChecked = false; toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); Pump();
        Require(((Border)window.FindName("ReceiverInspector")).Visibility == Visibility.Collapsed, "Inspector ocultable sin perder resultados");
        toggle.IsChecked = true; toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); Pump();
        if (args.Length > 0) Snapshot(window, System.IO.Path.Combine(args[0], "receptor-workspace.png"));
        {
            receiverTools.SelectedIndex = 2; Pump();
            var monitor = (SignalMonitorView)window.FindName("ReceiverSignalMonitor");
            var recorded = CarrierEnergyMap.Analyze(new(samples, 48000, MonitorSignalOrigin.ImportedWav, "WAV sintético de interfaz"));
            monitor.Show(recorded); monitor.SetReception(received, recorded.Signal.Duration); monitor.SetCursor(3.25);
            var monitorTools = (TabControl)typeof(SignalMonitorView).GetField("signalTools", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(monitor)!;
            monitorTools.SelectedIndex = 0; monitor.Zoom(3.2, 3.5); monitor.SelectInterval(3.2, 3.3); monitor.SetCursor(3.25); Pump();
            if (args.Length > 0) Snapshot(window, System.IO.Path.Combine(args[0], "receptor-simbolos.png"));
            monitorTools.SelectedIndex = 2; Pump();
            var lab = (ReceiverInterpretationView)typeof(SignalMonitorView).GetField("interpretation", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(monitor)!;
            T Part<T>(string name) => (T)typeof(ReceiverInterpretationView).GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(lab)!;
            int firstNumber = lab.Selection!.Trace!.Number;
            Part<Button>("next").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); Pump();
            Require(lab.Selection?.Trace?.Number == firstNumber + 1, "Siguiente actualiza ventana, energías y selección real");
            Part<Button>("previous").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); Pump();
            Require(lab.Selection?.Trace?.Number == firstNumber, "Anterior restaura símbolo");
            monitorTools.SelectedIndex = 0; monitorTools.SelectedIndex = 2; Pump();
            Require(lab.Selection?.Trace?.Number == firstNumber, "Cambiar de vista conserva selección");
            Require(!Part<Button>("firstUnknown").IsEnabled && !Part<Button>("nextUnknown").IsEnabled && !Part<Button>("problem").IsEnabled, "Accesos sin coincidencias deshabilitados");
            Part<ComboBox>("channel").SelectedIndex = 1;
            var crcSymbol = ReceivedSymbols.From(received, true, 48000).First(t => t.Number == 110);
            monitor.SetCursor((crcSymbol.Symbol.AnalysisStartSample + crcSymbol.Symbol.AnalysisSampleCount / 2.0) / 48000); Pump();
            Require(lab.Selection!.Trace!.ChannelB && Part<TextBlock>("field").Text == "Campo CRC-32" && Part<TextBlock>("decoded").Text.StartsWith("Nibble decodificado:"), "CRC de B se identifica como campo y nibble, nunca como contenido UTF-8");
            foreach (var size in new[] { new Size(1366, 768), new Size(1920, 1080), new Size(1366 / 1.25, 768 / 1.25) })
            {
                window.Width = size.Width; window.Height = size.Height; Pump();
                Require(lab.ActualWidth > 650 && lab.ActualHeight > 150 && Part<Button>("next").ActualWidth > 10, "Laboratorio adaptable en tamaños habituales y equivalencia lógica 125 %");
            }
            window.Width = 1366; window.Height = 768; Pump();
            if (args.Length > 0) Snapshot(window, System.IO.Path.Combine(args[0], "receptor-interpretacion.png"));
            monitor.Clear(); Pump();
            Require(lab.Selection is null && !Part<Button>("next").IsEnabled && Part<TextBlock>("field").Text == "", "Cambiar fuente invalida selección y navegación");
            receiverTools.SelectedIndex = 0; toggle.IsChecked = true; toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); Pump();
        }
        var inspectorTabs = (TabControl)window.FindName("ReceiverInspectorTabs");
        var detailTabs = (TabControl)window.FindName("ReceiverDetailTabs");
        inspectorTabs.SelectedIndex = 1; detailTabs.SelectedIndex = 0; Pump();
        Require(ReferenceEquals(((ReceptionInspectorView)window.FindName("ReceptionAInspector")).Result, received.ChannelA), "Inspector A utiliza el mismo resultado real");
        if (args.Length > 0) Snapshot(window, System.IO.Path.Combine(args[0], "inspector-recepcion.png"));
        detailTabs.SelectedIndex = 1; Pump();
        Require(ReferenceEquals(((ReceptionInspectorView)window.FindName("ReceptionBInspector")).Result, received.ChannelB), "Inspector B independiente");
        ((ReceptionView)window.FindName("ReceptionAView")).Clear(); Pump();
        Require(((ReceptionInspectorView)window.FindName("ReceptionAInspector")).Result is null, "Invalidación elimina propiedades del inspector anterior");
        ((ReceptionView)window.FindName("ReceptionAView")).Show(received.ChannelA, received.Synchronization, "A");
        tabs.SelectedIndex = 3; inner.SelectedIndex = 0; Pump();
        if (args.Length > 0) Snapshot(window, System.IO.Path.Combine(args[0], "analisis-workspace.png"));
        inner.SelectedIndex = 2; Pump();
        var technical = (ExperimentDiagnosticView)window.FindName("TechnicalDiagnosticView");
        var selectors = Descendants<ComboBox>(technical).ToArray();
        selectors.Single(c => c.Items.Contains("Canal A")).SelectedIndex = 1; Pump();
        Require(Descendants<TextBlock>(technical).Any(t => t.Text == "CANAL B"), "Diagnóstico selecciona canal independiente");
        selectors.Single(c => c.Items.Contains("Reconstruidos · bloque original")).SelectedIndex = 1; Pump();
        Require(Descendants<TextBlock>(technical).Any(t => t.Text == "Referencia original") && Descendants<TextBlock>(technical).Any(t => t.Text == "Reconstruido · Hamming"), "Referencias y salidas reales diferenciadas");
        if (args.Length > 0) Snapshot(window, System.IO.Path.Combine(args[0], "diagnostico-workspace.png"));
        selectors.Single(c => c.Items.Contains("Canal A")).SelectedIndex = 0;
        inner.SelectedIndex = 1; Pump();
        Require(!SystemParameters.ClientAreaAnimation || ((UIElement)((TabItem)inner.Items[1]).Content).HasAnimatedProperties, "Transición interna conectada");
        if (args.Length > 0) Snapshot(window, System.IO.Path.Combine(args[0], "hamming-workspace.png"));
        window.Width = 1366 / 1.25; window.Height = 768 / 1.25; Pump();
        if (args.Length > 0) Snapshot(window, System.IO.Path.Combine(args[0], "hamming-logical125.png"));
        var hammingView = (HammingTraceView)window.FindName("HammingWordTraceView");
        var reconstructed = (WrapPanel)typeof(HammingTraceView).GetField("reconstructed", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(hammingView)!;
        Require(reconstructed.ActualHeight > 0 && reconstructed.TransformToAncestor(hammingView).Transform(new Point(0, reconstructed.ActualHeight)).Y <= hammingView.ActualHeight, "Información reconstruida visible en el inspector al tamaño lógico del 125 %");
        tabs.SelectedIndex = 2; receiverTools.SelectedIndex = 0; Pump();
        var waveform = (WaveformView)window.FindName("ReceivedWaveform");
        var receiverRoot = (Grid)window.FindName("ReceiverWorkspace");
        var channelView = (ReceptionView)window.FindName("ReceptionAView");
        Require(!((Expander)window.FindName("CaptureInstrumentationDetails")).IsExpanded && !waveform.IsVisible && channelView.ActualHeight > 0, "Instrumentación secundaria cerrada; no se duplica la onda principal sobre los resultados");
        if (args.Length > 0) Snapshot(window, System.IO.Path.Combine(args[0], "receptor-logical125.png"));
        tabs.SelectedIndex = 3; inner.SelectedIndex = 0; Pump();
        if (args.Length > 0) Snapshot(window, System.IO.Path.Combine(args[0], "analisis-logical125.png"));
        inner.SelectedIndex = 2; Pump();
        if (args.Length > 0) Snapshot(window, System.IO.Path.Combine(args[0], "diagnostico-logical125.png"));
        tabs.SelectedIndex = 2; receiverTools.SelectedIndex = 1;
        ((FrameExplorerView)window.FindName("ReceivedFrameExplorer")).SelectContentBytes(0, 0, 1); Pump();
        if (args.Length > 0) Snapshot(window, System.IO.Path.Combine(args[0], "explorador-workspace.png"));
        window.Width = 1366; window.Height = 768; tabs.SelectedIndex = 1; Pump();
        if (args.Length > 0) Snapshot(window, System.IO.Path.Combine(args[0], "emisor-workspace.png"));
        tabs.SelectedIndex = 0; Pump();
        if (args.Length > 0) Snapshot(window, System.IO.Path.Combine(args[0], "panel-general-workspace.png"));
        window.Close(); Pump(); app.Shutdown();
    }
    catch (Exception ex) { failure = ex; }
});
thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
if (temporaryWav is not null && System.IO.File.Exists(temporaryWav)) System.IO.File.Delete(temporaryWav);
if (failure is not null) throw new InvalidOperationException("Distribución compacta", failure);
Console.WriteLine("1366×768: navegación interna, resumen en paralelo, configuración secundaria y estados incompletos: OK");
static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
static void Require(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }

static void Snapshot(Window window, string path)
{
    var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
    timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    window.UpdateLayout();
    var image = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
    image.Render(window); var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image)); using var output = System.IO.File.Create(path); encoder.Save(output);
}

static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
{
    foreach(object item in LogicalTreeHelper.GetChildren(root)) if(item is DependencyObject child)
    {
        if(child is T match) yield return match;
        foreach(var nested in Descendants<T>(child)) yield return nested;
    }
}
