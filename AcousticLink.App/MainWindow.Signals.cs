using AcousticLink.App.Signals;
using AcousticLink.App.Protocol;
using System.Windows;
using System.Windows.Controls;
using AcousticLink.App.Audio;
using AcousticLink.App.Visualization;

namespace AcousticLink.App;

public partial class MainWindow
{
    private CapturedAudio? monitorAudio;
    private bool preparingPreview;
    private readonly System.Windows.Threading.DispatcherTimer monitorTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private IncrementalSignalMonitor? incrementalMonitor;
    private object? streamingKey;
    private CarrierEnergyMap? streamingMap;
    private bool monitorBusy, streamActive;
    private int streamRate;
    private long streamRevision;
    private void InitializeSignalMonitor()
    {
        DashboardSignalMonitor.FrameDisplayed += (data, values, start, end, live) =>
        {
            double position = DashboardSignalMonitor.CursorSeconds;
            CarrierEnergyMap? complete = null;
            if (data?.Signal.Origin == MonitorSignalOrigin.Generated)
            {
                complete = generatedMap;
                if (playback.Progress is { } p && generatedSignal is not null && generatedSignal.Samples.Equals(new ReadOnlyMemory<float>(p.Samples)))
                    position = p.SampleCount / (double)p.SampleRate;
            }
            else if (live && data is not null) position = data.Signal.Duration;
            MiniSignalMonitor.Update(data, values, start, end, live, position, complete);
        };
        ReceiverSignalMonitor.FrameDisplayed += (data, values, start, end, live) =>
        { if (MainSectionsTabs.SelectedIndex == 2 && ReceiverTools.SelectedIndex == 2) MiniSignalMonitor.Update(data, values, start, end, live, live ? data?.Signal.Duration : ReceiverSignalMonitor.CursorSeconds); };
        monitorTimer.Tick += async (_, _) => await RefreshStreamingMonitorAsync();
        monitorTimer.Start();
    }
    private bool IsGeneratedPlaying => playback.Progress is { IsActive: true } p && generatedSignal is not null && generatedSignal.Samples.Equals(new ReadOnlyMemory<float>(p.Samples));
    private async Task RefreshStreamingMonitorAsync()
    {
        if (closing || monitorBusy) return;
        var recording = capture.IsRecording ? capture.Recording : streamActive ? streamingKey as CapturedAudio : null;
        var progress = playback.Progress;
        bool output = recording is null && progress is not null && generatedSignal is not null && generatedSignal.Samples.Equals(new ReadOnlyMemory<float>(progress.Samples));
        bool active = capture.IsRecording && recording is not null || output && progress!.IsActive;
        object? key = recording is not null ? recording : output ? progress!.Samples : null;
        if (!active && (!streamActive || key is null && streamingKey is null)) return;
        // Si acaba la captura, todavía leer el último prefijo publicado antes de finalizar.
        if (recording is null && !output && streamingKey is CapturedAudio ended) { recording = ended; key = ended; }
        if (key is null) return;
        int rate = recording?.SampleRate ?? progress!.SampleRate;
        if (!ReferenceEquals(key, streamingKey))
        {
            streamingKey = key; streamRate = rate; streamRevision++;
            incrementalMonitor = new(rate, recording?.SampleCapacity ?? progress!.Samples.Length);
            streamingMap = null; streamActive = true;
            MonitorSourceComboBox.SelectedIndex = recording is null ? 1 : 0;
            if (recording is not null) ReceiverSignalMonitor.BeginLive("CAPTURADA · ENTRADA ACÚSTICA · capturando…");
            DashboardSignalMonitor.BeginLive(recording is null ? "Iniciando salida digital…" : "Capturando micrófono · esperando muestras…");
            if (recording is null) GeneratedSignalMonitor.BeginLive("Salida digital en curso…");
            MiniSignalMonitor.SetWaiting(recording is null ? MonitorSignalOrigin.Generated : MonitorSignalOrigin.Microphone);
        }
        var worker = incrementalMonitor!; long revision = streamRevision;
        int published = recording?.GetStatus().Count ?? progress!.SampleCount;
        if (active && streamingMap is not null && published == worker.Count) return;
        var signal = generatedSignal;
        monitorBusy = true;
        try
        {
            var data = await Task.Run(() =>
            {
                windowLifetime.Token.ThrowIfCancellationRequested();
                if (recording is not null)
                {
                    // Solo el sufijo nuevo. Una copia acotada; el callback nunca dibuja ni calcula energía.
                    int available = recording.GetStatus().Count - worker.Count;
                    var added = new float[Math.Max(0, available)]; int read = recording.CopySamples(worker.Count, added);
                    worker.Append(added.AsSpan(0, read), windowLifetime.Token);
                }
                else
                {
                    int target = Math.Clamp(progress!.SampleCount, worker.Count, progress.Samples.Length);
                    worker.Append(progress.Samples.AsSpan(worker.Count, target - worker.Count), windowLifetime.Token);
                }
                return worker.Count == 0 ? null : worker.Snapshot(recording is null ? MonitorSignalOrigin.Generated : MonitorSignalOrigin.Microphone,
                    recording is null ? progress!.PositionSource : "Captura real · energía incremental", recording is null ? signal!.ChannelA : default, recording is null ? signal!.ChannelB : default);
            }, windowLifetime.Token);
            if (closing || revision != streamRevision) return;
            if (data is not null) data = data.WithSignal(data.Signal with { SourceId = key, Transmission = recording is null ? signal?.Transmission : null });
            if (data is not null)
            {
                streamingMap = data; if (recording is not null) ReceiverSignalMonitor.UpdateLive(data); QueueReceptionObservation(data, key, !active);
                bool selected = MonitorSourceComboBox.SelectedIndex == (recording is null ? 1 : 0);
                if (selected) DashboardSignalMonitor.UpdateLive(data);
                if (recording is null) GeneratedSignalMonitor.UpdateLive(data);
                if (!active)
                {
                    var completed = recording is null && generatedMap is not null && generatedMap.Signal.Samples.Equals(new ReadOnlyMemory<float>(progress!.Samples)) ? generatedMap : data;
                    if (recording is not null) ReceiverSignalMonitor.FinishLive(completed);
                    if (selected) DashboardSignalMonitor.FinishLive(completed);
                    if (recording is null) GeneratedSignalMonitor.FinishLive(completed);
                }
            }
            if (!active && data is null) { DashboardSignalMonitor.Clear("Operación detenida sin muestras disponibles."); if (output) GeneratedSignalMonitor.Clear("Salida detenida sin posición disponible."); }
            streamActive = active;
            if (!active) { streamingKey = null; incrementalMonitor = null; }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!closing) { streamActive = false; streamingKey = null; DashboardSignalMonitor.Clear($"Monitor: {ex.Message}"); } }
        finally { monitorBusy = false; }
    }
    private async void PrepareSignalPreview_Click(object sender, RoutedEventArgs e)
    {
        if (preparingPreview || playing) return;
        UpdateFdmTextInfo(); if (!fdmTextsValid) { FdmTextStatusText.Text = FdmTextInfoText.Text; return; }
        string a = FdmTextATextBox.Text, b = FdmTextBTextBox.Text; bool hamming = UseHammingTransmitCheckBox.IsChecked == true; var profile = SelectedFdmProfile;
        preparingPreview = true;
        try
        {
            var frames = new FdmTextFrameBuilder().Build(a, b, hamming);
            var samples = await Task.Run(() => fdmBitstreamGenerator.GenerateTextFrames(a, b, hamming, profile), windowLifetime.Token);
            if (closing) return;
            RememberDetailedSignal(samples, 48000, $"Señal digital preparada · sin reproducción · {FdmAmplitudeProfiles.Get(profile).Label} · Hamming {hamming}", frames.ChannelA.TransmittedBits, frames.ChannelB.TransmittedBits, profile, frames: frames);
            TransmitterTools.SelectedIndex = 2; FdmTextStatusText.Text = "Vista previa preparada sin reproducir sonido.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!closing) FdmTextStatusText.Text = $"No se pudo preparar la vista previa: {ex.Message}"; }
        finally { preparingPreview = false; }
    }
    private MonitorSignal? observedSignal, generatedSignal;
    private CarrierEnergyMap? observedMap, generatedMap;
    private CancellationTokenSource? observedMonitorCancellation, generatedMonitorCancellation;
    private string observedMonitorStatus = "Sin audio finalizado. Capture o importe un WAV.", generatedMonitorStatus = "Sin buffer generado. Prepare una emisión FDM para observarla.";

    private void OpenSignalMonitor_Click(object sender, RoutedEventArgs e) => DashboardTools.SelectedIndex = 1;
    private void OpenGeneratedMonitor_Click(object sender, RoutedEventArgs e) => TransmitterTools.SelectedIndex = 2;
    private void MonitorSource_Changed(object sender, SelectionChangedEventArgs e) { if (DashboardSignalMonitor is not null) ShowMonitorSource(); }
    private void ShowMonitorSource()
    {
        bool generated = MonitorSourceComboBox.SelectedIndex == 1;
        if (streamActive && streamingMap is not null && (streamingMap.Signal.Origin == MonitorSignalOrigin.Generated) == generated)
        { if (!ReferenceEquals(DashboardSignalMonitor.Data, streamingMap)) DashboardSignalMonitor.UpdateLive(streamingMap); return; }
        if (generated && IsGeneratedPlaying || !generated && capture.IsRecording) return;
        var data = generated ? generatedMap : observedMap;
        if (!ReferenceEquals(DashboardSignalMonitor.Data, data) || data is null)
        { if (data is not null) { DashboardSignalMonitor.Show(data); QueueReceptionObservation(data, data.Signal.SourceId ?? data.Signal, true); } else DashboardSignalMonitor.Clear(generated ? generatedMonitorStatus : observedMonitorStatus); }
        CancelMonitorButton.IsEnabled = generated ? generatedMonitorCancellation is not null : observedMonitorCancellation is not null;
    }
    private void InvalidateObservedMonitor()
    {
        observedMonitorCancellation?.Cancel(); observedMonitorCancellation = null;
        ResetReceptionObservation(); ReceiverSignalMonitor.Clear();
        observedMap = null; observedSignal = null; monitorAudio = null; if (streamingKey is CapturedAudio) { streamRevision++; streamingKey = null; streamActive = false; incrementalMonitor = null; streamingMap = null; }
        observedMonitorStatus = "Audio cambiado. Esperando una grabación finalizada para calcular el monitor.";
        ShowMonitorSource();
    }
    private void RefreshObservedMonitor()
    {
        if (continuousListening is not null || continuousHistorySelected || continuousSegmentRetained) return;
        var selection = audioSelection.Current;
                if (closing || capture.IsRecording || captureBusy || selection is null || ReferenceEquals(monitorAudio, selection.Audio)) return;
        if (selection.Audio.GetMetrics() is not { Count: > 0, StopReason: not CaptureStopReason.None }) return;
        monitorAudio = selection.Audio;
        _ = PrepareObservedMonitorAsync(selection.Audio, selection.Source);
    }
    private async Task PrepareObservedMonitorAsync(CapturedAudio audio, string source)
    {
        // Una única instantánea del audio finalizado; nunca desde el callback de captura.
        try
        {
            var samples = await Task.Run(audio.GetSamples, windowLifetime.Token);
            if (closing || !ReferenceEquals(monitorAudio, audio)) return;
            await Dispatcher.InvokeAsync(() =>
            {
                if (closing || !ReferenceEquals(monitorAudio, audio)) return;
                observedSignal = new(samples, audio.SampleRate, audio.GetMetrics().StopReason == CaptureStopReason.Imported ? MonitorSignalOrigin.ImportedWav : MonitorSignalOrigin.Microphone, source) { SourceId = audio };
                _ = AnalyzeSignalAsync(observedSignal, false);
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!closing && ReferenceEquals(monitorAudio, audio)) { observedMonitorStatus = $"No se pudo preparar el monitor: {ex.Message}"; ShowMonitorSource(); } }
    }
    private void RememberGeneratedSignal(float[] samples, int sampleRate, string description) => RememberDetailedSignal(samples, sampleRate, description);
    private async void RememberDetailedSignal(float[] samples, int sampleRate, string description, string? bitsA = null, string? bitsB = null, FdmAmplitudeProfile profile = FdmAmplitudeProfile.Original, double symbolSeconds = .1, FdmTextFrames? frames = null)
    {
        if (closing) return;
        // Es el mismo arreglo entregado a reproducción. Ni playback ni el monitor lo modifican.
        generatedSignal = new(samples, sampleRate, MonitorSignalOrigin.Generated, description)
        { SourceId = samples, Transmission = bitsA is not null && bitsB is not null ? new(bitsA, bitsB, symbolSeconds, frames) : null };
        var initial = generatedSignal;
        if (bitsA is not null && bitsB is not null)
        {
            generatedMap = null; if (!IsGeneratedPlaying) GeneratedSignalMonitor.Clear("Preparando contribuciones digitales…"); ShowMonitorSource();
            try
            {
                var prepared = await Task.Run(() =>
                {
                    windowLifetime.Token.ThrowIfCancellationRequested();
                    var gains = FdmAmplitudeProfiles.Get(profile); var generator = new BfskToneGenerator();
                    var a = generator.GenerateSequence(bitsA, 1200, 1900, symbolSeconds, gains.ChannelA, bitsA.Length);
                    var b = generator.GenerateSequence(bitsB, 3000, 3700, symbolSeconds, gains.ChannelB, bitsB.Length);
                    return a.Length == samples.Length && b.Length == samples.Length && Enumerable.Range(0, samples.Length).All(i => a[i] + b[i] == samples[i])
                        ? initial with { ChannelA = a, ChannelB = b } : initial;
                }, windowLifetime.Token);
                if (closing || !ReferenceEquals(initial, generatedSignal)) return;
                generatedSignal = prepared;
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { if (!closing) { generatedMonitorStatus = $"Contribuciones no disponibles: {ex.Message}"; ShowMonitorSource(); } return; }
        }
        _ = AnalyzeSignalAsync(generatedSignal, true);
    }
    private async Task AnalyzeSignalAsync(MonitorSignal signal, bool generated)
    {
        var previous = generated ? generatedMonitorCancellation : observedMonitorCancellation;
        previous?.Cancel();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(windowLifetime.Token);
        if (generated) { generatedMonitorCancellation = cancellation; generatedMap = null; generatedMonitorStatus = "Calculando energías de la señal digital…"; if (!IsGeneratedPlaying) GeneratedSignalMonitor.Clear(generatedMonitorStatus); }
        else { observedMonitorCancellation = cancellation; observedMap = null; observedMonitorStatus = "Calculando energías del audio seleccionado…"; }
        ShowMonitorSource();
        try
        {
            var result = await Task.Run(() => CarrierEnergyMap.Analyze(signal, cancellation.Token), cancellation.Token);
            if (closing || cancellation.IsCancellationRequested || !ReferenceEquals(signal, generated ? generatedSignal : observedSignal)) return;
            if (generated) { generatedMap = result; if (!IsGeneratedPlaying && !streamActive) GeneratedSignalMonitor.Show(result); QueueReceptionObservation(result, result.Signal.SourceId ?? signal, true); } else { observedMap = result; ReceiverSignalMonitor.Show(result); QueueReceptionObservation(result, monitorAudio ?? (object)signal, true); }
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(cancellation, generated ? generatedMonitorCancellation : observedMonitorCancellation))
            {
                if (generated) generatedMonitorStatus = "Monitor cancelado. El buffer digital permanece disponible para reintentar.";
                else observedMonitorStatus = "Monitor cancelado. El audio seleccionado permanece intacto.";
            }
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(cancellation, generated ? generatedMonitorCancellation : observedMonitorCancellation))
            { if (generated) generatedMonitorStatus = $"Monitor no disponible: {ex.Message}"; else observedMonitorStatus = $"Monitor no disponible: {ex.Message}"; }
        }
        finally
        {
            bool current = ReferenceEquals(cancellation, generated ? generatedMonitorCancellation : observedMonitorCancellation);
            if (current) { if (generated) generatedMonitorCancellation = null; else observedMonitorCancellation = null; }
            cancellation.Dispose();
            if (!closing && current) { ShowMonitorSource(); if (generated && generatedMap is null) GeneratedSignalMonitor.Clear(generatedMonitorStatus); }
        }
    }
    private void AnalyzeMonitor_Click(object sender, RoutedEventArgs e)
    {
        var signal = MonitorSourceComboBox.SelectedIndex == 1 ? generatedSignal : observedSignal;
        if (signal is not null) _ = AnalyzeSignalAsync(signal, MonitorSourceComboBox.SelectedIndex == 1);
        else RefreshObservedMonitor();
    }
    private void CancelMonitor_Click(object sender, RoutedEventArgs e)
    { if (MonitorSourceComboBox.SelectedIndex == 1) generatedMonitorCancellation?.Cancel(); else observedMonitorCancellation?.Cancel(); }
    private void CancelGeneratedMonitor_Click(object sender, RoutedEventArgs e) => generatedMonitorCancellation?.Cancel();
    private void RetryGeneratedMonitor_Click(object sender, RoutedEventArgs e)
    { if (generatedSignal is not null) _ = AnalyzeSignalAsync(generatedSignal, true); }
}
