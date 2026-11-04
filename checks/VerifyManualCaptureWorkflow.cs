#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property PublishAot=false
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
    string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"manual-workflow-{Guid.NewGuid()}.wav");
    try
    {
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown }; app.InitializeComponent();
        var window = new MainWindow { Width = 1366, Height = 768, ShowInTaskbar = false, Left = -10000 }; // Aislar la prueba de la preferencia personal; no escribirla ni abrir el micrófono.
        ((System.Windows.Controls.CheckBox)window.FindName("AutoListenCheckBox")).IsChecked = false;
        window.Show();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        object Field(string name) => typeof(MainWindow).GetField(name, flags)!.GetValue(window)!;
        object? Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        ((DispatcherTimer)Field("captureTimer")).Stop(); ((DispatcherTimer)Field("monitorTimer")).Stop();
        var capture = (AudioCaptureService)Field("capture");
        var selection = (AudioRecordingSelection)Field("audioSelection");
        var audio = new CapturedAudio(48000, 1, 30);
        selection.SelectMicrophone(audio, "Dispositivo simulado: buffers sintéticos, sin hardware");
        typeof(AudioCaptureService).GetField("recording", flags)!.SetValue(capture, audio);
        typeof(AudioCaptureService).GetField("running", flags)!.SetValue(capture, true);
        ((Wpf.Ui.Controls.ToggleSwitch)window.FindName("DecodeDuringCaptureCheckBox")).IsChecked = false;
        ((TabControl)window.FindName("MainSectionsTabs")).SelectedIndex = 2;
        ((TabControl)window.FindName("ReceiverTools")).SelectedIndex = 2;
        var monitor = (SignalMonitorView)window.FindName("ReceiverSignalMonitor");
        var lab = (ReceiverInterpretationView)typeof(SignalMonitorView).GetField("interpretation", flags)!.GetValue(monitor)!;
        var tools = (TabControl)typeof(SignalMonitorView).GetField("signalTools", flags)!.GetValue(monitor)!; tools.SelectedIndex = 2;
        var data = new FdmBitstreamGenerator().GenerateTextFrames("Hola", "Mundo");
        int beats = 0; var heartbeat = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) }; heartbeat.Tick += (_, _) => beats++; heartbeat.Start();
        for (int offset = 0; offset < data.Length;)
        {
            int count = Math.Min(48000, data.Length - offset); var block = data.AsSpan(offset, count).ToArray();
            Pump(Task.Run(() => audio.Append(block, block.Length))); offset += count;
            Call("RefreshCapture"); Pump((Task)Call("RefreshStreamingMonitorAsync")!); Pump(Task.Delay(15));
            Require(capture.IsRecording && monitor.Data?.Signal.Samples.Length == offset, "Captura manual y onda avanzan sin receptor incremental");
            Require(monitor.ReceptionActivity is { ProcessedSamples: 0 } && monitor.ReceivedChannelA.Count == 0 && lab.Selection is null, "Manual no sincroniza cabeceras ocultas ni mezcla estados");
            bool input = false; window.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => input = true)); Pump(Task.Delay(15));
            Require(input && ((Button)window.FindName("StopCaptureButton")).IsEnabled, "Dispatcher responde y permite detener antes del límite");
        }
        heartbeat.Stop(); Require(beats > 5, "Heartbeat UI durante captura manual");
        typeof(AudioCaptureService).GetField("running", flags)!.SetValue(capture, false); audio.Complete(CaptureStopReason.Manual);
        Call("StopCapture_Click", window, new RoutedEventArgs()); Pump((Task)Call("RefreshStreamingMonitorAsync")!);
        audio.SaveWav(path); var roundtrip = new AudioRecordingSelection(); roundtrip.ImportWav(path); Require(roundtrip.Current!.Audio.GetSamples().SequenceEqual(data), "Guardar WAV no altera muestras");
        Call("ReceiveFdmText_Click", window, new RoutedEventArgs());
        Wait(() => !(bool)Field("analyzing"));
        var actual = ((ReceptionView)window.FindName("ReceptionAView")).Result!;
        var expected = new FdmTextReceiver().Receive(data, 48000);
        Require(actual.Text == "Hola" && actual.Block!.Bits == expected.ChannelA.Block!.Bits && ((ReceptionView)window.FindName("ReceptionBView")).Result?.Text == "Mundo", "Recibir mensajes conserva resultado manual anterior");
        selection.ImportWav(path); var imported = selection.Current!; Call("InvalidateAudioAnalysis");
        var wav = CarrierEnergyMap.Analyze(new(imported.Audio.GetSamples(), 48000, MonitorSignalOrigin.ImportedWav, "WAV sintético") { SourceId = imported.Audio }); monitor.Show(wav);
        Call("QueueReceptionObservation", wav, imported.Audio, true); Require(monitor.ReceivedChannelA.Count == 0, "Abrir/recorrer WAV no decodifica automáticamente");
        monitor.StartInspection(); Pump(Task.Delay(120)); Require(monitor.InspectionRunning && !monitor.IsLive && monitor.CursorSeconds > 0, "Recorrer WAV permanece disponible sin captura"); monitor.StopInspection();
        Call("ReceiveFdmText_Click", window, new RoutedEventArgs()); Wait(() => !(bool)Field("analyzing"));
        monitor.SetCursor(3.25); Require(lab.Selection is not null && lab.WindowObservation?.AnalysisSampleCount == 2880, "Símbolo y ventana central coherentes después de análisis manual");
        typeof(AudioCaptureService).GetField("session", flags)!.SetValue(capture, Task.FromException(new InvalidOperationException("fallo de parada inyectado")));
        Pump((Task)Call("StopCaptureSafelyAsync")!);
        Require(!(bool)Field("captureBusy") && ((Button)window.FindName("StartCaptureButton")).IsEnabled && ((TextBlock)window.FindName("CaptureStatusText")).Text.Contains("fallo de parada inyectado"), "Error de parada se informa y finally restaura controles");
        typeof(AudioCaptureService).GetField("session", flags)!.SetValue(capture, Task.CompletedTask);
        window.Close(); Wait(() => !window.IsVisible); app.Shutdown();
    }
    catch (Exception ex) { failure = ex; }
    finally { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
});
thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
Console.WriteLine("Captura manual simulada: muestras/onda, heartbeat/input UI, parada, WAV, recepción explícita, inspección histórica y cierre: OK; sin hardware físico");
static void Wait(Func<bool> condition) { var end = DateTime.UtcNow.AddSeconds(15); while (!condition() && DateTime.UtcNow < end) Pump(Task.Delay(10)); if (!condition()) throw new TimeoutException(); }
static void Pump(Task task) { var end = DateTime.UtcNow.AddSeconds(15); while (!task.IsCompleted && DateTime.UtcNow < end) { var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false)); Dispatcher.PushFrame(frame); Thread.Sleep(1); } if (!task.IsCompleted) throw new TimeoutException(); task.GetAwaiter().GetResult(); }
static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
