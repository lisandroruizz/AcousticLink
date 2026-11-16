#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property PublishAot=false
#:project ../AcousticLink.App/AcousticLink.App.csproj
using System.Diagnostics;
using System.Reflection;
using System.Windows;
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
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        var window = new MainWindow(); // Sin Show ni dispositivo físico.
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        foreach (string name in new[] { "captureTimer", "monitorTimer" })
            ((DispatcherTimer)typeof(MainWindow).GetField(name, flags)!.GetValue(window)!).Stop();
        var type = typeof(MainWindow).GetNestedType("ListeningSession", flags)!;
        var capture = (AudioCaptureService)typeof(MainWindow).GetField("capture", flags)!.GetValue(window)!;
        object? Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        void Run(string name, bool bfsk, float[] audio, bool refreshUi, long expectedReceipts,
            int blockSize = 4800, int cadenceMs = 100, bool burst = false, bool comparisonLoad = false)
        {
            var session = Activator.CreateInstance(type, true)!;
            type.GetField("IsBfsk")!.SetValue(session, bfsk);
            type.GetField("Diagnostics")!.SetValue(session, new ReceiverSessionDiagnostics(bfsk ? "BFSK" : "FDM", "Audio digital sintético temporizado", true, bfsk, acousticSource: false));
            typeof(MainWindow).GetField("continuousListening", flags)!.SetValue(window, session);
            var worker = (Task)Call("ListenWorkerAsync", session, false, false)!;
            type.GetField("Worker")!.SetValue(session, worker);
            typeof(AudioCaptureService).GetField("running", flags)!.SetValue(capture, true);
            type.GetField("MicrophoneStarted")!.SetValue(session, true);
            using var loadCancellation = new CancellationTokenSource();
            int completedPairs = 0;
            var loadTask = comparisonLoad ? Task.Run(() =>
            {
                try
                {
                    while (!loadCancellation.IsCancellationRequested)
                    {
                        new AcousticLink.App.Experiments.ComparisonExperiment().RunPair("carga", "Hola", "Mundo",
                            true, 20, 42, FdmAmplitudeProfile.Original, loadCancellation.Token);
                        Interlocked.Increment(ref completedPairs);
                    }
                }
                catch (OperationCanceledException) { }
            }) : Task.CompletedTask;
            long Counter(string field) => (long)type.GetField(field)!.GetValue(session)!;
            var clock = Stopwatch.StartNew();
            int blocks = 0;
            for (int at = 0; at < audio.Length;)
            {
                if (burst && at == 48000) Thread.Sleep(95); // Pausa de callbacks, seguida por cinco paquetes de 960 muestras.
                int chunkSize = burst && at >= 48000 && at < 52800 ? 960 : blockSize;
                var block = audio.AsSpan(at, Math.Min(chunkSize, audio.Length - at)).ToArray();
                try { Call("EnqueueContinuousSamples", session, block, 48000); }
                catch (TargetInvocationException ex) { throw new InvalidOperationException($"{name}: rechazo al bloque {blocks}: {ex.InnerException?.Message}", ex); }
                at += block.Length;
                blocks++;
                if (refreshUi && blocks % Math.Max(1, 100 / cadenceMs) == 0) Call("RefreshCapture");
                int wait = (int)Math.Max(0, at * 1000L / 48000 - clock.ElapsedMilliseconds);
                if (wait > 0) Thread.Sleep(wait);
            }
            bool drained = SpinWait.SpinUntil(() => Counter("ConsumedSamples") == Counter("AcceptedSamples"), 5000);
            string report = (string)Call("DescribeContinuousProgress", session)!;
            Console.WriteLine($"{name}: {audio.Length} muestras, {clock.Elapsed.TotalSeconds:0.00} s reloj, cola máxima {Counter("MaxQueue")}, pendiente máximo {Counter("MaxPendingSamples")}, rechazados {Counter("RejectedBlocks")}");
            loadCancellation.Cancel(); loadTask.GetAwaiter().GetResult();
            if (comparisonLoad) Console.WriteLine($"{name}: pares sintéticos completos durante la entrega: {completedPairs}");
            Console.WriteLine(string.Join(" | ", report.Split('\n').Where(x => x.StartsWith("Append receptor:") || x.StartsWith("Diagnóstico:") || x.StartsWith("Monitor/resumen:") || x.StartsWith("Total worker:") || x.StartsWith("Sincronizador:"))));
            if (!drained || Counter("RejectedBlocks") != 0) throw new InvalidOperationException($"{name}: muestras pendientes o rechazadas");
            long receipts = ((IContinuousTextReceiver?)type.GetField("Receiver")!.GetValue(session))?.ReceptionCount ?? 0;
            if (receipts != expectedReceipts) throw new InvalidOperationException($"{name}: recepciones {receipts}, esperadas {expectedReceipts}");
            int validated = ((ReceiverSessionDiagnostics)type.GetField("Diagnostics")!.GetValue(session)!).Snapshot().ValidMessages;
            if (validated != expectedReceipts * (bfsk ? 1 : 2))
                throw new InvalidOperationException($"{name}: {validated} canales válidos, se esperaban {expectedReceipts * (bfsk ? 1 : 2)}");
            typeof(AudioCaptureService).GetField("running", flags)!.SetValue(capture, false);
            Pump((Task)Call("StopContinuousListeningAsync", false)!);
        }
        var bfsk = new BfskTextModem().Generate("Hola");
        var fdm = new FdmBitstreamGenerator().GenerateTextFrames("Hola", "Mundo");
        var random = new Random(719);
        var noise = Enumerable.Range(0, 48000 * 4).Select(_ => (float)((random.NextDouble() - .5) * .04)).ToArray();
        if (Environment.GetEnvironmentVariable("ACOUSTICLINK_COMPARISON_LOAD") == "1")
        {
            Run("BFSK/base/5 ms", true, bfsk, true, 1, 240, 5);
            Run("BFSK/comparación/5 ms", true, bfsk, true, 1, 240, 5, comparisonLoad: true);
            Run("FDM/base/5 ms", false, fdm, true, 1, 240, 5);
            Run("FDM/comparación/5 ms", false, fdm, true, 1, 240, 5, comparisonLoad: true);
            window.Close(); app.Shutdown();
            return;
        }
        if (Environment.GetEnvironmentVariable("ACOUSTICLINK_SMALL_BLOCKS") == "1")
        {
            Run("silencio/5 ms", false, new float[48000 * 5], true, 0, 240, 5);
            Run("BFSK/Hola/5 ms", true, bfsk, true, 1, 240, 5);
            Run("FDM/Hola-Mundo/5 ms", false, fdm, true, 1, 240, 5);
            Run("ruido y cabecera parcial/5 ms", false, noise.Concat(fdm[..96000]).ToArray(), true, 0, 240, 5);
            Run("BFSK consecutivas/5 ms", true, bfsk.Concat(new float[24000]).Concat(bfsk).ToArray(), true, 2, 240, 5);
            Run("FDM consecutivas/5 ms", false, fdm.Concat(new float[24000]).Concat(fdm).ToArray(), true, 2, 240, 5);
            Run("BFSK/pausa y ráfaga", true, bfsk, true, 1, 240, 5, true);
            Run("FDM/pausa y ráfaga", false, fdm, true, 1, 240, 5, true);
            window.Close(); app.Shutdown();
            return;
        }
        Run("silencio/FDM/sin UI", false, new float[48000 * 5], false, 0);
        Run("BFSK/Hola/sin UI", true, bfsk, false, 1);
        Run("BFSK/Hola/con UI", true, bfsk, true, 1);
        Run("FDM/Hola-Mundo/sin UI", false, fdm, false, 1);
        Run("FDM/Hola-Mundo/con UI", false, fdm, true, 1);
        Run("ruido y cabecera parcial/FDM", false, noise.Concat(fdm[..96000]).ToArray(), false, 0);
        Run("BFSK consecutivas/con UI", true, bfsk.Concat(new float[24000]).Concat(bfsk).ToArray(), true, 2);
        Run("FDM consecutivas/con UI", false, fdm.Concat(new float[24000]).Concat(fdm).ToArray(), true, 2);
        window.Close(); app.Shutdown();
    }
    catch (Exception ex) { failure = ex; }
});
thread.SetApartmentState(ApartmentState.STA);
thread.Start(); thread.Join();
if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
Console.WriteLine("Entrega temporizada sintética: OK; no equivale a captura acústica.");

static void Pump(Task task)
{
    var limit = DateTime.UtcNow.AddSeconds(10);
    while (!task.IsCompleted && DateTime.UtcNow < limit)
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    task.GetAwaiter().GetResult();
}
