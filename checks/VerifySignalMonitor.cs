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
        var window = new MainWindow { ShowInTaskbar = false, Left = -10000, Width = 1366, Height = 768 }; window.Show();
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
        var generated = view.Data;
        Invoke("InvalidateAudioAnalysis"); Pump(); Require(ReferenceEquals(view.Data, generated), "Cambiar audio no atribuye el buffer generado a la captura");
        Find<ComboBox>("MonitorSourceComboBox").SelectedIndex = 0; Require(view.Data is null, "Invalidación del audio observado");
        Invoke("RefreshCapture"); Wait(() => view.Data is not null);
        foreach (var size in new[] { new Size(1366, 768), new Size(1920, 1080), new Size(1366 / 1.25, 768 / 1.25) })
        {
            window.Width = size.Width; window.Height = size.Height; Pump();
            Require(view.ActualWidth > 650 && view.ActualHeight > 350, "Área estable en resoluciones de escritorio y equivalencia lógica al 125 %");
        }
        window.Width = 1366; window.Height = 768; view.Zoom(0, view.Data!.Signal.Duration); Pump();
        if (args.Length > 0)
        {
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(1366, 768, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); bitmap.Render(window);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var output = System.IO.File.Create(args[0]); encoder.Save(output);
        }
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
