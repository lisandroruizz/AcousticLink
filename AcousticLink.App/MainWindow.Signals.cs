using System.Windows;
using System.Windows.Controls;
using AcousticLink.App.Audio;
using AcousticLink.App.Visualization;

namespace AcousticLink.App;

public partial class MainWindow
{
    private CapturedAudio? monitorAudio;
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
        var data = generated ? generatedMap : observedMap;
        if (!ReferenceEquals(DashboardSignalMonitor.Data, data) || data is null)
        { if (data is not null) DashboardSignalMonitor.Show(data); else DashboardSignalMonitor.Clear(generated ? generatedMonitorStatus : observedMonitorStatus); }
        CancelMonitorButton.IsEnabled = generated ? generatedMonitorCancellation is not null : observedMonitorCancellation is not null;
    }
    private void InvalidateObservedMonitor()
    {
        observedMonitorCancellation?.Cancel(); observedMonitorCancellation = null;
        observedMap = null; observedSignal = null; monitorAudio = null;
        observedMonitorStatus = "Audio cambiado. Esperando una grabación finalizada para calcular el monitor.";
        ShowMonitorSource();
    }
    private void RefreshObservedMonitor()
    {
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
            observedSignal = new(samples, audio.SampleRate, audio.GetMetrics().StopReason == CaptureStopReason.Imported ? MonitorSignalOrigin.ImportedWav : MonitorSignalOrigin.Microphone, source);
            await AnalyzeSignalAsync(observedSignal, false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!closing && ReferenceEquals(monitorAudio, audio)) { observedMonitorStatus = $"No se pudo preparar el monitor: {ex.Message}"; ShowMonitorSource(); } }
    }
    private void RememberGeneratedSignal(float[] samples, int sampleRate, string description)
    {
        if (closing) return;
        // Es el mismo arreglo entregado a reproducción. Ni playback ni el monitor lo modifican.
        generatedSignal = new(samples, sampleRate, MonitorSignalOrigin.Generated, description);
        _ = AnalyzeSignalAsync(generatedSignal, true);
    }
    private async Task AnalyzeSignalAsync(MonitorSignal signal, bool generated)
    {
        var previous = generated ? generatedMonitorCancellation : observedMonitorCancellation;
        previous?.Cancel();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(windowLifetime.Token);
        if (generated) { generatedMonitorCancellation = cancellation; generatedMap = null; generatedMonitorStatus = "Calculando energías de la señal digital…"; GeneratedSignalMonitor.Clear(generatedMonitorStatus); }
        else { observedMonitorCancellation = cancellation; observedMap = null; observedMonitorStatus = "Calculando energías del audio seleccionado…"; }
        ShowMonitorSource();
        try
        {
            var result = await Task.Run(() => CarrierEnergyMap.Analyze(signal, cancellation.Token), cancellation.Token);
            if (closing || cancellation.IsCancellationRequested || !ReferenceEquals(signal, generated ? generatedSignal : observedSignal)) return;
            if (generated) { generatedMap = result; GeneratedSignalMonitor.Show(result); } else observedMap = result;
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
