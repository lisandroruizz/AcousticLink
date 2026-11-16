#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property PublishAot=false
#:project ../AcousticLink.App/AcousticLink.App.csproj
using System.Reflection;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Threading;
using AcousticLink.App;
using AcousticLink.App.Protocol;
using AcousticLink.App.Audio;
using AcousticLink.App.Signals;
using System.Diagnostics;
using System.Windows.Controls;

Exception? failure = null;
var thread = new Thread(() =>
{
    MainWindow? window = null;
    object? session = null;
    Task worker = Task.CompletedTask;
    try
    {
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown }; app.InitializeComponent();
        window = new MainWindow(); // No Show: no autoarranque ni dispositivo físico.
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        const BindingFlags f = BindingFlags.NonPublic | BindingFlags.Instance;
        foreach (string timer in new[] { "captureTimer", "monitorTimer" })
            ((DispatcherTimer)typeof(MainWindow).GetField(timer, f)!.GetValue(window)!).Stop();
        var type = typeof(MainWindow).GetNestedType("ListeningSession", f)!;
        session = Activator.CreateInstance(type, true)!;
        typeof(MainWindow).GetField("continuousListening", f)!.SetValue(window, session);
        var queue = type.GetField("Queue")!.GetValue(session)!;
        int QueueCount() => (int)queue.GetType().GetProperty("Reader")!.GetValue(queue)!.GetType().GetProperty("Count")!.GetValue(queue.GetType().GetProperty("Reader")!.GetValue(queue)!)!;
        worker = (Task)typeof(MainWindow).GetMethod("ListenWorkerAsync", f)!.Invoke(window, [session, false, false])!;
        type.GetField("Worker")!.SetValue(session, worker);
        object? Call(string name, params object[] args)
        {
            try { return typeof(MainWindow).GetMethod(name, f)!.Invoke(window, args); }
            catch (TargetInvocationException ex) when (ex.InnerException is not null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
        }
        long Counter(string name) => (long)type.GetField(name)!.GetValue(session)!;
        void Block(float[] values)
        {
            Require(SpinWait.SpinUntil(() => QueueCount() < 12, 10000), "El consumidor avanza sin Dispatcher");
            Call("EnqueueContinuousSamples", session!, values, 48000);
        }
        void Feed(float[] audio)
        {
            for (int at = 0; at < audio.Length; at += 4800) Block(audio.AsSpan(at, Math.Min(4800, audio.Length - at)).ToArray());
            Require(SpinWait.SpinUntil(() => Counter("ConsumedSamples") == Counter("AcceptedSamples"), 10000), "Todos los bloques aceptados se procesan");
        }
        for (int i = 0; i < 8; i++) Block(new float[4800]);
        // Simular UI ocupada: el worker debe consumir sin que se procese un solo mensaje WPF.
        bool advanced = SpinWait.SpinUntil(() => ((ContinuousFdmReceiver?)type.GetField("Receiver")!.GetValue(session))?.ReceivedSamples >= 38400, 2000);
        long count = ((ContinuousFdmReceiver?)type.GetField("Receiver")!.GetValue(session))?.ReceivedSamples ?? 0;
        Console.WriteLine($"Sin bombear Dispatcher: consumidas {count}/38400 muestras");
        Require(advanced, "El worker queda esperando la publicación gráfica en el Dispatcher");
        Require(Counter("InputSamples") == 38400 && Counter("AcceptedSamples") == 38400 && Counter("ConsumedSamples") == 38400, "Contadores distintos, íntegros");

        var watch = Stopwatch.StartNew();
        long memoryBefore = GC.GetTotalMemory(true);
        var silence = new float[4800]; var random = new Random(44);
        var noise = Enumerable.Range(0, 4800).Select(_ => (float)((random.NextDouble() - .5) * .03)).ToArray();
        // Cinco minutos acelerados: sin esperas por tiempo de audio y sin bombear WPF.
        for (int i = 0; i < 3000; i++) Block(i % 3 == 0 ? noise : silence);
        Require(SpinWait.SpinUntil(() => Counter("ConsumedSamples") == Counter("AcceptedSamples"), 10000), "Progreso sin cabecera durante cinco minutos");
        var receiver = (ContinuousFdmReceiver)type.GetField("Receiver")!.GetValue(session)!;
        Require(receiver.ReceptionCount == 0 && receiver.BufferedSamples <= 48000 * 5 && receiver.Capacity == 48000 * 30, "Sin falsas recepciones y retención acotada");
        Require(receiver.EvaluatedHeaderStarts < receiver.ReceivedSamples / 48000.0 * 120, "Búsqueda acotada, sin recorrer todos los minutos anteriores");
        Require(Counter("LastUi") == 0 && Counter("LastProgress") > 0, "Worker avanza mientras la publicación WPF no avanza");
        long retained = GC.GetTotalMemory(true) - memoryBefore;
        Require(retained < 64L * 1024 * 1024, "Sin crecimiento acumulado de audio/minutos ni de trabajos WPF");
        Console.WriteLine($"300 s sin cabecera: {Counter("ConsumedSamples")} muestras, buffer {receiver.BufferedSamples}, {watch.Elapsed.TotalSeconds:0.00} s de reloj de prueba, delta RAM administrada {retained / 1048576.0:0.0} MiB");

        var capture = (AudioCaptureService)typeof(MainWindow).GetField("capture", f)!.GetValue(window)!;
        typeof(AudioCaptureService).GetField("running", f)!.SetValue(capture, true); // Estado de dispositivo simulado, sin micrófono.
        type.GetField("MicrophoneStarted")!.SetValue(session, true);
        Call("RefreshCapture");
        Require(((TextBlock)window.FindName("LiveCaptureText")).Text.Contains("recibido") && ((TextBlock)window.FindName("AudioSourceText")).Text.Contains("Micrófono"), "Sin mensajes contradictorios sobre captura/fuente");
        Require(Counter("LastUi") > 0 && ((TextBox)window.FindName("ContinuousDiagnosticsText")).Text.Contains("Cola:"), "Diagnóstico distingue publicación de consumo");
        var sender = new FdmBitstreamGenerator();
        for (int i = 0; i < 3; i++) { Feed(sender.GenerateTextFrames("Hola", "Mundo")[..96000]); Feed(new float[48000]); }
        Require(receiver.History.Count == 0, "Cabeceras parciales repetidas no se confirman ni bloquean la búsqueda");
        Feed(sender.GenerateTextFrames("Hola", "Mundo")); Call("RefreshCapture");
        Feed(sender.GenerateTextFrames("Hola", "Mundo")); Call("RefreshCapture");
        Require(receiver.History.Count == 2 && receiver.History.All(x => x.Result.ChannelA.Text == "Hola" && x.Result.ChannelB.Text == "Mundo"), "Dos transmisiones después de silencio prolongado sin reiniciar");
        for (int i = 0; i < 11; i++) Feed(sender.GenerateTextFrames("a", "b")); // WPF ocupado: solo retener las diez últimas.
        Call("RefreshCapture");
        var history = (System.Collections.ObjectModel.ObservableCollection<ContinuousReception>)typeof(MainWindow).GetField("continuousHistory", f)!.GetValue(window)!;
        Require(history.Count == 10 && history.Last().Id == receiver.ReceptionCount && history.Select(x => x.Id).Distinct().Count() == 10,
            "Publicaciones coalescidas mantienen las diez últimas recepciones, incluida la más nueva");
        // Parada durante datos: no drenar a ciegas ni esperar a un gráfico.
        Feed(sender.GenerateTextFrames("Hola", "Mundo")[..(48000 * 4)]);
        typeof(AudioCaptureService).GetField("running", f)!.SetValue(capture, false);
        var stopped = (Task)Call("StopContinuousListeningAsync", false)!;
        Require(SpinWait.SpinUntil(() => worker.IsCompleted, 2000), "Worker cancelado sin bombear Dispatcher");
        Pump(stopped);
        Require(typeof(MainWindow).GetField("continuousListening", f)!.GetValue(window) is null, "Parada libera sesión");

        // Productor más rápido que un consumidor detenido: error explícito, jamás pérdida silenciosa.
        session = Activator.CreateInstance(type, true)!;
        typeof(MainWindow).GetField("continuousListening", f)!.SetValue(window, session);
        queue = type.GetField("Queue")!.GetValue(session)!;
        for (int i = 0; i < 16; i++) Call("EnqueueContinuousSamples", session, silence, 48000);
        Call("EnqueueContinuousSamples", session, Array.Empty<float>(), 48000);
        Require(Counter("EmptyBlocks") == 1 && Counter("RejectedBlocks") == 0 && QueueCount() == 16,
            "Un callback sin muestras no consume cola ni dispara sobrecarga");
        try { Call("EnqueueContinuousSamples", session, silence, 48000); throw new Exception("No detectó saturación"); }
        catch (InvalidOperationException) { }
        Require(QueueCount() == 16 && Counter("AcceptedSamples") == 16 * 4800 && Counter("InputSamples") == 17 * 4800 && Counter("ConsumedSamples") == 0,
            "Captura crece, worker detenido, cola acotada y bloque rechazado contabilizado");
        Require(((CancellationTokenSource)type.GetField("Cancellation")!.GetValue(session)!).IsCancellationRequested
            && ((string)type.GetField("LastError")!.GetValue(session)!).Contains("Sobrecarga"), "Sobrecarga cancela y conserva el error");
        var incident = type.GetField("Incident")!.GetValue(session)!;
        Require((string)incident.GetType().GetProperty("Reason")!.GetValue(incident)! == "Cola llena (TryWrite rechazó)"
            && (int)incident.GetType().GetProperty("BlockSamples")!.GetValue(incident)! == 4800
            && Counter("MaxQueue") == 16, "Motivo exacto, bloque y ocupación máxima retenidos");
        Call("EnqueueContinuousSamples", session, silence, 48000);
        Require(ReferenceEquals(incident, type.GetField("Incident")!.GetValue(session)) && Counter("RejectedBlocks") == 2,
            "El primer incidente permanece intacto ante callbacks posteriores");
        Pump((Task)Call("StopContinuousListeningAsync", true)!);
        Require(((TextBlock)window.FindName("ContinuousStatusText")).Text.Contains("Sobrecarga"), "La parada no oculta el error original con un mensaje de éxito");
        Require(((TextBox)window.FindName("ContinuousDiagnosticsText")).Text.Contains("TryWrite rechazó")
            && ((TextBox)window.FindName("ContinuousDiagnosticsText")).Text.Contains("Append receptor:"),
            "El diagnóstico del incidente sigue disponible tras liberar la sesión");
        session = Activator.CreateInstance(type, true)!;
        typeof(MainWindow).GetField("continuousListening", f)!.SetValue(window, session);
        try { Call("EnqueueContinuousSamples", session, new float[48001], 48000); throw new Exception("No rechazó paquete mayor a un segundo"); }
        catch (InvalidOperationException) { }
        incident = type.GetField("Incident")!.GetValue(session)!;
        Require((string)incident.GetType().GetProperty("Reason")!.GetValue(incident)! == "Bloque superior a un segundo"
            && (int)incident.GetType().GetProperty("BlockSamples")!.GetValue(incident)! == 48001,
            "Bloque sobredimensionado distinguido de cola llena");
        Pump((Task)Call("StopContinuousListeningAsync", true)!);
        session = null; // El cierre también queda cubierto por VerifyContinuousWorkspace.
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { Pump(capture.StartContinuousAsync((_, _) => throw new Exception("No debe abrir dispositivo"), cancelled.Token)); throw new Exception("No canceló inicio"); }
        catch (OperationCanceledException) { }
    }
    catch (Exception ex) { failure = ex; }
    finally
    {
        if (session is not null) ((CancellationTokenSource)session.GetType().GetField("Cancellation")!.GetValue(session)!).Cancel();
        Pump(worker); window?.Close(); Pump(Task.Delay(30));
    }
});
thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
Console.WriteLine("Worker continuo independiente del Dispatcher: OK (audio sintético)");
static void Require(bool condition, string text) { if (!condition) throw new InvalidOperationException(text); }
static void Pump(Task task)
{
    var limit = DateTime.UtcNow.AddSeconds(5);
    while (!task.IsCompleted && DateTime.UtcNow < limit)
    {
        var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame); Thread.Sleep(1);
    }
    if (!task.IsCompleted) throw new TimeoutException("Worker sin finalizar");
    task.GetAwaiter().GetResult();
}
