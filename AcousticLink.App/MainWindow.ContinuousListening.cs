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
        public const int QueueCapacity = 16;
        public readonly Channel<(float[] Samples, int Rate)> Queue = Channel.CreateBounded<(float[], int)>(new BoundedChannelOptions(QueueCapacity)
        { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
        public readonly CancellationTokenSource Cancellation = new();
        public Task Worker = Task.CompletedTask;
        public Task? StopTask;
        public ContinuousFdmReceiver? Receiver;
        public volatile bool Stopping;
        public bool MicrophoneStarted;
        public long InputSamples, AcceptedSamples, ConsumedSamples, LastInput, LastProgress, LastUi, DiscardedOnStop, HeaderStarts;
        public int Rate, BufferedSamples;
        public volatile string Stage = "Esperando bloques", Synchronizer = "Buscando cabecera", LastError = "";
        public ContinuousUpdate? Latest;
        public ContinuousUpdate? Displayed;
        public readonly ConcurrentQueue<ContinuousReception> Completed = new();
        public readonly object HistoryLock = new(); // Solo metadatos (máximo diez); nunca audio, WPF ni esperas.
    }
    private sealed record ContinuousUpdate(CarrierEnergyMap Map, LiveFdmReception? Reception, long Processed, long ReceptionCount);
    private ListeningSession? continuousListening;
    private readonly ObservableCollection<ContinuousReception> continuousHistory = [];
    private bool continuousHistorySelected;
    private bool continuousSegmentRetained;
    private string continuousLastError = "";
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
        if (continuousListening is not null || capture.IsRecording || captureBusy || analyzing || closing)
        { ContinuousStatusText.Text = "Finalizá captura/análisis antes de abrir escucha continua."; return; }
        InvalidateAudioAnalysis();
        continuousLastError = "";
        ReceiverSignalMonitor.BeginLive("Escucha continua · esperando muestras reales…");
        if (MonitorSourceComboBox.SelectedIndex == 0) DashboardSignalMonitor.BeginLive("Micrófono continuo · esperando muestras…");
        var session = new ListeningSession(); continuousListening = session;
        continuousHistory.Clear(); continuousHistorySelected = false;
        bool hamming = UseHammingReceiveCheckBox.IsChecked == true, erasures = RecoverReceiveErasuresCheckBox.IsChecked == true;
        ContinuousStatusText.Text = "ESCUCHA CONTINUA · abriendo micrófono…";
        ReceptionAView.Clear("Escucha automática · buscando cabecera A");
        ReceptionBView.Clear("Escucha automática · buscando cabecera B");
        ReceivedFdmText.Text = "Recepción automática · esperando una cabecera válida; no requiere pulsar Recibir mensajes.";
        session.Worker = ListenWorkerAsync(session, hamming, erasures);
        RefreshContinuousControls();
        try
        {
            await capture.StartContinuousAsync((samples, rate) => EnqueueContinuousSamples(session, samples, rate), session.Cancellation.Token);
            if (closing || !ReferenceEquals(continuousListening, session)) return;
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
        Interlocked.Add(ref session.InputSamples, samples.Length);
        Interlocked.Exchange(ref session.LastInput, Stopwatch.GetTimestamp());
        Volatile.Write(ref session.Rate, rate);
        if (session.Stopping) { Interlocked.Add(ref session.DiscardedOnStop, samples.Length); return; }
        Interlocked.Add(ref session.AcceptedSamples, samples.Length);
        if (samples.Length <= rate && session.Queue.Writer.TryWrite((samples, rate))) return;
        Interlocked.Add(ref session.AcceptedSamples, -samples.Length);
        session.LastError = "Sobrecarga de escucha: bloque rechazado; no se continúa con audio perdido. Detenga/reintente.";
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
                if (session.Receiver is null)
                {
                    session.Receiver = new(block.Rate, hamming, erasures);
                    session.Receiver.Received += entry =>
                    {
                        lock (session.HistoryLock)
                        {
                            session.Completed.Enqueue(entry);
                            while (session.Completed.Count > ContinuousFdmReceiver.HistoryLimit) session.Completed.TryDequeue(out _);
                        }
                    };
                    monitor = new(block.Rate, block.Rate * 30);
                }
                session.Stage = "Sincronización / símbolos";
                session.Receiver.Append(block.Samples, session.Cancellation.Token);
                Interlocked.Add(ref session.ConsumedSamples, block.Samples.Length);
                Interlocked.Exchange(ref session.LastProgress, Stopwatch.GetTimestamp());
                Volatile.Write(ref session.BufferedSamples, session.Receiver.BufferedSamples);
                Interlocked.Exchange(ref session.HeaderStarts, session.Receiver.EvaluatedHeaderStarts);
                Volatile.Write(ref session.Rate, block.Rate);
                session.Synchronizer = session.Receiver.Current?.Result.Synchronization.Reason ?? "Buscando cabecera";
                session.Stage = "Energía / resumen gráfico";
                if (monitorOrigin != session.Receiver.BufferOrigin)
                { monitor = new(block.Rate, block.Rate * 30); monitorOrigin = session.Receiver.BufferOrigin; monitorSource = new(); }
                var available = session.Receiver.AvailableSamples;
                monitor!.Append(available.Span[monitor.Count..], session.Cancellation.Token);
                if (monitor.Count == 0) continue;
                if (Stopwatch.GetTimestamp() < nextUi && session.Receiver.ReceptionCount == lastPublicationCount) { session.Stage = "Esperando bloques"; continue; }
                nextUi = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 5;
                lastPublicationCount = session.Receiver.ReceptionCount;
                var map = monitor.Snapshot(MonitorSignalOrigin.Microphone, "Micrófono continuo · tiempo local del buffer de escucha");
                map = map.WithSignal(map.Signal with { SourceId = monitorSource });
                // Solo una instantánea pendiente: nunca esperar al Dispatcher para recibir audio.
                Interlocked.Exchange(ref session.Latest, new(map, session.Receiver.Current,
                    Interlocked.Read(ref session.ConsumedSamples), session.Receiver.ReceptionCount));
                session.Stage = "Esperando bloques";
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
        finally { session.Stage = session.LastError.Length > 0 ? "Error" : session.Cancellation.IsCancellationRequested ? "Cancelado" : "Finalizado"; }
    }
    private void PublishContinuous(ListeningSession session, ContinuousUpdate update, ContinuousReception[] entries)
    {
        if (closing || session.Stopping || !ReferenceEquals(continuousListening, session)) return;
        var map = update.Map; var snapshot = update.Reception;
        AddContinuousHistory(entries);
        string last = continuousHistory.LastOrDefault()?.Label ?? "sin recepciones";
        ContinuousStatusText.Text = $"ESCUCHA CONTINUA · MICRÓFONO ACTIVO · recepciones {update.ReceptionCount} · última: {last}";
        var activity = new ReceptionActivity("ESCUCHA CONTINUA · buscando/recibiendo", "Micrófono (segmento gráfico local)", map.Signal.SampleRate,
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
        { continuousHistory.Add(entry); while (continuousHistory.Count > ContinuousFdmReceiver.HistoryLimit) continuousHistory.RemoveAt(0); }
    }
    private async void ContinuousHistory_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ContinuousHistoryComboBox.SelectedItem is not ContinuousReception entry) return;
        continuousHistorySelected = true;
        RefreshCapture();
        try
        {
            var map = await Task.Run(() => CarrierEnergyMap.Analyze(new(entry.Samples, entry.SampleRate, MonitorSignalOrigin.Microphone, $"Histórico #{entry.Id} · no captura nueva") { SourceId = entry }));
            if (closing || !ReferenceEquals(ContinuousHistoryComboBox.SelectedItem, entry)) return;
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
        }
        catch (Exception ex) { if (!closing && ReferenceEquals(ContinuousHistoryComboBox.SelectedItem, entry)) ContinuousStatusText.Text = $"Histórico no disponible: {ex.Message}"; }
    }
    private void ContinuousLive_Click(object sender, RoutedEventArgs e)
    {
        ContinuousHistoryComboBox.SelectedIndex = -1; continuousHistorySelected = false;
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
            ContinuousDiagnosticsText.Text = DescribeContinuousProgress(session);
            session.Cancellation.Dispose();
            session.Receiver = null; // Liberar el lector y su buffer; el monitor conserva solo su segmento observable.
            Interlocked.Exchange(ref session.Latest, null);
            session.Completed.Clear();
            if (session.LastError.Length == 0 && capture.Error is { } deviceError) session.LastError = deviceError;
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
        StartCaptureButton.IsEnabled = LoadWavButton.IsEnabled = UseHammingReceiveCheckBox.IsEnabled = RecoverReceiveErasuresCheckBox.IsEnabled = DecodeDuringCaptureCheckBox.IsEnabled = false;
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
        long accepted = Interlocked.Read(ref session.AcceptedSamples), consumed = Interlocked.Read(ref session.ConsumedSamples);
        int rate = Volatile.Read(ref session.Rate);
        string pending = rate > 0 ? $"{Math.Max(0, accepted - consumed) / (double)rate:0.000} s" : "no disponible";
        return $"Dispositivo: {(capture.IsRecording ? "capturando" : "apagado")} · tarea captura {(capture.SessionCompleted ? "finalizada" : "activa")}\n" +
            $"NAudio: {capture.ReceivedSamples:N0} muestras mono · último paquete hace {Age(capture.LastBufferTimestamp)}\n" +
            $"Callback continuo: {Interlocked.Read(ref session.InputSamples):N0} · aceptadas en cola: {accepted:N0} · consumidas: {consumed:N0}\n" +
            $"Cola: {session.Queue.Reader.Count}/{ListeningSession.QueueCapacity} bloques · {(session.Stopping ? "sin completar al parar" : "pendiente")} {pending}\n" +
            $"Worker: {session.Stage} · último progreso hace {Age(Interlocked.Read(ref session.LastProgress))}\n" +
            $"Última entrada hace {Age(Interlocked.Read(ref session.LastInput))} · publicación WPF hace {Age(Interlocked.Read(ref session.LastUi))}\n" +
            $"Sincronizador: {session.Synchronizer} · inicios evaluados: {Interlocked.Read(ref session.HeaderStarts):N0}\nBuffer conservado: {Volatile.Read(ref session.BufferedSamples):N0} muestras / {(long)rate * 30:N0}\n" +
            $"Cancelación: {session.Cancellation.IsCancellationRequested} · descartadas al detener: {Interlocked.Read(ref session.DiscardedOnStop):N0}\n" +
            $"Último error: {(session.LastError.Length > 0 ? session.LastError : capture.Error ?? "ninguno")}";
    }
}
