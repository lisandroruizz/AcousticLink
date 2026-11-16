using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Controls;
using AcousticLink.App.Protocol;
using AcousticLink.App.Visualization;

namespace AcousticLink.App;

public partial class MainWindow
{
    private sealed class ListeningSession
    {
        public readonly record struct QueuedBlock(float[] Samples, int Rate, long EnqueuedAt);
        public sealed class Timing
        {
            public long Count, TotalTicks, MaxTicks;
            public void Add(long ticks)
            {
                Interlocked.Increment(ref Count);
                Interlocked.Add(ref TotalTicks, ticks);
                SetMaximum(ref MaxTicks, ticks);
            }
        }
        public sealed record Rejection(string Reason, int BlockSamples, int QueueAtFailure,
            string WorkerStage, long At, long Input, long Accepted, long Consumed,
            long HeaderStarts, string Synchronizer, long RunningAppendTicks, long CompletedAppendMaxTicks);
        public Guid Id { get; } = Guid.NewGuid();
        public long Started = Stopwatch.GetTimestamp();
        public const int QueueCapacity = 16;
        public readonly Channel<QueuedBlock> Queue = Channel.CreateBounded<QueuedBlock>(new BoundedChannelOptions(QueueCapacity)
        { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
        public readonly CancellationTokenSource Cancellation = new();
        public Task Worker = Task.CompletedTask;
        public Task? StopTask;
        public IContinuousTextReceiver? Receiver;
        public bool IsBfsk;
        public volatile bool Stopping;
        public bool MicrophoneStarted;
        public long InputSamples, AcceptedSamples, ConsumedSamples, LastInput, LastProgress, LastUi, DiscardedOnStop, HeaderStarts;
        public long LastArrival, ArrivalCount, ArrivalTotalTicks, ArrivalMaxTicks, LargestBlock, RejectedBlocks, RejectedSamples;
        public long MaxQueue, MaxPendingSamples, AllocatedBytes, MaxAllocatedBytes, Rearms, Receptions;
        public long CurrentAppendStarted, WorkerFinishedAt, EmptyBlocks;
        public int Gen0AtStart = GC.CollectionCount(0), Gen1AtStart = GC.CollectionCount(1), Gen2AtStart = GC.CollectionCount(2);
        public readonly Timing QueueWait = new(), AppendTime = new(), CompletedAppendTime = new(), InterruptedAppendTime = new(),
            DiagnosticsTime = new(), MonitorTime = new(), TotalTime = new();
        public Rejection? Incident;
        public int Rate, BufferedSamples;
        public volatile string Stage = "Esperando bloques", Synchronizer = "Buscando cabecera", LastError = "";
        public ContinuousUpdate? Latest;
        public ContinuousUpdate? Displayed;
        public readonly ConcurrentQueue<ContinuousReception> Completed = new();
        public readonly object HistoryLock = new(); // Solo metadatos (máximo diez); nunca audio, WPF ni esperas.
        public ReceiverSessionDiagnostics? Diagnostics;
    }
    private sealed record ContinuousUpdate(CarrierEnergyMap Map, LiveFdmReception? Reception, long Processed, long ReceptionCount);
    private ListeningSession? continuousListening;
    private readonly ObservableCollection<ContinuousReception> continuousHistory = [];
    private bool continuousHistorySelected;
    private bool continuousSegmentRetained;
    private string continuousLastError = "";
    private static void SetMaximum(ref long target, long value)
    {
        long prior;
        while (value > (prior = Interlocked.Read(ref target)) && Interlocked.CompareExchange(ref target, value, prior) != prior) { }
    }
    private static string ListeningPreference => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AcousticLink", "continuous-listening.txt");
    private void InitializeContinuousListening()
    {
        ContinuousHistoryComboBox.ItemsSource = continuousHistory;
        try { AutoListenCheckBox.IsChecked = File.Exists(ListeningPreference) && File.ReadAllText(ListeningPreference).Trim() == "true"; }
        catch (Exception ex) { ContinuousStatusText.Text = $"No se pudo leer preferencia: {ex.Message}"; }
        Loaded += async (_, _) => { if (AutoListenCheckBox.IsChecked == true && !closing) await StartContinuousListeningAsync(); };
    }
    private void AutoListen_Click(object sender, RoutedEventArgs e)
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(ListeningPreference)!); File.WriteAllText(ListeningPreference, AutoListenCheckBox.IsChecked == true ? "true" : "false"); }
        catch (Exception ex) { ContinuousStatusText.Text = $"Preferencia no guardada: {ex.Message}"; }
    }
    private async void ContinuousListen_Click(object sender, RoutedEventArgs e)
    {
        if (continuousListening is null) await StartContinuousListeningAsync(); else await StopContinuousListeningAsync();
    }
    private async Task StartContinuousListeningAsync()
    {
        if (calibrationBusy || calibrationCapture.IsRecording) { ContinuousStatusText.Text = "Detenga la calibración antes de iniciar la escucha."; return; }
        if (continuousListening is not null || capture.IsRecording || captureBusy || analyzing || closing)
        { ContinuousStatusText.Text = "Finalizá captura/análisis antes de abrir escucha continua."; return; }
        InvalidateAudioAnalysis();
        continuousLastError = "";
        ReceiverSignalMonitor.BeginLive("Escucha continua · esperando muestras reales…");
        if (MonitorSourceComboBox.SelectedIndex == 0) DashboardSignalMonitor.BeginLive("Micrófono continuo · esperando muestras…");
        var session = new ListeningSession { IsBfsk = IsBfskReceive };
        session.Diagnostics = new(session.IsBfsk ? "BFSK" : "FDM", "Micrófono · escucha continua", true,
            session.IsBfsk, sessionId: session.Id);
        continuousListening = session;
        ShowReceiverDiagnostics(session.Diagnostics);
        continuousHistory.Clear(); continuousHistorySelected = false;
        bool hamming = UseHammingReceiveCheckBox.IsChecked == true, erasures = RecoverReceiveErasuresCheckBox.IsChecked == true;
        ContinuousStatusText.Text = $"ESCUCHA CONTINUA {(session.IsBfsk ? "BFSK" : "FDM")} · abriendo micrófono…";
        ReceptionAView.Clear("Escucha automática · buscando cabecera A");
        ReceptionBView.Clear(session.IsBfsk ? "Canal B no utilizado" : "Escucha automática · buscando cabecera B");
        ReceivedFdmText.Text = "Recepción automática · esperando una cabecera válida; no requiere pulsar Recibir mensajes.";
        session.Worker = ListenWorkerAsync(session, hamming, erasures);
        RefreshContinuousControls();
        try
        {
            await capture.StartContinuousAsync((samples, rate) => EnqueueContinuousSamples(session, samples, rate), session.Cancellation.Token);
            if (closing || !ReferenceEquals(continuousListening, session)) return;
            session.Diagnostics?.Note("Micrófono activo", "Dispositivo iniciado; buscando muestras y cabeceras.");
            ShowReceiverDiagnostics(session.Diagnostics);
            session.MicrophoneStarted = true;
            ContinuousStatusText.Text = "ESCUCHA CONTINUA ACTIVADA · MICRÓFONO ACTIVO · buscando transmisión";
        }
        catch (OperationCanceledException) when (session.Stopping || closing) { }
        catch (Exception ex)
        {
            if (session.LastError.Length == 0) session.LastError = ex.Message;
            ContinuousStatusText.Text = $"No se pudo iniciar escucha: {ex.Message}. Puede reintentar.";
            await StopContinuousListeningAsync(true);
        }
    }
    private void EnqueueContinuousSamples(ListeningSession session, float[] samples, int rate)
    {
        // Callback acotado: nunca demodula, espera al consumidor ni dibuja.
        if (samples.Length == 0) { Interlocked.Increment(ref session.EmptyBlocks); return; }
        Interlocked.Add(ref session.InputSamples, samples.Length);
        long now = Stopwatch.GetTimestamp();
        Interlocked.Exchange(ref session.LastInput, now);
        long prior = Interlocked.Exchange(ref session.LastArrival, now);
        if (prior != 0) { long gap = now - prior; Interlocked.Increment(ref session.ArrivalCount); Interlocked.Add(ref session.ArrivalTotalTicks, gap); SetMaximum(ref session.ArrivalMaxTicks, gap); }
        SetMaximum(ref session.LargestBlock, samples.Length);
        Volatile.Write(ref session.Rate, rate);
        if (session.Stopping) { Interlocked.Add(ref session.DiscardedOnStop, samples.Length); return; }
        if (Volatile.Read(ref session.Incident) is not null)
        {
            Interlocked.Increment(ref session.RejectedBlocks);
            Interlocked.Add(ref session.RejectedSamples, samples.Length);
            return; // La sesión ya está cancelada; no aceptar más audio tras la primera pérdida.
        }
        Interlocked.Add(ref session.AcceptedSamples, samples.Length);
        if (samples.Length <= rate && session.Queue.Writer.TryWrite(new(samples, rate, now)))
        {
            SetMaximum(ref session.MaxQueue, session.Queue.Reader.Count);
            SetMaximum(ref session.MaxPendingSamples, Interlocked.Read(ref session.AcceptedSamples) - Interlocked.Read(ref session.ConsumedSamples));
            return;
        }
        Interlocked.Add(ref session.AcceptedSamples, -samples.Length);
        bool stoppingAtFailure = session.Stopping;
        string reason = samples.Length > rate ? "Bloque superior a un segundo" : stoppingAtFailure
            ? "TryWrite rechazó durante la parada" : "Cola llena (TryWrite rechazó)";
        Interlocked.Increment(ref session.RejectedBlocks);
        Interlocked.Add(ref session.RejectedSamples, samples.Length);
        int atFailure = session.Queue.Reader.Count;
        SetMaximum(ref session.MaxQueue, samples.Length <= rate && !stoppingAtFailure
            ? ListeningSession.QueueCapacity : atFailure);
        long appendStarted = Interlocked.Read(ref session.CurrentAppendStarted);
        var first = new ListeningSession.Rejection(reason, samples.Length, atFailure, session.Stage, now,
            Interlocked.Read(ref session.InputSamples), Interlocked.Read(ref session.AcceptedSamples),
            Interlocked.Read(ref session.ConsumedSamples), Interlocked.Read(ref session.HeaderStarts), session.Synchronizer,
            appendStarted == 0 ? 0 : now - appendStarted, Interlocked.Read(ref session.CompletedAppendTime.MaxTicks));
        var recorded = Interlocked.CompareExchange(ref session.Incident, first, null);
        if (recorded is null)
            session.LastError = $"Sobrecarga de escucha: {reason}; no se continúa con audio perdido. Detenga/reintente.";
        session.Cancellation.Cancel(); // También solicita detener el dispositivo, aunque WPF esté ocupado.
        throw new InvalidOperationException(session.LastError);
    }
    private async Task ListenWorkerAsync(ListeningSession session, bool hamming, bool erasures)
    {
        IncrementalSignalMonitor? monitor = null;
        long monitorOrigin = -1; object monitorSource = new();
        long nextUi = 0, lastPublicationCount = -1;
        try
        {
            await foreach (var block in session.Queue.Reader.ReadAllAsync(session.Cancellation.Token).ConfigureAwait(false))
            {
                long blockStarted = Stopwatch.GetTimestamp();
                long allocationStarted = GC.GetAllocatedBytesForCurrentThread();
                session.QueueWait.Add(blockStarted - block.EnqueuedAt);
                if (session.Receiver is null)
                {
                    session.Receiver = session.IsBfsk ? new ContinuousBfskReceiver(block.Rate, hamming, erasures, session.Id)
                        : new ContinuousFdmReceiver(block.Rate, hamming, erasures, session.Id);
                    session.Receiver.Received += entry =>
                    {
                        session.Diagnostics?.ObserveProgress(entry.Result, entry.StartSample);
                        session.Diagnostics?.RecordCompleted(entry.Id, entry.Result, countMissingHeaders: true);
                        lock (session.HistoryLock)
                        {
                            session.Completed.Enqueue(entry);
                            while (session.Completed.Count > ContinuousFdmReceiver.HistoryLimit) session.Completed.TryDequeue(out _);
                        }
                    };
                    monitor = new(block.Rate, block.Rate * 30);
                }
                session.Stage = "Sincronización / símbolos";
                long stageStarted = Stopwatch.GetTimestamp();
                Interlocked.Exchange(ref session.CurrentAppendStarted, stageStarted);
                bool appendCompleted = false;
                try { session.Receiver.Append(block.Samples, session.Cancellation.Token); appendCompleted = true; }
                finally
                {
                    long ticks = Stopwatch.GetTimestamp() - stageStarted;
                    session.AppendTime.Add(ticks);
                    (appendCompleted ? session.CompletedAppendTime : session.InterruptedAppendTime).Add(ticks);
                    Interlocked.Exchange(ref session.CurrentAppendStarted, 0);
                }
                Interlocked.Add(ref session.ConsumedSamples, block.Samples.Length);
                Interlocked.Exchange(ref session.LastProgress, Stopwatch.GetTimestamp());
                Volatile.Write(ref session.BufferedSamples, session.Receiver.BufferedSamples);
                Interlocked.Exchange(ref session.HeaderStarts, session.Receiver.EvaluatedHeaderStarts);
                Interlocked.Exchange(ref session.Rearms, session.Receiver.RearmCount);
                Interlocked.Exchange(ref session.Receptions, session.Receiver.ReceptionCount);
                Volatile.Write(ref session.Rate, block.Rate);
                session.Synchronizer = session.Receiver.Current?.Result.Synchronization.Reason ?? "Buscando cabecera";
                stageStarted = Stopwatch.GetTimestamp();
                session.Diagnostics?.SetSamples(Interlocked.Read(ref session.InputSamples), Interlocked.Read(ref session.ConsumedSamples), block.Rate);
                var currentResult = session.Receiver.Current?.ObservedResult;
                long frameKey = currentResult?.Synchronization.StartSeconds is double start
                    ? session.Receiver.BufferOrigin + (long)Math.Round(start * block.Rate) : -1;
                session.Diagnostics?.ObserveProgress(currentResult, frameKey);
                session.DiagnosticsTime.Add(Stopwatch.GetTimestamp() - stageStarted);
                session.Stage = "Energía / resumen gráfico";
                stageStarted = Stopwatch.GetTimestamp();
                if (monitorOrigin != session.Receiver.BufferOrigin)
                { monitor = new(block.Rate, block.Rate * 30); monitorOrigin = session.Receiver.BufferOrigin; monitorSource = new(); }
                var available = session.Receiver.AvailableSamples;
                monitor!.Append(available.Span[monitor.Count..], session.Cancellation.Token);
                if (monitor.Count == 0) { session.MonitorTime.Add(Stopwatch.GetTimestamp() - stageStarted); RecordBlockCost(); continue; }
                if (Stopwatch.GetTimestamp() < nextUi && session.Receiver.ReceptionCount == lastPublicationCount) { session.MonitorTime.Add(Stopwatch.GetTimestamp() - stageStarted); RecordBlockCost(); session.Stage = "Esperando bloques"; continue; }
                nextUi = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 5;
                lastPublicationCount = session.Receiver.ReceptionCount;
                var map = monitor.Snapshot(MonitorSignalOrigin.Microphone, "Micrófono continuo · tiempo local del buffer de escucha");
                session.Diagnostics?.ObserveAcousticPeak(map.Peak);
                map = map.WithSignal(map.Signal with { SourceId = monitorSource });
                // Solo una instantánea pendiente: nunca esperar al Dispatcher para recibir audio.
                Interlocked.Exchange(ref session.Latest, new(map, session.Receiver.Current,
                    Interlocked.Read(ref session.ConsumedSamples), session.Receiver.ReceptionCount));
                session.MonitorTime.Add(Stopwatch.GetTimestamp() - stageStarted);
                RecordBlockCost();
                session.Stage = "Esperando bloques";
                void RecordBlockCost()
                {
                    session.TotalTime.Add(Stopwatch.GetTimestamp() - blockStarted);
                    long allocated = Math.Max(0, GC.GetAllocatedBytesForCurrentThread() - allocationStarted);
                    Interlocked.Add(ref session.AllocatedBytes, allocated);
                    SetMaximum(ref session.MaxAllocatedBytes, allocated);
                }
            }
            session.Cancellation.Token.ThrowIfCancellationRequested();
            session.Receiver?.Complete(session.Cancellation.Token);
        }
        catch (OperationCanceledException) when (session.Cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError($"Escucha continua: {ex}");
            session.LastError = ex.Message;
            session.Cancellation.Cancel();
        }
        finally
        {
            Interlocked.Exchange(ref session.WorkerFinishedAt, Stopwatch.GetTimestamp());
            session.Stage = session.LastError.Length > 0 ? "Error" : session.Cancellation.IsCancellationRequested ? "Cancelado" : "Finalizado";
        }
    }
    private void PublishContinuous(ListeningSession session, ContinuousUpdate update, ContinuousReception[] entries)
    {
        if (closing || session.Stopping || !ReferenceEquals(continuousListening, session)) return;
        var map = update.Map; var snapshot = update.Reception;
        AddContinuousHistory(entries);
        ShowReceiverDiagnostics(session.Diagnostics);
        string last = continuousHistory.LastOrDefault()?.Label ?? "sin recepciones";
        ContinuousStatusText.Text = $"ESCUCHA CONTINUA · MICRÓFONO ACTIVO · recepciones {update.ReceptionCount} · última: {last}";
        var activity = new ReceptionActivity($"ESCUCHA CONTINUA {(session.IsBfsk ? "BFSK" : "FDM")} · buscando/recibiendo", "Micrófono (segmento gráfico local)", map.Signal.SampleRate,
            (int)Math.Min(int.MaxValue, Interlocked.Read(ref session.InputSamples)), (int)Math.Min(int.MaxValue, update.Processed),
            ReceptionChannelActivity.From(snapshot?.Result.ChannelA, true), ReceptionChannelActivity.From(snapshot?.Result.ChannelB, true));
        LiveReceptionInspector.Show(activity);
        if (!continuousHistorySelected)
        {
            bool sameSegment = ReferenceEquals(ReceiverSignalMonitor.Data?.Signal.SourceId, map.Signal.SourceId);
            // Una inspección pausada conserva sus muestras y trazas al rotar el buffer.
            if (sameSegment || !ReceiverSignalMonitor.IsLive || ReceiverSignalMonitor.FollowingCapture)
            {
                if (!sameSegment) ReceiverSignalMonitor.BeginLive("Micrófono continuo · nuevo segmento local");
                ReceiverSignalMonitor.UpdateLive(map);
                ReceiverSignalMonitor.SetReception(snapshot?.Result, map.Signal.Duration);
                ReceiverSignalMonitor.SetReceptionActivity(activity);
            }
            var shown = entries.LastOrDefault()?.Result ?? (snapshot?.Result.ChannelA.HeaderValid == true || snapshot?.Result.ChannelB.HeaderValid == true ? snapshot.Result : null);
            if (shown is not null)
            {
                ReceptionAView.Show(shown.ChannelA, shown.Synchronization, "A");
                ReceptionBView.Show(shown.ChannelB, shown.Synchronization, "B");
                if (session.IsBfsk) ReceivedFdmText.Text = DescribeBfskReception(shown.ChannelA, shown.Synchronization.Reason);
            }
        }
        if (continuousHistorySelected) ContinuousStatusText.Text += " · inspección histórica retenida";
        if (MonitorSourceComboBox.SelectedIndex == 0)
        {
            if (!ReferenceEquals(DashboardSignalMonitor.Data?.Signal.SourceId, map.Signal.SourceId))
                DashboardSignalMonitor.BeginLive("Micrófono continuo · nuevo segmento local");
            DashboardSignalMonitor.UpdateLive(map);
        }
        if (!continuousHistorySelected)
        {
            ReceivedWaveform.SetData(map.Overview.ToArray(), map.Signal.Duration);
            WaveformInfoText.Text = $"Micrófono continuo · segmento local {map.Signal.Duration:0.00} s · {map.Signal.SampleRate} Hz · pico {map.Peak:G4}";
        }
    }
    private void AddContinuousHistory(IEnumerable<ContinuousReception> entries)
    {
        if (closing) return;
        foreach (var entry in entries)
        { continuousHistory.Add(entry); while (continuousHistory.Count > ContinuousFdmReceiver.HistoryLimit)
            { if (ReferenceEquals(ContinuousHistoryComboBox.SelectedItem, continuousHistory[0])) InvalidateWalkthrough("El histórico seleccionado salió del historial."); continuousHistory.RemoveAt(0); } }
    }
    private async void ContinuousHistory_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ContinuousHistoryComboBox.SelectedItem is not ContinuousReception entry)
        { InvalidateWalkthrough("No hay recepción histórica seleccionada."); return; }
        InvalidateWalkthrough("Cambió la recepción histórica seleccionada. Prepará un recorrido nuevo.");
        continuousHistorySelected = true;
        RefreshCapture();
        try
        {
            var map = await Task.Run(() => CarrierEnergyMap.Analyze(new(entry.Samples, entry.SampleRate, MonitorSignalOrigin.Microphone, $"Histórico #{entry.Id} · no captura nueva") { SourceId = entry }));
            if (closing || !ReferenceEquals(ContinuousHistoryComboBox.SelectedItem, entry)) return;
            ApplyReceptionModeVisual(entry.IsBfsk);
            ReceiverSignalMonitor.SetReception(null);
            ReceiverSignalMonitor.Show(map); ReceiverSignalMonitor.SetReception(entry.Result, map.Signal.Duration);
            var activity = new ReceptionActivity($"HISTÓRICO · recepción #{entry.Id} · no audio nuevo", "Micrófono", entry.SampleRate, entry.Samples.Length, entry.Samples.Length,
                ReceptionChannelActivity.From(entry.Result.ChannelA, false), ReceptionChannelActivity.From(entry.Result.ChannelB, false));
            ReceiverSignalMonitor.SetReceptionActivity(activity);
            if (continuousListening is null) LiveReceptionInspector.Show(activity);
            ReceivedWaveform.SetData(map.Overview.ToArray(), map.Signal.Duration);
            WaveformInfoText.Text = $"Histórico #{entry.Id} · {map.Signal.Duration:0.00} s · {entry.SampleRate} Hz";
            ReceivedFrameExplorer.Show(entry.Result);
            ReceptionAView.Show(entry.Result.ChannelA, entry.Result.Synchronization, "A"); ReceptionBView.Show(entry.Result.ChannelB, entry.Result.Synchronization, "B");
            if (entry.IsBfsk) ReceivedFdmText.Text = $"Histórico #{entry.Id} · " +
                DescribeBfskReception(entry.Result.ChannelA, entry.Result.Synchronization.Reason);
        }
        catch (Exception ex) { if (!closing && ReferenceEquals(ContinuousHistoryComboBox.SelectedItem, entry)) ContinuousStatusText.Text = $"Histórico no disponible: {ex.Message}"; }
    }
    private void ContinuousLive_Click(object sender, RoutedEventArgs e)
    {
        InvalidateWalkthrough("Volviendo al audio en vivo. Seleccioná una recepción para inspeccionarla.");
        ContinuousHistoryComboBox.SelectedIndex = -1; continuousHistorySelected = false;
        ApplyReceptionModeVisual(IsBfskReceive);
        if (continuousListening is null) { InvalidateAudioAnalysis(); RefreshCapture(); }
        else ReceiverSignalMonitor.Clear("Volviendo a muestras nuevas del micrófono…");
    }
    private async void SaveContinuousSegment_Click(object sender, RoutedEventArgs e)
    {
        var map = ReceiverSignalMonitor.Data;
        if (map is null || map.Signal.Origin != MonitorSignalOrigin.Microphone)
        { ContinuousSaveNoteText.Text = "No hay un segmento de micrófono observado disponible."; return; }
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Audio WAV (*.wav)|*.wav", FileName = "segmento-escucha.wav" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            await Task.Run(() =>
            {
                var samples = map.Signal.Samples.ToArray();
                var audio = new Audio.CapturedAudio(map.Signal.SampleRate, 1, 30);
                audio.Append(samples, samples.Length); audio.Complete(Audio.CaptureStopReason.Manual);
                audio.SaveWav(dialog.FileName);
            });
            if (!closing) ContinuousSaveNoteText.Text = $"Guardado segmento local de {map.Signal.Duration:0.00} s · {Path.GetFileName(dialog.FileName)}. No es toda la sesión.";
        }
        catch (Exception ex) { if (!closing) ContinuousSaveNoteText.Text = $"No se pudo guardar segmento: {ex.Message}"; }
    }
    private Task StopContinuousListeningAsync(bool preserveStatus = false)
    {
        var session = continuousListening;
        return session is null ? Task.CompletedTask : session.StopTask ??= StopContinuousCoreAsync(session, preserveStatus);
    }
    private async Task StopContinuousCoreAsync(ListeningSession session, bool preserveStatus)
    {
        session.Stopping = true;
        bool failed = false;
        if (!preserveStatus) ContinuousStatusText.Text = "Deteniendo micrófono · finalizando recepción…";
        ContinuousListenButton.Content = "Deteniendo escucha…"; ContinuousListenButton.IsEnabled = false;
        // La cancelación precede a cualquier espera: no drenar una cola atrasada ni esperar gráficos.
        session.Cancellation.Cancel();
        session.Queue.Writer.TryComplete();
        try { await capture.StopAsync(); await session.Worker; }
        catch (Exception ex)
        {
            failed = true; session.LastError = ex.Message;
            Trace.TraceError($"Parada de escucha continua: {ex}");
            ContinuousStatusText.Text = $"Error al detener escucha: {ex.Message}";
        }
        finally
        {
            await session.Worker;
            AddContinuousHistory(session.Completed.ToArray());
            // Incluye también el bloque retirado de la cola que se interrumpió durante Append.
            Interlocked.Add(ref session.DiscardedOnStop, Math.Max(0, session.AcceptedSamples - session.ConsumedSamples));
            while (session.Queue.Reader.TryRead(out _)) { } // Liberar arrays; su descarte ya está contabilizado.
            session.Cancellation.Dispose();
            session.Receiver = null; // Liberar el lector y su buffer; el monitor conserva solo su segmento observable.
            Interlocked.Exchange(ref session.Latest, null);
            session.Completed.Clear();
            if (session.LastError.Length == 0 && capture.Error is { } deviceError) session.LastError = deviceError;
            session.Diagnostics?.SetSamples(session.InputSamples, session.ConsumedSamples, session.Rate);
            if (capture.Error is { } captureError) session.Diagnostics?.DeviceError(captureError);
            else if (session.LastError.Length > 0) session.Diagnostics?.Note("Error de escucha", session.LastError);
            session.Diagnostics?.Stop(session.LastError.Length > 0 ? "Escucha finalizada con error."
                : "Escucha detenida; solo recepciones finalizadas se contabilizaron.");
            ShowReceiverDiagnostics(session.Diagnostics);
            ContinuousDiagnosticsText.Text = DescribeContinuousProgress(session);
            continuousLastError = session.LastError;
            if (ReferenceEquals(continuousListening, session)) continuousListening = null;
            if (session.LastError.Length > 0) ContinuousStatusText.Text = $"Escucha finalizada con error: {session.LastError} Puede reintentar.";
            else if (!preserveStatus && !failed) ContinuousStatusText.Text = $"Escucha detenida · {continuousHistory.Count} recepciones conservadas";
            if (!closing)
            {
                continuousSegmentRetained = ReceiverSignalMonitor.Data is not null;
                var result = session.Displayed?.Reception?.ObservedResult;
                if (result is not null && !continuousHistorySelected)
                {
                    var activity = new ReceptionActivity("ESCUCHA DETENIDA · segmento observado retenido", "Micrófono", session.Rate,
                        (int)Math.Min(int.MaxValue, session.InputSamples), (int)Math.Min(int.MaxValue, session.ConsumedSamples),
                        ReceptionChannelActivity.From(result.ChannelA, false), ReceptionChannelActivity.From(result.ChannelB, false));
                    LiveReceptionInspector.Show(activity); ReceiverSignalMonitor.SetReceptionActivity(activity);
                }
                if (!continuousHistorySelected && continuousHistory.LastOrDefault() is { } lastEntry)
                { ReceptionAView.Show(lastEntry.Result.ChannelA, lastEntry.Result.Synchronization, "A"); ReceptionBView.Show(lastEntry.Result.ChannelB, lastEntry.Result.Synchronization, "B"); }
                if (!continuousHistorySelected && ReceiverSignalMonitor.Data is { } last) ReceiverSignalMonitor.FinishLive(last);
                if (DashboardSignalMonitor.Data is { } dashboard && dashboard.Signal.Origin == MonitorSignalOrigin.Microphone) DashboardSignalMonitor.FinishLive(dashboard);
                RefreshCapture();
                RefreshContinuousControls();
            }
        }
    }
    private void DisableManualAudioTools()
    {
        foreach (var button in new[] { SaveCaptureButton, AnalyzeToneButton, DecodeSequenceButton, SynchronizeButton,
            AnalyzeFdmButton, DecodeFdmButton, SynchronizeFdmButton, ReceiveFdmTextButton, AnalyzeExperimentButton, ExportExperimentButton })
            button.IsEnabled = false;
    }
    private void RefreshContinuousRetainedContext()
    {
        if (!continuousSegmentRetained || continuousHistorySelected || continuousListening is not null) return;
        DisableManualAudioTools();
        LiveCaptureText.Text = "Micrófono apagado · inspección del segmento continuo retenido";
        GeneralStateText.Text = "Escucha detenida · segmento observado retenido";
        WorkspaceAudioStatusText.Text = "Audio observado de micrófono · segmento local retenido";
        AudioSourceText.Text = "Segmento de escucha continua (no grabación completa)";
        CaptureStatusText.Text = "Escucha detenida. Puede guardar el segmento o volver al audio manual.";
    }
    private void RefreshContinuousControls()
    {
        var session = continuousListening;
        ContinuousListenButton.Content = session is null ? "Iniciar escucha continua" : session.Stopping ? "Deteniendo escucha…"
            : !session.MicrophoneStarted ? "Iniciando micrófono…" : "Detener escucha continua";
        ContinuousListenButton.IsEnabled = session is null ? !capture.IsRecording && !captureBusy && !analyzing : !session.Stopping && session.MicrophoneStarted;
        if (session is null)
        {
            ContinuousOperationalText.Text = continuousLastError.Length > 0 ? "Error · micrófono apagado · puede reintentar" : "Micrófono apagado · escucha detenida";
            return;
        }
        var update = Interlocked.Exchange(ref session.Latest, null);
        if (!session.Stopping && update is not null)
        {
            var entries = new List<ContinuousReception>();
            lock (session.HistoryLock)
                while (session.Completed.TryDequeue(out var entry)) entries.Add(entry);
            try { PublishContinuous(session, update, entries.ToArray()); }
            catch (Exception ex)
            {
                session.LastError = $"Presentación WPF: {ex.Message}";
                Trace.TraceError($"Publicación de escucha continua: {ex}");
                ContinuousStatusText.Text = $"Error: {session.LastError}";
                _ = StopContinuousListeningAsync(true);
                return;
            }
            session.Displayed = update;
            Interlocked.Exchange(ref session.LastUi, Stopwatch.GetTimestamp());
        }
        StartCaptureButton.IsEnabled = LoadWavButton.IsEnabled = UseHammingReceiveCheckBox.IsEnabled = RecoverReceiveErasuresCheckBox.IsEnabled = DecodeDuringCaptureCheckBox.IsEnabled = ReceiverModeComboBox.IsEnabled = false;
        StopCaptureButton.IsEnabled = false;
        DisableManualAudioTools();
        int rate = Volatile.Read(ref session.Rate);
        var current = session.Displayed?.Reception?.Result;
        string state = session.Stopping ? "Deteniendo escucha" : !session.MicrophoneStarted ? "Iniciando micrófono"
            : session.LastError.Length > 0 ? "Error de escucha"
            : current?.ChannelA.CrcValid == true || current?.ChannelB.CrcValid == true ? "Mensaje recibido"
            : current?.ChannelA.HeaderValid == true || current?.ChannelB.HeaderValid == true ? "Recibiendo · cabecera detectada" : "Escuchando · buscando transmisión";
        ContinuousOperationalText.Text = state;
        GeneralStateText.Text = state;
        WorkspaceAudioStatusText.Text = rate > 0 ? $"Micrófono continuo · {rate:N0} Hz" : "Entrada de micrófono · iniciando";
        AudioSourceText.Text = continuousHistorySelected ? "Histórico seleccionado · escucha continúa" : "Micrófono · recepción automática";
        CaptureFormatText.Text = capture.FormatDescription;
        CaptureStatusText.Text = state;
        LiveReceptionStatusText.Text = $"Escucha automática · {session.Synchronizer}";
        if (session.MicrophoneStarted && !session.Stopping && session.LastInput > 0 && Stopwatch.GetElapsedTime(Interlocked.Read(ref session.LastInput)).TotalSeconds > 2)
            ContinuousOperationalText.Text += " · sin paquetes recientes (ver diagnóstico)";
        else if (session.AcceptedSamples > session.ConsumedSamples && session.LastProgress > 0 && Stopwatch.GetElapsedTime(Interlocked.Read(ref session.LastProgress)).TotalSeconds > 2)
            ContinuousOperationalText.Text += " · procesamiento retrasado (ver diagnóstico)";
        LiveCaptureText.Text = rate > 0 ? $"Escucha continua · recibido {Interlocked.Read(ref session.InputSamples) / (double)rate:0.00} s · consumido {Interlocked.Read(ref session.ConsumedSamples) / (double)rate:0.00} s" : state;
        CaptureMetricsText.Text = rate > 0 ? $"Buffer local {Volatile.Read(ref session.BufferedSamples) / (double)rate:0.00} s · no es duración global" : "Esperando muestras reales";
        ContinuousDiagnosticsText.Text = DescribeContinuousProgress(session);
        if (!session.Stopping && session.MicrophoneStarted && (!capture.IsRecording || session.Worker.IsCompleted || session.LastError.Length > 0))
        {
            if (session.LastError.Length == 0) session.LastError = capture.Error ?? (session.Worker.IsCompleted ? "El worker terminó inesperadamente." : "El dispositivo finalizó la entrada.");
            ContinuousStatusText.Text = $"Error: {session.LastError} Puede reintentar.";
            _ = StopContinuousListeningAsync(true);
        }
    }
    private string DescribeContinuousProgress(ListeningSession session)
    {
        static string Age(long timestamp) => timestamp == 0 ? "sin eventos" : $"{Stopwatch.GetElapsedTime(timestamp).TotalSeconds:0.00} s";
        static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
        static string Time(ListeningSession.Timing value) =>
            $"media {(value.Count == 0 ? 0 : Ms(Interlocked.Read(ref value.TotalTicks)) / value.Count):0.000} ms · máximo {Ms(Interlocked.Read(ref value.MaxTicks)):0.000} ms · {Interlocked.Read(ref value.Count):N0} bloques";
        long accepted = Interlocked.Read(ref session.AcceptedSamples), consumed = Interlocked.Read(ref session.ConsumedSamples);
        int rate = Volatile.Read(ref session.Rate);
        string pending = rate > 0 ? $"{Math.Max(0, accepted - consumed) / (double)rate:0.000} s" : "no disponible";
        var incident = session.Incident;
        var receiverDiagnostics = session.Diagnostics?.Snapshot();
        string failure = incident is null ? "ninguno" :
            $"{incident.Reason} · bloque {incident.BlockSamples:N0} muestras · cola observada tras rechazo {incident.QueueAtFailure}/{ListeningSession.QueueCapacity} · a {Ms(incident.At - session.Started) / 1000:0.000} s\n" +
            $"En rechazo: worker {incident.WorkerStage} · Append en curso {Ms(incident.RunningAppendTicks):0.000} ms · máximo Append ya completado {Ms(incident.CompletedAppendMaxTicks):0.000} ms · callback {incident.Input:N0} · aceptadas {incident.Accepted:N0} · consumidas {incident.Consumed:N0} · inicios {incident.HeaderStarts:N0} · {incident.Synchronizer}";
        return $"Modalidad: {(session.IsBfsk ? "BFSK" : "FDM")} · duración {Stopwatch.GetElapsedTime(session.Started).TotalSeconds:0.000} s · frecuencia {rate:N0} Hz\n" +
            $"Rechazo: {failure}\n" +
            $"Dispositivo: {(capture.IsRecording ? "capturando" : "apagado")} · tarea captura {(capture.SessionCompleted ? "finalizada" : "activa")}\n" +
            $"NAudio: {capture.ReceivedSamples:N0} muestras mono · callbacks vacíos {capture.EmptyBuffers:N0} · último paquete hace {Age(capture.LastBufferTimestamp)}\n" +
            $"Callback continuo: {Interlocked.Read(ref session.InputSamples):N0} · aceptadas en cola: {accepted:N0} · consumidas: {consumed:N0}\n" +
            $"Bloques: mayor {Interlocked.Read(ref session.LargestBlock):N0} muestras · vacíos ignorados {Interlocked.Read(ref session.EmptyBlocks):N0} · rechazados {Interlocked.Read(ref session.RejectedBlocks):N0} ({Interlocked.Read(ref session.RejectedSamples):N0} muestras) · intervalo medio {(session.ArrivalCount == 0 ? 0 : Ms(session.ArrivalTotalTicks) / session.ArrivalCount):0.000} ms · mayor {Ms(session.ArrivalMaxTicks):0.000} ms\n" +
            $"Cola: {session.Queue.Reader.Count}/{ListeningSession.QueueCapacity} ahora · máximo {Interlocked.Read(ref session.MaxQueue)}/{ListeningSession.QueueCapacity} · pendiente {pending} · máximo pendiente {Interlocked.Read(ref session.MaxPendingSamples):N0} muestras (incluye bloque en proceso)\n" +
            $"En cola: {Time(session.QueueWait)}\nAppend receptor: {Time(session.AppendTime)}\nAppend completo: {Time(session.CompletedAppendTime)}\nAppend interrumpido: {Time(session.InterruptedAppendTime)}\nDiagnóstico: {Time(session.DiagnosticsTime)}\nMonitor/resumen: {Time(session.MonitorTime)}\nTotal worker: {Time(session.TotalTime)}\n" +
            $"Worker: {session.Stage} · último progreso hace {Age(Interlocked.Read(ref session.LastProgress))}\n" +
            $"Última entrada hace {Age(Interlocked.Read(ref session.LastInput))} · publicación WPF hace {Age(Interlocked.Read(ref session.LastUi))}\n" +
            $"Sincronizador: {session.Synchronizer} · inicios evaluados: {Interlocked.Read(ref session.HeaderStarts):N0} · candidatas observadas: {receiverDiagnostics?.CandidateHeaders ?? 0:N0} · cabeceras validadas: {receiverDiagnostics?.ConfirmedHeaders ?? 0:N0} · rearmes: {Interlocked.Read(ref session.Rearms):N0} · recepciones: {Interlocked.Read(ref session.Receptions):N0}\nBuffer conservado: {Volatile.Read(ref session.BufferedSamples):N0} muestras / {(long)rate * 30:N0}\n" +
            $"Asignaciones worker aproximadas: {Interlocked.Read(ref session.AllocatedBytes):N0} bytes · máximo por bloque {Interlocked.Read(ref session.MaxAllocatedBytes):N0} bytes · GC de todo el proceso desde inicio: gen0 {GC.CollectionCount(0) - session.Gen0AtStart}, gen1 {GC.CollectionCount(1) - session.Gen1AtStart}, gen2 {GC.CollectionCount(2) - session.Gen2AtStart}\n" +
            $"Cancelación: {session.Cancellation.IsCancellationRequested} · descartadas al detener: {Interlocked.Read(ref session.DiscardedOnStop):N0}\n" +
            $"Worker tras rechazo: {(incident is null || session.WorkerFinishedAt == 0 ? "sin dato" : $"{Ms(session.WorkerFinishedAt - incident.At):0.000} ms hasta finalizar")}\n" +
            $"Último error: {(session.LastError.Length > 0 ? session.LastError : capture.Error ?? "ninguno")}";
    }
    private void CopyContinuousDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(ContinuousDiagnosticsText.Text);
            ContinuousCopyStatusText.Text = "Diagnóstico copiado al portapapeles.";
        }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            ContinuousCopyStatusText.Text = $"No se pudo copiar el diagnóstico: {ex.Message}";
        }
    }
}
