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
using AcousticLink.App.Protocol;
using AcousticLink.App.Signals;
using AcousticLink.App.Visualization;

Exception? failure = null;
var thread = new Thread(() =>
{
    try
    {
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown }; app.InitializeComponent();
        var window = new MainWindow { Width = 1366, Height = 768, ShowInTaskbar = false, Left = -10000, Top = -10000 }; // Aislar la prueba de la preferencia personal; no escribirla ni abrir el micrófono.
        ((System.Windows.Controls.CheckBox)window.FindName("AutoListenCheckBox")).IsChecked = false;
        window.Show();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        object? Field(string name) => typeof(MainWindow).GetField(name, flags)!.GetValue(window);
        object? Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        ((DispatcherTimer)Field("captureTimer")!).Stop(); ((DispatcherTimer)Field("monitorTimer")!).Stop();
        Require(((Wpf.Ui.Controls.ToggleSwitch)window.FindName("DecodeDuringCaptureCheckBox")).IsChecked == false, "Modalidad manual predeterminada");
        ((Wpf.Ui.Controls.ToggleSwitch)window.FindName("UseHammingReceiveCheckBox")).IsChecked = true;
        ((Wpf.Ui.Controls.ToggleSwitch)window.FindName("RecoverReceiveErasuresCheckBox")).IsChecked = true;
        var selection = (AudioRecordingSelection)Field("audioSelection")!;
        var capture = (AudioCaptureService)Field("capture")!;
        // Simular únicamente la fuente de callbacks del dispositivo; no abrir un micrófono físico.
        void CaptureState(CapturedAudio audio, bool active)
        {
            typeof(AudioCaptureService).GetField("recording", flags)!.SetValue(capture, audio);
            typeof(AudioCaptureService).GetField("running", flags)!.SetValue(capture, active);
        }
        var audio = new CapturedAudio(48000, 1, 30); selection.SelectMicrophone(audio, "Captura sintética de prueba, sin hardware");
        CaptureState(audio, true);
        ((Wpf.Ui.Controls.ToggleSwitch)window.FindName("DecodeDuringCaptureCheckBox")).IsChecked = true;
        Call("StartLiveReception", audio);
        object session = Field("liveReception")!;
        var samples = new FdmBitstreamGenerator().GenerateTextFrames("Hola", "Mundo", true);
        void Process(bool final)
        {
            CaptureState(audio, !final);
            // Recorrer la integración del timer real, no llamar directamente al worker.
            Call("QueueLiveReception");
            Pump((Task)session.GetType().GetField("Pending")!.GetValue(session)!);
        }
        audio.Append(samples[..(48000 * 5)], 48000 * 5); Process(false);
        var a = (ReceptionView)window.FindName("ReceptionAView");
        Require(a.Result is { HeaderValid: true, IsReceiving: true, ByteLength: 4, CrcValid: null }, "La UI publica longitud y datos parciales reales");
        Require(((SignalMonitorView)window.FindName("ReceiverSignalMonitor")).Data is null, "El worker de recepción no depende del gráfico ni de copiar su audio: " + ((SignalMonitorView)window.FindName("ReceiverSignalMonitor")).Data?.Signal.Description);
        Call("RefreshCapture");
        var inspector = (ReceptionProgressView)window.FindName("LiveReceptionInspector");
        var monitor = (SignalMonitorView)window.FindName("ReceiverSignalMonitor");
        Require(inspector.Activity!.Mode == "FDM · CAPTURANDO · DECODIFICACIÓN EN VIVO" && inspector.Activity.ProcessedSamples == 48000 * 5 && inspector.Activity.ChannelA.HammingWords > 2 && inspector.Activity.ChannelA.CrcValid is null, "La captura sigue activa y el estado visible ya contiene longitud y palabras procesadas");
        var energy = new IncrementalSignalMonitor(48000, audio.SampleCapacity); energy.Append(samples.AsSpan(0, 48000 * 5));
        var map = energy.Snapshot(MonitorSignalOrigin.Microphone, "Captura sintética; no hardware"); map = map.WithSignal(map.Signal with { SourceId = audio });
        monitor.BeginLive("Prueba sintética"); monitor.UpdateLive(map); Call("QueueReceptionObservation", map, audio, false);
        var lab = (ReceiverInterpretationView)typeof(SignalMonitorView).GetField("interpretation", flags)!.GetValue(monitor)!;
        Require(lab.Selection?.Trace is not null && lab.WindowObservation is { SampleCount: 4800, AnalysisSampleCount: 2880 }, "El seguimiento al final del audio muestra el último símbolo completo, no una ventana exploratoria");
        Require(monitor.ReceptionActivity?.ChannelA.AvailableBits > 0 && monitor.ReceptionActivity.ChannelA.CrcValid is null, "El monitor muestra progreso independiente del mapa y CRC no anticipado");
        monitor.SuspendFollowing(); monitor.SetCursor(3.25); int selected = lab.Selection!.Trace!.Number;
        ((System.Windows.Controls.TabControl)window.FindName("MainSectionsTabs")).SelectedIndex = 2;
        ((System.Windows.Controls.TabControl)window.FindName("ReceiverTools")).SelectedIndex = 2;
        Require(((System.Windows.Controls.Primitives.ToggleButton)window.FindName("ReceiverInspectorToggle")).IsChecked == true, "Abrir el monitor conserva visible el inspector y su configuración");
        var internalTools = (System.Windows.Controls.TabControl)typeof(SignalMonitorView).GetField("signalTools", flags)!.GetValue(monitor)!;
        internalTools.SelectedIndex = 2; Pump(Task.Delay(250));
        if (args.Length > 0) Snapshot(window, System.IO.Path.Combine(args[0], "receptor-en-vivo.png"));
        audio.Append(samples[(48000 * 5)..], samples.Length - 48000 * 5); Process(false);
        energy.Append(samples.AsSpan(48000 * 5)); map = energy.Snapshot(MonitorSignalOrigin.Microphone, "Captura sintética; no hardware"); map = map.WithSignal(map.Signal with { SourceId = audio });
        monitor.UpdateLive(map); Call("QueueReceptionObservation", map, audio, false);
        Require(lab.Selection!.Trace!.Number == selected && !monitor.FollowingCapture, "Pausar seguimiento conserva selección y no detiene la decodificación");
        monitor.FollowCapture(); monitor.UpdateLive(map); Require(lab.Selection?.Trace is not null, "Volver al vivo muestra la última decisión real");
        Require(a.Result?.Text == "Hola" && ((ReceptionView)window.FindName("ReceptionBView")).Result?.Text == "Mundo" && audio.GetMetrics().StopReason == CaptureStopReason.None, "Mensajes válidos antes de finalizar la captura sintética");
        audio.Complete(CaptureStopReason.Manual); Process(true);
        monitor.FinishLive(map);
        Require(lab.Selection?.Trace is not null, "Finalizar conserva seleccionado el último símbolo histórico");
        monitor.Show(map.WithSignal(map.Signal with { Samples = samples.ToArray() }));
        Require(lab.Selection?.Trace is not null, "Publicar el buffer final de la misma fuente no borra símbolos ni selección");
        Require(a.Result?.IsReceiving == false && a.Result?.CrcValid == true && inspector.Activity!.Mode == "FDM · CAPTURA FINALIZADA · RESULTADOS INCREMENTALES RETENIDOS", "Cierre sin pérdida y cambio real de modo");
        var labChannel = (ComboBox)typeof(ReceiverInterpretationView).GetField("channel", flags)!.GetValue(lab)!;
        labChannel.SelectedIndex = 1; monitor.SetCursor(10.95);
        var group = (System.Windows.Controls.WrapPanel)typeof(ReceiverInterpretationView).GetField("wordBits", flags)!.GetValue(lab)!;
        var nibble = (System.Windows.Controls.TextBlock)typeof(ReceiverInterpretationView).GetField("decoded", flags)!.GetValue(lab)!;
        var wordSelector = (ComboBox)typeof(ReceiverInterpretationView).GetField("words", flags)!.GetValue(lab)!;
        Require(group.Children.Count == 7 && wordSelector.SelectedItem is not null && nibble.Text.StartsWith("Nibble decodificado:"), "Columna 3 conserva palabra seleccionada, siete posiciones y nibble reales");
        var timing = (System.Windows.Controls.TextBlock)typeof(ReceiverInterpretationView).GetField("window", flags)!.GetValue(lab)!;
        var measured = lab.WindowObservation!;
        string symbolStart = (measured.StartSample / (double)measured.SampleRate).ToString("0.000000");
        Require(timing.Text.Contains(symbolStart) && timing.ToolTip!.ToString()!.Contains(symbolStart), "Tiempo visible y ToolTip corresponden al mismo símbolo completo");
        // Un worker anterior no puede publicar después de seleccionar otra captura.
        var next = new CapturedAudio(48000, 1, 30); CaptureState(next, true); selection.SelectMicrophone(next, "Nueva sesión sintética");
        Call("InvalidateAudioAnalysis"); Call("StartLiveReception", next);
        session = Field("liveReception")!;
        next.Append(samples[..(48000 * 3)], 48000 * 3);
        var pending = (Task)Call("ProcessLiveReceptionAsync", session, next.GetStatus().Count, false)!;
        session.GetType().GetField("Pending")!.SetValue(session, pending);
        Call("InvalidateAudioAnalysis"); Pump(pending);
        Require(a.Result is null && Field("liveReception") is null, "Invalidación y rechazo de publicación tardía");
        Call("StartLiveReception", next); Call("CancelLiveReception");
        Require(((System.Windows.Controls.TextBlock)window.FindName("LiveReceptionStatusText")).Text.Contains("cancelada"), "Cancelación esperada, sin error funcional");
        CaptureState(next, false);
        window.Close();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (window.IsVisible && DateTime.UtcNow < deadline) Pump(Task.Delay(10));
        Require(!window.IsVisible, "Cierre seguro después de cancelar");
        app.Shutdown();
    }
    catch (Exception ex) { failure = ex; }
});
thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
Console.WriteLine("WPF con muestras sintéticas: progreso, validación durante captura, invalidación, cancelación y cierre: OK (sin micrófono físico)");
static void Pump(Task task)
{
    var deadline = DateTime.UtcNow.AddSeconds(15);
    while (!task.IsCompleted && DateTime.UtcNow < deadline)
    {
        var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame); Thread.Sleep(2);
    }
    if (!task.IsCompleted) throw new TimeoutException("Worker WPF sin finalizar");
    task.GetAwaiter().GetResult();
}
static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

static void Snapshot(Window window, string path)
{
    window.UpdateLayout();
    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
    bitmap.Render(window); var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
    using var output = System.IO.File.Create(path); encoder.Save(output);
}
