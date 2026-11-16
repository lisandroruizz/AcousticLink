using System.Windows;
using System.Windows.Controls;
using AcousticLink.App.Audio;
using AcousticLink.App.Protocol;
using AcousticLink.App.Signals;

namespace AcousticLink.App;

public partial class MainWindow
{
    private bool IsBfskTransmit => TransmitModeComboBox?.SelectedIndex == 0;
    private bool IsBfskReceive => ReceiverModeComboBox?.SelectedIndex == 0;

    private void TransmitMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (TransmitChannelBPanel is null || FdmTextInfoText is null) return;
        bool bfsk = IsBfskTransmit;
        TransmitChannelBPanel.Visibility = bfsk ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetColumnSpan(DashboardTransmitACard, bfsk ? 3 : 1);
        FdmProfilePanel.Visibility = bfsk ? Visibility.Collapsed : Visibility.Visible;
        TransmitConfigurationTitle.Text = bfsk ? "CONFIGURACIÓN BFSK · CANAL ÚNICO" : "CONFIGURACIÓN FDM";
        TransmitModeSummaryText.Text = bfsk ? "CRC-32 permanente · BFSK de un canal · 48 kHz · 0,10 s/símbolo"
            : "CRC-32 permanente · FDM de dos canales · 48 kHz · 0,10 s/símbolo";
        TransmitPaddingNote.Text = bfsk ? "Cabecera sin Hamming. Una sola portadora de datos por símbolo; sin relleno ni canal B."
            : "Cabecera sin Hamming. El relleno FDM queda fuera del CRC y del bloque protegido.";
        UpdateFdmTextInfo();
    }

    private void ReceiverMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ReceptionChannelBPanel is null || ReceiverSignalMonitor is null) return;
        if (capture.IsRecording || continuousListening is not null || captureBusy || analyzing) return;
        InvalidateAudioAnalysis();
        ApplyReceptionModeVisual(IsBfskReceive);
        ReceivedFdmText.Text = IsBfskReceive
            ? "BFSK — un canal seleccionado. El WAV no informa el modo; analizá solo si conocés su origen."
            : "FDM — dos canales seleccionado. El WAV no informa el modo; analizá solo si conocés su origen.";
        RefreshCapture();
    }

    private void ApplyReceptionModeVisual(bool bfsk)
    {
        ReceptionChannelBPanel.Visibility = bfsk ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetColumnSpan(ReceptionChannelAPanel, bfsk ? 3 : 1);
        Grid.SetColumnSpan(DashboardReceptionACard, bfsk ? 3 : 1);
        ReceptionBDetailTab.Visibility = bfsk ? Visibility.Collapsed : Visibility.Visible;
        ReceivedFrameExplorer.SingleChannel = bfsk;
        ReceiverSignalMonitor.SingleChannel = bfsk;
        LiveReceptionInspector.SingleChannel = bfsk;
        ShowMonitorSource();
        if (bfsk) ReceptionBView.Clear("Canal B no utilizado en BFSK");
    }

    private static string DescribeBfskReception(FdmTextChannelResult channel, string synchronization) =>
        $"BFSK — un canal · sincronización: {synchronization}\n" +
        $"Estado: {channel.State} · longitud: {channel.ByteLength?.ToString() ?? "sin determinar"} bytes · " +
        $"CRC: {(channel.CrcValid is true ? "válido" : channel.CrcValid is false ? "incorrecto" : "no comprobado")}\n" +
        (channel.State == FdmTextReceiveState.Valid ? $"Texto validado: {channel.Text}\n" : "") + channel.Reason;

    private void UpdateBfskTextInfo()
    {
        var input = AcousticLink.App.Visualization.FdmTextInputValidation.Check(FdmTextATextBox.Text, "BFSK");
        fdmTextsValid = input.Error is null;
        TransmitFdmTextButton.IsEnabled = fdmTextsValid && !playing;
        TransmitABytesText.Text = input.Error ?? $"{input.ByteCount} / {FdmTextFrameBuilder.MaximumUtf8Bytes} bytes UTF-8";
        TransmitABytesText.Foreground = (System.Windows.Media.Brush)FindResource(input.Error is null ? "MutedBrush" : "WarningBrush");
        if (!fdmTextsValid)
        {
            FdmTextInfoText.Text = input.Error;
            TransmitDurationText.Text = "Duración no disponible: corregí el mensaje.";
            return;
        }
        var frame = new BfskTextModem().Build(FdmTextATextBox.Text, UseHammingTransmitCheckBox.IsChecked == true);
        FdmTextInfoText.Text = $"Canal BFSK 1200/1900 Hz · {frame.Utf8ByteCount} bytes UTF-8 · " +
            $"bloque original {frame.OriginalBlockBitCount} bits · " +
            (frame.UsesHamming ? $"Hamming {frame.EncodedBlockBitCount} bits · " : "sin Hamming · ") +
            $"cabecera {frame.HeaderBitCount} bits · total {frame.RealBitCount} símbolos · " +
            $"CRC-32 incluido · sin relleno ni canal B.";
        TransmitDurationText.Text = $"{frame.RealBitCount * BfskSequenceDecoder.BitDurationSeconds:0.0} s de transmisión";
    }

    private async Task TransmitBfskTextAsync()
    {
        string message = FdmTextATextBox.Text;
        bool hamming = UseHammingTransmitCheckBox.IsChecked == true;
        UpdateBfskTextInfo();
        if (!fdmTextsValid) { FdmTextStatusText.Text = FdmTextInfoText.Text; return; }
        SetPlaybackControls(false);
        FdmTextStatusText.Text = "Generando y reproduciendo trama BFSK de un canal…";
        try
        {
            var samples = await Task.Run(() => new BfskTextModem().Generate(message, hamming), windowLifetime.Token);
            if (closing) return;
            RememberBfskSignal(samples, $"Mensaje BFSK digital · Hamming {(hamming ? "activado" : "desactivado")}",
                new BfskTextModem().Build(message, hamming).FrameBits);
            await playback.PlayAsync(samples, BfskToneGenerator.SampleRate, windowLifetime.Token);
            FdmTextStatusText.Text = "Reproducción BFSK finalizada. Recepción acústica aún no verificada.";
        }
        catch (OperationCanceledException) when (closing) { }
        catch (Exception ex) { FdmTextStatusText.Text = ex.Message; ShowPlaybackError(ex); }
        finally { SetPlaybackControls(true); }
    }

    private void RememberBfskSignal(float[] samples, string description, string frameBits)
    {
        generatedSignal = new(samples, BfskToneGenerator.SampleRate,
            AcousticLink.App.Visualization.MonitorSignalOrigin.Generated, description)
        { SourceId = samples, ChannelA = samples, BfskFrameBits = frameBits };
        GeneratedSignalMonitor.SingleChannel = true;
        _ = AnalyzeSignalAsync(generatedSignal, true);
    }

    private async void SaveGeneratedWav_Click(object sender, RoutedEventArgs e)
    {
        var signal = generatedSignal;
        if (signal is null) { FdmTextStatusText.Text = "Prepará o transmití una señal antes de guardar el WAV."; return; }
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Audio WAV (*.wav)|*.wav",
            FileName = signal.Description.Contains("BFSK", StringComparison.Ordinal) ? "mensaje-bfsk.wav" : "mensaje-fdm.wav" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            await Task.Run(() =>
            {
                var samples = signal.Samples.ToArray();
                using var writer = new NAudio.Wave.WaveFileWriter(dialog.FileName,
                    NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(signal.SampleRate, 1));
                writer.WriteSamples(samples, 0, samples.Length);
            });
            if (!closing) FdmTextStatusText.Text = $"WAV digital guardado: {dialog.FileName}. El archivo no almacena la modalidad.";
        }
        catch (Exception ex) { if (!closing) FdmTextStatusText.Text = $"No se pudo guardar WAV: {ex.Message}"; }
    }

    private async Task ReceiveBfskTextAsync()
    {
        var recording = audioSelection.Current?.Audio;
        if (recording is null || capture.IsRecording || captureBusy || analyzing) return;
        StopLiveReception();
        var diagnostics = StartHistoricalDiagnostics(recording);
        analyzing = true; RefreshCapture();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(windowLifetime.Token);
        textReceiveCancellation = cancellation;
        CancelReceiveFdmTextButton.IsEnabled = true;
        ReceivedFdmText.Text = "Buscando cabecera BFSK y procesando la señal seleccionada…";
        ReceptionAView.Clear("Analizando canal BFSK…"); ReceptionBView.Clear("Canal B no utilizado");
        ReceivedFrameExplorer.Clear("Analizando canal BFSK…");
        bool hamming = UseHammingReceiveCheckBox.IsChecked == true;
        bool erasures = hamming && RecoverReceiveErasuresCheckBox.IsChecked == true;
        try
        {
            var result = await Task.Run(() => new BfskTextReceiver().Receive(recording.GetSamples(),
                recording.SampleRate, cancellation.Token, hamming, erasures), cancellation.Token);
            if (closing || cancellation.IsCancellationRequested || !ReferenceEquals(audioSelection.Current?.Audio, recording)) return;
            var presented = BfskPresentation.AsSingleChannel(result);
            ShowFdmReception(presented, recording);
            FinishHistoricalDiagnostics(diagnostics, recording, presented);
        }
        catch (OperationCanceledException) { if (!closing) ReceivedFdmText.Text = "Recepción BFSK cancelada."; diagnostics.Stop("Análisis posterior cancelado."); ShowReceiverDiagnostics(diagnostics); }
        catch (Exception ex) { if (!closing) ReceivedFdmText.Text = $"No se pudo recibir BFSK: {ex.Message}"; diagnostics.Stop("Análisis posterior detenido por error: " + ex.Message); ShowReceiverDiagnostics(diagnostics); }
        finally
        {
            textReceiveCancellation = null;
            CancelReceiveFdmTextButton.IsEnabled = false;
            analyzing = false;
            if (!closing) RefreshCapture();
        }
    }
}
