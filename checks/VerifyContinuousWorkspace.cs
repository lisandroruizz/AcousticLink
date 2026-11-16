#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property PublishAot=false
#:project ../AcousticLink.App/AcousticLink.App.csproj
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Threading.Channels;
using System.Collections.ObjectModel;
using AcousticLink.App;
using AcousticLink.App.Audio;
using AcousticLink.App.Protocol;
using AcousticLink.App.Signals;
using AcousticLink.App.Visualization;
Exception? failure = null;
var thread = new Thread(() =>
{
    try
    {
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown }; app.InitializeComponent();
        var w = new MainWindow { ShowInTaskbar = false, Left = -10000 }; // Aislar la prueba de la preferencia personal; no escribirla ni abrir el micrófono.
        ((System.Windows.Controls.CheckBox)w.FindName("AutoListenCheckBox")).IsChecked = false;
        w.Show();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        const BindingFlags f = BindingFlags.NonPublic | BindingFlags.Instance;
        object Field(string n) => typeof(MainWindow).GetField(n, f)!.GetValue(w)!;
        object? Call(string n, params object[] args) => typeof(MainWindow).GetMethod(n, f)!.Invoke(w, args);
        ((DispatcherTimer)Field("captureTimer")).Stop(); ((DispatcherTimer)Field("monitorTimer")).Stop();
        var type = typeof(MainWindow).GetNestedType("ListeningSession", f)!;
        var session = Activator.CreateInstance(type, true)!;
        typeof(MainWindow).GetField("continuousListening", f)!.SetValue(w, session);
        var worker = (Task)Call("ListenWorkerAsync", session, false, false)!;
        type.GetField("Worker")!.SetValue(session, worker);
        var capture = (AudioCaptureService)Field("capture");
        typeof(AudioCaptureService).GetField("running", f)!.SetValue(capture, true); // dispositivo simulado, no hardware
        type.GetField("MicrophoneStarted")!.SetValue(session, true);
        ((DispatcherTimer)Field("captureTimer")).Start(); // El timer consume la última instantánea, sin bloquear el worker.
        Call("RefreshCapture");
        if (args.Length > 0)
        {
            w.Width = 1366; w.Height = 768;
            ((TabControl)w.FindName("MainSectionsTabs")).SelectedIndex = 2; Pump(Task.Delay(50));
            Snapshot(w, System.IO.Path.Combine(args[0], "estudio-escucha-sintetica.png"));
        }
        Require(!((Button)w.FindName("StartCaptureButton")).IsEnabled && !((Button)w.FindName("LoadWavButton")).IsEnabled, "Conflicto de micrófono bloqueado explícitamente");
        Call("StartCapture_Click", w, new RoutedEventArgs());
        Require(((TextBlock)w.FindName("CaptureStatusText")).Text.Contains("escucha continua"), "No abrir segundo recorder");
        var history = (ObservableCollection<ContinuousReception>)Field("continuousHistory");
        var sender = new FdmBitstreamGenerator();
        void Feed(string a, string b)
        {
            var audio = sender.GenerateTextFrames(a, b).Concat(new float[48000]).ToArray();
            for (int at = 0; at < audio.Length; at += 4800)
            {
                var chunk = audio.AsSpan(at, Math.Min(4800, audio.Length - at)).ToArray();
                Call("EnqueueContinuousSamples", session, chunk, 48000);
                Wait(() => (long)type.GetField("AcceptedSamples")!.GetValue(session)! - (long)type.GetField("ConsumedSamples")!.GetValue(session)! < 12 * 4800);
            }
        }
        Feed("Hola", "Mundo"); Wait(() => history.Count == 1);
        var monitor = (SignalMonitorView)w.FindName("ReceiverSignalMonitor");
        ((ComboBox)w.FindName("ContinuousHistoryComboBox")).SelectedIndex = 0; Wait(() => ReferenceEquals(monitor.Data?.Signal.SourceId, history[0]));
        if (args.Length > 0) Snapshot(w, System.IO.Path.Combine(args[0], "estudio-historico-sintetico.png"));
        var selected = monitor.Data;
        ((TabControl)w.FindName("MainSectionsTabs")).SelectedIndex = 1;
        Feed("Prueba", "Datos"); Wait(() => history.Count == 2);
        Require(ReferenceEquals(selected, monitor.Data) && ReferenceEquals(Field("continuousListening"), session), "Cambiar pestaña/seleccionar histórico no detiene ni reemplaza inspección");
        Call("ContinuousLive_Click", w, new RoutedEventArgs());
        Feed("Hola", "Mundo"); Wait(() => history.Count == 3);
        Require(history[2].Result.ChannelA.Text == "Hola" && history[2].Id != history[0].Id, "Repetición registrada sin reiniciar escucha");
        typeof(AudioCaptureService).GetField("running", f)!.SetValue(capture, false);
        Pump((Task)Call("StopContinuousListeningAsync", false)!);
        Require(typeof(MainWindow).GetField("continuousListening", f)!.GetValue(w) is null, "Worker y sesión detenidos");
        // Un error de dispositivo debe terminar la sesión, liberar el worker y permitir reintentar.
        var deviceSession = Activator.CreateInstance(type, true)!;
        typeof(MainWindow).GetField("continuousListening", f)!.SetValue(w, deviceSession);
        var deviceWorker = (Task)Call("ListenWorkerAsync", deviceSession, false, false)!;
        type.GetField("Worker")!.SetValue(deviceSession, deviceWorker);
        type.GetField("MicrophoneStarted")!.SetValue(deviceSession, true);
        typeof(AudioCaptureService).GetField("error", f)!.SetValue(capture, "Desconexión simulada de dispositivo");
        Call("RefreshCapture");
        Wait(() => typeof(MainWindow).GetField("continuousListening", f)!.GetValue(w) is null);
        Require(deviceWorker.IsCompleted && ((TextBlock)w.FindName("ContinuousStatusText")).Text.Contains("Desconexión simulada")
            && ((Button)w.FindName("ContinuousListenButton")).IsEnabled, "Error visible, sin worker retenido y reintento habilitado");
        if (args.Length > 0)
        {
            ((TabControl)w.FindName("MainSectionsTabs")).SelectedIndex = 2;
            ((TabControl)w.FindName("ReceiverInspectorTabs")).SelectedIndex = 0; Pump(Task.Delay(50));
            Snapshot(w, System.IO.Path.Combine(args[0], "estudio-error-dispositivo-simulado.png"));
        }
        typeof(AudioCaptureService).GetField("error", f)!.SetValue(capture, null);
        // Reiniciar una sesión y cerrar mientras otra petición de parada sigue pendiente.
        var restarted = Activator.CreateInstance(type, true)!;
        typeof(MainWindow).GetField("continuousListening", f)!.SetValue(w, restarted);
        var secondWorker = (Task)Call("ListenWorkerAsync", restarted, true, true)!;
        type.GetField("Worker")!.SetValue(restarted, secondWorker);
        Call("EnqueueContinuousSamples", restarted, sender.GenerateTextFrames("a", "b", true)[..4800], 48000);
        var firstStop = (Task)Call("StopContinuousListeningAsync", false)!;
        var sameStop = (Task)Call("StopContinuousListeningAsync", false)!;
        Require(ReferenceEquals(firstStop, sameStop), "Parada concurrente comparte limpieza, no retorna prematuramente");
        w.Close(); Wait(() => !w.IsVisible);
        Require(secondWorker.IsCompleted && firstStop.IsCompleted && typeof(MainWindow).GetField("continuousListening", f)!.GetValue(w) is null,
            "Reinicio/cierre espera worker sin publicar sesión obsoleta");
        app.Shutdown();
    }
    catch (Exception ex) { failure = ex; }
});
thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
Console.WriteLine("WPF continuo sintético: tres transmisiones, histórico, pestañas, conflicto manual, parada compartida, reinicio y cierre activo: OK");
static void Wait(Func<bool> check) { var end = DateTime.UtcNow.AddSeconds(15); while (!check() && DateTime.UtcNow < end) Pump(Task.Delay(10)); if (!check()) throw new TimeoutException(); }
static void Pump(Task task) { var end = DateTime.UtcNow.AddSeconds(15); while (!task.IsCompleted && DateTime.UtcNow < end) { var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false)); Dispatcher.PushFrame(frame); Thread.Sleep(1); } if (!task.IsCompleted) throw new TimeoutException(); task.GetAwaiter().GetResult(); }
static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
static void Snapshot(Window window, string path)
{
    window.UpdateLayout();
    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
    bitmap.Render(window);
    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
    using var file = System.IO.File.Create(path); encoder.Save(file);
}
