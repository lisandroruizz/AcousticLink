#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property PublishAot=false
#:property PublishTrimmed=false
#:project ../AcousticLink.App/AcousticLink.App.csproj

using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AcousticLink.App;
using AcousticLink.App.Audio;
using AcousticLink.App.Signals;
using AcousticLink.App.Visualization;

foreach (int rate in new[] { 48000, 44100 })
foreach (var setting in FdmAmplitudeProfiles.All)
foreach (bool a in new[] { false, true })
foreach (bool b in new[] { false, true })
{
    float[] samples = rate == 48000 ? new FdmToneMixer().Generate(a, b, profile: setting.Profile)
        : Enumerable.Range(0, rate).Select(i => (float)(setting.ChannelA * Math.Sin(2 * Math.PI * (a ? 1900 : 1200) * i / rate) + setting.ChannelB * Math.Sin(2 * Math.PI * (b ? 3700 : 3000) * i / rate))).ToArray();
    var original = samples.ToArray();
    var map = Analyze(samples, rate);
    var frame = map.Windows[map.WindowAt(.5)];
    for (int i = 0; i < 4; i++)
    {
        double expected = i == (a ? 1 : 0) ? setting.ChannelA * setting.ChannelA / 2 : i == (b ? 3 : 2) ? setting.ChannelB * setting.ChannelB / 2 : 0;
        Require(double.IsFinite(frame.Energy(i)) && frame.Energy(i) >= 0 && Math.Abs(frame.Energy(i) - expected) < 1e-9, "Potencia real, simultaneidad y separación de cuatro portadoras");
    }
    Require(map.WindowSamples == rate / 50 && map.HopSamples == rate / 100 && map.Signal.Duration == 1, "Ventana/paso/tasa/duración reales");
    Require(map.Windows[0].StartSample == 0 && map.Windows[^1].StartSample + map.Windows[^1].SampleCount == samples.Length, "Cobertura temporal hasta el final");
    Require(samples.SequenceEqual(original), "El análisis no altera las muestras");
    Require(map.Overview.Min(v => v.Minimum) == samples.Min() && map.Overview.Max(v => v.Maximum) == samples.Max(), "Reducción preserva picos");
}
var silent = Analyze(new float[48000], 48000);
Require(silent.Peak == 0 && silent.Windows.All(w => Enumerable.Range(0, 4).All(i => w.Energy(i) == 0)) && double.IsNegativeInfinity(CarrierEnergyMap.Decibels(0)), "Silencio no inventa energía");
float[] weak = Enumerable.Range(0, 44100).Select(i => (float)(1e-7 * Math.Sin(2 * Math.PI * 1200 * i / 44100))).ToArray();
Require(Math.Abs(Analyze(weak, 44100).Windows[10].A0 / 5e-15 - 1) < 1e-6, "Señal débil conservada sin umbral ni normalización");
foreach (int count in new[] { 1, 200, 49371 })
{
    var map = Analyze(new float[count], 48000);
    Require(map.Windows[^1].StartSample + map.Windows[^1].SampleCount == count, "Final no múltiplo de salto y audio menor que ventana");
    Require(map.WindowAt(0) == 0 && map.WindowAt(map.Signal.Duration) == map.Windows.Count - 1, "Cursor acotado");
}
var range = CarrierEnergyMap.Interval(.123, .127, 44100, 44100);
Require(range.Start <= .123 * 44100 && range.End >= .127 * 44100, "Selección conserva soporte temporal de muestras reales");
foreach (Action action in new Action[] { () => CarrierEnergyMap.Interval(-1, .1, 48000, 48000), () => CarrierEnergyMap.Interval(0, 2, 48000, 48000), () => Analyze([], 48000), () => Analyze([float.NaN], 48000), () => Analyze([0], 7000) })
{ try { action(); throw new Exception("Falta validación"); } catch (ArgumentException) { } }
using (var canceled = new CancellationTokenSource())
{ canceled.Cancel(); try { CarrierEnergyMap.Analyze(new(new float[48000], 48000, MonitorSignalOrigin.Generated, "Cancelación"), canceled.Token); throw new Exception("Falta cancelación"); } catch (OperationCanceledException) { } }
var sequence = new FdmBitstreamGenerator().Generate("10110010", "01001101");
var before = new FdmSequenceDecoder().Decode(sequence, 48000, 0, 8);
_ = Analyze(sequence, 48000);
var after = new FdmSequenceDecoder().Decode(sequence, 48000, 0, 8);
Require(before.ChannelA.Bits == after.ChannelA.Bits && before.ChannelB.Bits == after.ChannelB.Bits, "Decisiones del módem intactas");
// Integración WPF con audio sintético y archivos reales; no abre el micrófono ni reproduce audio.
Exception? failure = null;
var thread = new Thread(() =>
{
    string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"AcousticLink-monitor-{Guid.NewGuid()}.wav");
    try
    {
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown }; app.InitializeComponent();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var window = new MainWindow { ShowInTaskbar = false, Left = -10000, Width = 1366, Height = 768 }; // Aislar la prueba de la preferencia personal; no escribirla ni abrir el micrófono.
        ((System.Windows.Controls.CheckBox)window.FindName("AutoListenCheckBox")).IsChecked = false;
        window.Show();
        T Find<T>(string name) => (T)window.FindName(name);
        void Invoke(string method, params object[] values) => typeof(MainWindow).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, values);
        var view = Find<SignalMonitorView>("DashboardSignalMonitor");
        Require(view.Data is null, "Estado vacío sin señal ficticia");
        var captured = new CapturedAudio(48000, 1); captured.Append(sequence, sequence.Length); captured.Complete(CaptureStopReason.Manual); captured.SaveWav(path);
        var selection = (AudioRecordingSelection)typeof(MainWindow).GetField("audioSelection", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
        selection.ImportWav(path); Invoke("RefreshCapture");
        try { Wait(() => view.Data is not null); } catch (Exception ex) { throw new Exception((string?)typeof(MainWindow).GetField("observedMonitorStatus", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window), ex); }
        Require(view.Data!.Signal.Origin == MonitorSignalOrigin.ImportedWav && view.Data.Signal.Samples.Span.SequenceEqual(sequence), "WAV conserva contenido y origen");
        Find<TabControl>("DashboardTools").SelectedIndex = 1;
        view.SelectInterval(.2, .21); view.Zoom(.2, .21); view.SetCursor(.205); Pump();
        Require(view.ViewStart <= .2 && view.ViewEnd >= .21 && view.SelectionStart == .2 && view.CursorSeconds == .205 && view.CursorDescription.Contains("3700 Hz"), "Zoom, selección y energías de cursor sincronizados");
        view.Zoom(.2, .3); double width = view.ViewEnd - view.ViewStart;
        view.Pan(.25); Require(Math.Abs(view.ViewEnd - view.ViewStart - width) < 1e-10, "Pan conserva cantidad de muestras");
        view.Pan(-100); Require(view.ViewStart == 0, "Límite izquierdo exacto"); view.Pan(100); Require(view.ViewEnd == view.Data!.Signal.Duration, "Límite derecho exacto");
        view.Zoom(0, .8); view.ZoomAt(.4, .5); Require(Math.Abs((.4 - view.ViewStart) / (view.ViewEnd - view.ViewStart) - .5) < 1e-4, "Zoom anclado al cursor");
        foreach(double duration in new[]{.005, .1, .8}) { view.SetWindowDuration(duration); Require(Math.Abs(view.ViewEnd - view.ViewStart - duration) < 2.0 / 48000, "Ventanas rápidas"); }
        var imported = view.Data;
        try { selection.ImportWav(path + ".missing"); } catch (System.IO.IOException) { }
        Invoke("RefreshCapture"); Pump(); Require(ReferenceEquals(imported, view.Data), "Importación fallida conserva monitor válido");
        Invoke("AnalyzeMonitor_Click", window, new RoutedEventArgs()); Invoke("CancelMonitor_Click", window, new RoutedEventArgs());
        Wait(() => typeof(MainWindow).GetField("observedMonitorCancellation", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window) is null);
        Require(view.Data is null && selection.Current!.Audio.GetSamples().SequenceEqual(sequence), "Cancelación conserva audio y descarta análisis cancelado");
        Invoke("AnalyzeMonitor_Click", window, new RoutedEventArgs()); Wait(() => view.Data is not null);
        Invoke("RememberGeneratedSignal", sequence, 48000, "Buffer sintético realmente preparado");
        Find<ComboBox>("MonitorSourceComboBox").SelectedIndex = 1; Wait(() => view.Data is not null);
        Require(view.Data!.Signal.Origin == MonitorSignalOrigin.Generated && view.Data.Signal.Samples.Span.SequenceEqual(sequence) && ReferenceEquals(view.Data, Find<SignalMonitorView>("GeneratedSignalMonitor").Data), "Un cálculo compartido, origen generado inequívoco");
        Invoke("RememberDetailedSignal", sequence, 48000, "Contribuciones verificadas", "10110010", "01001101", FdmAmplitudeProfile.Original, .1, null!);
        Wait(() => view.Data?.Signal.Description == "Contribuciones verificadas");
        var signal = view.Data!.Signal;
        Require(signal.ChannelA.Length == sequence.Length && Enumerable.Range(0, sequence.Length).All(i => signal.ChannelA.Span[i] + signal.ChannelB.Span[i] == sequence[i]), "A+B exacta sin separar audio acústico");
        Invoke("PrepareSignalPreview_Click", window, new RoutedEventArgs());
        Wait(() => view.Data?.Signal.Description.Contains("sin reproducción") == true);
        Require(!(bool)typeof(MainWindow).GetField("playing", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!, "Vista previa sin playback");
        var transmission = view.Data!.Signal.Transmission;
        Require(transmission?.Frames is not null && transmission.Segments(false).Sum(f => f.SymbolCount) == transmission.BitsA.Length, "Metadatos de trama real en vista previa");
        var toolSelector = (TabControl)typeof(SignalMonitorView).GetField("signalTools", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(view)!;
        toolSelector.SelectedIndex = 1; Pump();
        Require(Math.Abs(view.ViewEnd - view.ViewStart - .01) < 2.0 / 48000, "Herramienta didáctica inicial de 10 ms comparte zoom");
        view.SetCursor(.2); Require(view.CursorSeconds == .2, "Cursor compartido con la formación FDM");
        if (args.Length > 1)
        {
            Find<TabControl>("MainSectionsTabs").SelectedIndex = 0; Find<TabControl>("DashboardTools").SelectedIndex = 1;
            window.Width = 1366; window.Height = 768; view.Zoom(.095, .105); view.SetCursor(.1);
            var deadlineFormation = DateTime.UtcNow.AddMilliseconds(400); while (DateTime.UtcNow < deadlineFormation) { Pump(); Thread.Sleep(10); }
            window.UpdateLayout();
            var shot = new System.Windows.Media.Imaging.RenderTargetBitmap(1366, 768, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); shot.Render(window);
            var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(shot)); using var output = System.IO.File.Create(args[1]); png.Save(output);
        }
        toolSelector.SelectedIndex = 0; Pump();
        var generated = view.Data;
        Invoke("InvalidateAudioAnalysis"); Pump(); Require(ReferenceEquals(view.Data, generated), "Cambiar audio no atribuye el buffer generado a la captura");
        Find<ComboBox>("MonitorSourceComboBox").SelectedIndex = 0; Require(view.Data is null, "Invalidación del audio observado");
        Invoke("RefreshCapture"); Wait(() => view.Data is not null);
        Require(Find<TabControl>("MainSectionsTabs").SelectedIndex == 0 && Find<TabControl>("DashboardTools").SelectedIndex == 1 && view.IsLoaded && view.IsVisible,
            "Monitor cargado en la pestaña activa del Panel general");
        var workspaceScroll = Find<ScrollViewer>("DashboardMonitorScroll");
        foreach (var size in new[] { new Size(1366, 768), new Size(1180, 740), new Size(820, 600), new Size(1366 / 1.25, 768 / 1.25), new Size(1920, 1080) })
        {
            window.Width = size.Width; window.Height = size.Height; Pump();
            if (args.Length > 2) { var shot = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); shot.Render(window); var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(shot)); using var output = System.IO.File.Create(System.IO.Path.Combine(args[2], $"monitor-{size.Width:0}-{size.Height:0}.png")); png.Save(output); }
            Require(view.ActualWidth > (size.Width < 1000 ? 500 : 650) && view.ActualHeight >= 320, "Área estable en resoluciones de escritorio y equivalencia lógica al 125 %");
            var signalScroll = (ScrollViewer)((TabItem)toolSelector.Items[0]).Content;
            var waveform = (FrameworkElement)typeof(SignalMonitorView).GetField("waveform", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(view)!;
            var carriers = (FrameworkElement)typeof(SignalMonitorView).GetField("carriers", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(view)!;
            Require(toolSelector.SelectedIndex == 0 && signalScroll.IsVisible && signalScroll.ViewportHeight >= 100 && waveform.IsVisible && carriers.IsVisible && waveform.ActualHeight >= 80 && carriers.ActualHeight >= 112,
                $"Señal y detección mantiene acceso a ambos gráficos en el área compacta: tamaño {view.ActualWidth:0}×{view.ActualHeight:0}, pestaña {toolSelector.SelectedIndex} alto {toolSelector.ActualHeight:0}, visor {signalScroll.ViewportHeight:0}/{signalScroll.ActualHeight:0} visible {signalScroll.IsVisible}, onda {waveform.ActualHeight:0} visible {waveform.IsVisible}, portadoras {carriers.ActualHeight:0} visible {carriers.IsVisible}");
            if (args.Length > 2) Console.WriteLine($"Monitor {size.Width:0}×{size.Height:0}: vista {view.ActualWidth:0}×{view.ActualHeight:0}, visor {signalScroll.ViewportHeight:0}, desplazamiento {workspaceScroll.ScrollableHeight:0}");
            double VisibleHeight(FrameworkElement element)
            {
                double top = element.TranslatePoint(new Point(), workspaceScroll).Y;
                return Math.Max(0, Math.Min(top + element.ActualHeight, workspaceScroll.ViewportHeight) - Math.Max(top, 0));
            }
            Require(workspaceScroll.ViewportHeight >= 160 && VisibleHeight(waveform) >= 50, "La onda y los controles temporales están accesibles al abrir el monitor");
            var timeTools = ((Grid)view.Content).Children.OfType<ScrollViewer>().Single();
            Require(timeTools.IsVisible && timeTools.ViewportWidth >= 300, "Navegación temporal disponible en el ancho compacto");
            if (timeTools.ScrollableWidth > 0)
            {
                timeTools.ScrollToRightEnd(); Pump();
                Require(timeTools.HorizontalOffset >= timeTools.ScrollableWidth - 1, "El desplazamiento horizontal alcanza los últimos controles temporales");
                timeTools.ScrollToLeftEnd(); Pump();
            }
            double energyBottom = carriers.TranslatePoint(new Point(0, carriers.ActualHeight), workspaceScroll).Y;
            workspaceScroll.ScrollToVerticalOffset(workspaceScroll.VerticalOffset + Math.Max(0, energyBottom - workspaceScroll.ViewportHeight)); Pump();
            Require(VisibleHeight(carriers) >= 80, "El desplazamiento permite visualizar el gráfico de energías");
            if (args.Length > 2 && size.Width < 1100) { var shot = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); shot.Render(window); var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(shot)); using var output = System.IO.File.Create(System.IO.Path.Combine(args[2], $"monitor-{size.Width:0}-{size.Height:0}-desplazado.png")); png.Save(output); }
            workspaceScroll.ScrollToTop(); Pump();
        }
        window.Width = 1366; window.Height = 768; view.Zoom(0, view.Data!.Signal.Duration); Pump();
        if (args.Length > 0)
        {
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(1366, 768, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); bitmap.Render(window);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var output = System.IO.File.Create(args[0]); encoder.Save(output);
        }
        var receiverMonitor = Find<SignalMonitorView>("ReceiverSignalMonitor"); var previousAcoustic = receiverMonitor.Data;
        // Ruta de salida simulada: el contador es inyectado, no una medición de hardware.
        Invoke("RememberGeneratedSignal", sequence, 48000, "Salida simulada para comprobar sincronización");
        Wait(() => Find<SignalMonitorView>("GeneratedSignalMonitor").Data?.Signal.Description == "Salida simulada para comprobar sincronización");
        var playback = typeof(MainWindow).GetField("playback", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
        var progressField = playback.GetType().GetField("progress", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var mini = Find<SignalMiniView>("MiniSignalMonitor");
        foreach (int delivered in new[] { 4800, 12000, 24000 })
        {
            progressField.SetValue(playback, new PlaybackProgress(sequence, 48000, delivered, true, "Reloj de salida simulado"));
            Invoke("RefreshStreamingMonitorAsync");
            Wait(() => view.Data?.Signal.Samples.Length == delivered && mini.EndSeconds == view.ViewEnd);
            Require(view.Data!.Signal.Origin == MonitorSignalOrigin.Generated && mini.IsLive && mini.Origin == MonitorSignalOrigin.Generated, "Miniatura y monitor comparten posición y origen digital");
            Require(view.Data.Windows.All(w => w.StartSample + w.SampleCount <= delivered), "No se analizan muestras futuras de la salida");
        }
        Require(ReferenceEquals(previousAcoustic, receiverMonitor.Data), "La transmisión digital no sustituye la fuente acústica del Receptor");
        receiverMonitor.UpdateLive(view.Data!); Require(ReferenceEquals(previousAcoustic, receiverMonitor.Data), "Defensa de origen en la vista del receptor");
        view.Pan(-.1); Require(!view.FollowingCapture, "Pausar seguimiento no pausa salida");
        progressField.SetValue(playback, new PlaybackProgress(sequence, 48000, 24000, false, "Reloj de salida simulado"));
        Invoke("RefreshStreamingMonitorAsync"); Wait(() => !mini.IsLive);
        progressField.SetValue(playback, null);
        Require(!view.IsLive && view.Data!.Signal.Samples.Length == sequence.Length, "Salida detenida conserva buffer completo para inspección");
                var liveSamples = new float[48000 * 6];
        CarrierEnergyMap Live(int seconds) => CarrierEnergyMap.Live(new(liveSamples.AsMemory(0, seconds * 48000), 48000, MonitorSignalOrigin.Microphone, "Captura sintética"), WaveformView.Reduce(liveSamples.AsSpan(0, seconds * 48000)), 0);
        view.UpdateLive(Live(4)); Require(view.FollowingCapture && view.ViewEnd == 4 && view.ViewStart == 1, "Seguir últimos tres segundos");
        view.Pan(-.5); double paused = view.ViewStart; view.UpdateLive(Live(5)); Require(!view.FollowingCapture && view.ViewStart == paused, "Navegación manual suspende seguimiento");
        view.FollowCapture(); Require(view.FollowingCapture && view.ViewEnd == 5, "Volver a En vivo");
        view.Show(imported!); Require(!view.FollowingCapture && view.Data!.Signal.Origin == MonitorSignalOrigin.ImportedWav, "Finalización/origen acústico independiente");
        var miniSource = Analyze(sequence, 48000); var preserved = sequence.ToArray();
        mini.Update(miniSource, [], 0, .8, true, .25, miniSource);
        Require(mini.DetailSampleCount == 480 && Math.Abs(mini.DetailStartSeconds - .24) < 1e-9 && Math.Abs(mini.DetailEndSeconds - .25) < 1e-9, "Detalle de 10 ms en la posición real");
        Require(mini.TotalSeconds == .8 && mini.CursorSeconds == .25 && mini.VerticalLimit > 0 && mini.IsLive, "Envolvente completa y cursor independientes del zoom");
        mini.Update(miniSource, [], 0, .8, false, .8, miniSource);
        Require(!mini.IsLive && mini.DetailEndSeconds == .8 && mini.TotalSeconds == .8, "Final conserva buffer y última posición");
        var wavSource = CarrierEnergyMap.Analyze(new(sequence, 48000, MonitorSignalOrigin.ImportedWav, "WAV sintético"));
        mini.Update(wavSource, [], 0, .8, false, 0);
        Require(!mini.IsLive && mini.Origin == MonitorSignalOrigin.ImportedWav && mini.DetailStartSeconds == 0, "WAV estático con detalle inicial real");
        mini.Update(silent, [], 0, 1, false, .4); Require(mini.VerticalLimit == 1, "Silencio con escala finita");
        var low = Analyze(weak, 44100); mini.Update(low, [], 0, 1, true, .5);
        Require(mini.DetailSampleCount == 441 && mini.VerticalLimit > 0 && mini.VerticalLimit < 1e-6, "Autoescala de señal débil a 44.100 Hz");
        mini.Update(null, [], 0, 0, false); Require(mini.DetailSampleCount == 0 && mini.Origin is null, "Estado vacío");
        Require(sequence.SequenceEqual(preserved), "Miniatura no modifica muestras");
        view.Show(wavSource); view.StartInspection();
        Wait(() => view.CursorSeconds > 0); Require(view.InspectionRunning && !view.IsLive, "Recorrido automático WAV explícito sin fingir captura o reproducción");
        view.StopInspection(); double stoppedCursor = view.CursorSeconds; Pump(); Require(view.CursorSeconds == stoppedCursor, "Pausa del recorrido");
        var random = new Random(94); var noise = Enumerable.Range(0, 48000 * 30).Select(_ => (float)((random.NextDouble() - .5) * .03)).ToArray();
        object sourceKey = new(); var noiseMap = CarrierEnergyMap.Analyze(new(noise, 48000, MonitorSignalOrigin.ImportedWav, "Cancelación controlada") { SourceId = sourceKey });
        receiverMonitor.Show(noiseMap);
        Invoke("QueueReceptionObservation", noiseMap, sourceKey, true);
        Invoke("ResetReceptionObservation");
        Require(receiverMonitor.ReceivedChannelA.Count == 0, "Un monitor sin recepción explícita no inicia sincronización automática");
        var generatedOnly = Analyze(sequence, 48000);
        Invoke("QueueReceptionObservation", generatedOnly, new object(), true);
        Require(receiverMonitor.ReceivedChannelA.Count == 0, "Salida digital no aporta cabeceras acústicas");
        view.Clear(); Require(view.Data is null, "Limpieza del control");
        view.Show(silent); view.Zoom(0, .001); Pump(); Require(view.Data!.Peak == 0, "Zoom de silencio sin división por cero");
        Invoke("AnalyzeMonitor_Click", window, new RoutedEventArgs());
        window.Close(); Pump(); app.Shutdown();
    }
    catch (Exception ex) { failure = ex; }
    finally { System.IO.File.Delete(path); }
});
thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
if (failure is not null) throw new InvalidOperationException("Monitor WPF", failure);
Console.WriteLine("Monitor real FDM: frecuencias, ambas tasas/perfiles, energía, silencio, bajo nivel, picos, intervalos, cancelación, fuentes, WAV, invalidación y módem intacto: OK.");
static CarrierEnergyMap Analyze(float[] samples, int rate) => CarrierEnergyMap.Analyze(new(samples, rate, MonitorSignalOrigin.Generated, "Señal sintética conocida de comprobación"));
static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static void Pump() { var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false)); Dispatcher.PushFrame(frame); }
static void Wait(Func<bool> ready) { var deadline = DateTime.UtcNow.AddSeconds(10); while (!ready() && DateTime.UtcNow < deadline) { Pump(); Thread.Sleep(10); } Require(ready(), "La preparación asíncrona terminó"); }
