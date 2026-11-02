using System.Windows;
using System.Windows.Controls;
using AcousticLink.App.Audio;
using AcousticLink.App.Signals;

namespace AcousticLink.App;

public partial class MainWindow : Window
{
    private readonly BfskToneGenerator toneGenerator = new();
    private readonly FdmToneMixer fdmMixer = new();
    private readonly FdmBitstreamGenerator fdmBitstreamGenerator = new();
    private readonly BfskFrameBuilder frameBuilder = new();
    private readonly AudioPlaybackService playback = new();
    private readonly CancellationTokenSource windowLifetime = new();
    private readonly AudioCaptureService capture = new();
    private readonly System.Windows.Threading.DispatcherTimer captureTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private bool closing;
    private bool closeReady;
    private bool captureBusy;
    private bool wasCaptureRecording;
    private bool analyzing;
    private CancellationTokenSource? fdmSyncCancellation;
    private readonly BfskToneDetector toneDetector = new();

    public MainWindow()
    {
        InitializeComponent();
        UpdateFramePreview();
        UpdateFdmSequencesInfo();
        UpdateFdmFramesInfo();
        captureTimer.Tick += (_, _) => RefreshCapture();
        captureTimer.Start();
    }

    private async void PlayBit0_Click(object sender, RoutedEventArgs e) => await PlayToneAsync(false);
    private async void PlayBit1_Click(object sender, RoutedEventArgs e) => await PlayToneAsync(true);

    private async Task PlayToneAsync(bool bit)
    {
        SetPlaybackControls(false);
        StatusText.Text = "Reproduciendo tono...";
        try
        {
            float[] samples = toneGenerator.Generate(bit, durationSeconds: 1);
            await playback.PlayAsync(samples, BfskToneGenerator.SampleRate, windowLifetime.Token);
            StatusText.Text = "Reproducción finalizada.";
        }
        catch (OperationCanceledException) when (windowLifetime.IsCancellationRequested)
        {
            // Cierre normal de la ventana.
        }
        catch (Exception ex)
        {
            ShowPlaybackError(ex);
        }
        finally
        {
            SetPlaybackControls(true);
        }
    }

    private async void PlaySequence_Click(object sender, RoutedEventArgs e) => await PlaySequenceAsync(false);

    private async void PlayFrame_Click(object sender, RoutedEventArgs e) => await PlaySequenceAsync(true);

    private async Task PlaySequenceAsync(bool framed)
    {
        float[] samples;
        try
        {
            string bits = framed ? frameBuilder.Build(BitSequenceTextBox.Text) : BitSequenceTextBox.Text;
            samples = toneGenerator.GenerateSequence(bits);
        }
        catch (ArgumentException ex)
        {
            StatusText.Text = ex.Message;
            return;
        }

        SetPlaybackControls(false);
        StatusText.Text = framed ? "Reproduciendo trama de prueba..." : "Reproduciendo secuencia BFSK...";
        try
        {
            await playback.PlayAsync(samples, BfskToneGenerator.SampleRate, windowLifetime.Token);
            StatusText.Text = framed ? "Reproducción de la trama finalizada." : "Reproducción de la secuencia finalizada.";
        }
        catch (OperationCanceledException) when (windowLifetime.IsCancellationRequested)
        {
            // Cierre normal de la ventana.
        }
        catch (Exception ex)
        {
            ShowPlaybackError(ex);
        }
        finally
        {
            SetPlaybackControls(true);
        }
    }

    private async void PlayFdm_Click(object sender, RoutedEventArgs e)
    {
        bool bitA = FdmBitAComboBox.SelectedIndex == 1;
        bool bitB = FdmBitBComboBox.SelectedIndex == 1;
        SetPlaybackControls(false);
        FdmStatusText.Text = $"Reproduciendo FDM A={(bitA ? 1 : 0)}, B={(bitB ? 1 : 0)}...";
        try
        {
            float[] samples = fdmMixer.Generate(bitA, bitB);
            await playback.PlayAsync(samples, FdmToneMixer.SampleRate, windowLifetime.Token);
            FdmStatusText.Text = "Reproducción FDM finalizada.";
        }
        catch (OperationCanceledException) when (windowLifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            FdmStatusText.Text = "No se pudo reproducir la prueba FDM.";
            ShowPlaybackError(ex);
        }
        finally { SetPlaybackControls(true); }
    }

    private void FdmSequences_TextChanged(object sender, TextChangedEventArgs e) => UpdateFdmSequencesInfo();

    private void UpdateFdmSequencesInfo()
    {
        if (FdmSequencesInfoText is null) return;
        try
        {
            FdmBitstreamGenerator.ValidateSequences(FdmSequenceATextBox.Text, FdmSequenceBTextBox.Text);
            FdmSequencesInfoText.Text = $"{FdmSequenceATextBox.Text.Length} bits por canal · duración estimada: " +
                $"{FdmSequenceATextBox.Text.Length * FdmBitstreamGenerator.SymbolDurationSeconds:0.0} s";
        }
        catch (ArgumentException ex) { FdmSequencesInfoText.Text = ex.Message; }
    }

    private async void PlayFdmSequences_Click(object sender, RoutedEventArgs e)
    {
        string a = FdmSequenceATextBox.Text;
        string b = FdmSequenceBTextBox.Text;
        try { FdmBitstreamGenerator.ValidateSequences(a, b); }
        catch (ArgumentException ex) { FdmSequencesStatusText.Text = ex.Message; return; }
        SetPlaybackControls(false);
        FdmSequencesStatusText.Text = "Reproduciendo secuencias FDM...";
        CancellationToken token = windowLifetime.Token;
        try
        {
            float[] samples = await Task.Run(() => fdmBitstreamGenerator.Generate(a, b), token);
            await playback.PlayAsync(samples, FdmBitstreamGenerator.SampleRate, token);
            FdmSequencesStatusText.Text = "Reproducción de ambas secuencias finalizada.";
        }
        catch (OperationCanceledException) when (closing) { }
        catch (Exception ex)
        {
            FdmSequencesStatusText.Text = "No se pudieron reproducir las secuencias FDM.";
            ShowPlaybackError(ex);
        }
        finally { SetPlaybackControls(true); }
    }

    private void FdmFrames_TextChanged(object sender, TextChangedEventArgs e) => UpdateFdmFramesInfo();

    private void UpdateFdmFramesInfo()
    {
        if (FdmFramesInfoText is null) return;
        try
        {
            var frames = FdmBitstreamGenerator.BuildFrames(FdmFrameATextBox.Text, FdmFrameBTextBox.Text);
            FdmFramesInfoText.Text = $"A · preámbulo: {BfskFrameBuilder.Preamble} · marcador: {BfskFrameBuilder.StartMarker} · datos: {FdmFrameATextBox.Text}\n" +
                $"B · preámbulo: {BfskFrameBuilder.Preamble} · marcador: {BfskFrameBuilder.StartMarker} · datos: {FdmFrameBTextBox.Text}\n" +
                $"{frames.ChannelA.Length} símbolos por canal · duración conjunta: {frames.ChannelA.Length * FdmBitstreamGenerator.SymbolDurationSeconds:0.0} s";
        }
        catch (ArgumentException ex) { FdmFramesInfoText.Text = ex.Message; }
    }

    private async void PlayFdmFrames_Click(object sender, RoutedEventArgs e)
    {
        string a = FdmFrameATextBox.Text, b = FdmFrameBTextBox.Text;
        try { FdmBitstreamGenerator.ValidateSequences(a, b); }
        catch (ArgumentException ex) { FdmFramesStatusText.Text = ex.Message; return; }
        SetPlaybackControls(false);
        FdmFramesStatusText.Text = "Reproduciendo ambas tramas...";
        CancellationToken token = windowLifetime.Token;
        try
        {
            float[] samples = await Task.Run(() => fdmBitstreamGenerator.GenerateFrames(a, b), token);
            await playback.PlayAsync(samples, FdmBitstreamGenerator.SampleRate, token);
            FdmFramesStatusText.Text = "Reproducción de tramas FDM finalizada.";
        }
        catch (OperationCanceledException) when (closing) { }
        catch (Exception ex) { FdmFramesStatusText.Text = ex.Message; ShowPlaybackError(ex); }
        finally { SetPlaybackControls(true); }
    }

    private void BitSequenceTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (SequenceInfoText is null)
            return;
        string bits = BitSequenceTextBox.Text;
        double duration = bits.Length * BfskToneGenerator.DefaultBitDurationSeconds;
        SequenceInfoText.Text = bits.Length <= BfskToneGenerator.MaximumSequenceLength
            ? $"{bits.Length} bits · duración estimada: {duration:0.0} s"
            : $"{bits.Length} bits · máximo permitido: {BfskToneGenerator.MaximumSequenceLength}";
        UpdateFramePreview();
    }

    private void UpdateFramePreview()
    {
        if (FrameInfoText is null) return; // Puede dispararse TextChanged durante InitializeComponent.
        string patterns = $"Preámbulo (16): {BfskFrameBuilder.Preamble}\nMarcador provisional (8): {BfskFrameBuilder.StartMarker}";
        try
        {
            string data = BitSequenceTextBox.Text;
            string frame = frameBuilder.Build(data);
            FrameInfoText.Text = $"{patterns}\nDatos ({data.Length}): {data}\nTotal: {frame.Length} bits · duración estimada: {frame.Length * BfskToneGenerator.DefaultBitDurationSeconds:0.0} s";
        }
        catch (ArgumentException ex)
        {
            FrameInfoText.Text = $"{patterns}\n{ex.Message}";
        }
    }

    private void SetPlaybackControls(bool enabled)
    {
        Bit0Button.IsEnabled = enabled;
        Bit1Button.IsEnabled = enabled;
        BitSequenceTextBox.IsEnabled = enabled;
        PlaySequenceButton.IsEnabled = enabled;
        PlayFrameButton.IsEnabled = enabled;
        FdmBitAComboBox.IsEnabled = enabled;
        FdmBitBComboBox.IsEnabled = enabled;
        PlayFdmButton.IsEnabled = enabled;
        FdmSequenceATextBox.IsEnabled = enabled;
        FdmSequenceBTextBox.IsEnabled = enabled;
        PlayFdmSequencesButton.IsEnabled = enabled;
        FdmFrameATextBox.IsEnabled = enabled;
        FdmFrameBTextBox.IsEnabled = enabled;
        PlayFdmFramesButton.IsEnabled = enabled;
    }

    private void ShowPlaybackError(Exception exception)
    {
        StatusText.Text = "No se pudo reproducir el audio.";
        if (!windowLifetime.IsCancellationRequested)
            MessageBox.Show(this, $"No se pudo inicializar o reproducir el audio.\n{exception.Message}",
                "AcousticLink", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private async void StartCapture_Click(object sender, RoutedEventArgs e)
    {
        StartCaptureButton.IsEnabled = false;
        captureBusy = true;
        SaveCaptureButton.IsEnabled = false;
        CaptureStatusText.Text = "Inicializando micrófono...";
        try
        {
            int seconds = int.Parse((string)((ComboBoxItem)CaptureDurationComboBox.SelectedItem).Tag);
            await capture.StartAsync(seconds);
        }
        catch (Exception ex) { CaptureStatusText.Text = $"No se pudo iniciar el micrófono: {ex.Message}"; }
        captureBusy = false;
        RefreshCapture();
    }

    private async void StopCapture_Click(object sender, RoutedEventArgs e)
    {
        StopCaptureButton.IsEnabled = false;
        captureBusy = true;
        await capture.StopAsync();
        captureBusy = false;
        RefreshCapture();
    }

    private void RefreshCapture()
    {
        if (closing) return;
        StartCaptureButton.IsEnabled = !capture.IsRecording && !captureBusy;
        StopCaptureButton.IsEnabled = capture.IsRecording && !captureBusy;
        CaptureDurationComboBox.IsEnabled = !capture.IsRecording && !captureBusy;
        var recording = capture.Recording;
        var metrics = recording?.GetStatus() ?? (Count: 0, Peak: 0f);
        SaveCaptureButton.IsEnabled = !capture.IsRecording && !captureBusy && metrics.Count > 0;
        AnalyzeToneButton.IsEnabled = !capture.IsRecording && !captureBusy && !analyzing && metrics.Count > 0;
        DecodeSequenceButton.IsEnabled = !capture.IsRecording && !captureBusy && !analyzing && metrics.Count > 0;
        SynchronizeButton.IsEnabled = !capture.IsRecording && !captureBusy && !analyzing && metrics.Count > 0;
        AnalyzeFdmButton.IsEnabled = !capture.IsRecording && !captureBusy && !analyzing && metrics.Count > 0;
        DecodeFdmButton.IsEnabled = !capture.IsRecording && !captureBusy && !analyzing && metrics.Count > 0;
        SynchronizeFdmButton.IsEnabled = !capture.IsRecording && !captureBusy && !analyzing && metrics.Count > 0;
        CaptureFormatText.Text = capture.FormatDescription;
        if (recording is not null)
        {
            var details = recording.GetMetrics();
            CaptureMetricsText.Text = $"Duración: {details.DurationSeconds:0.00} / {recording.MaximumDurationSeconds} s · {details.Count} muestras · RMS: {details.Rms:0.0000} · pico: {details.Peak:0.0000}";
        }
        if (capture.Error is not null) CaptureStatusText.Text = $"Error del micrófono: {capture.Error}";
        else if (capture.IsRecording) CaptureStatusText.Text = "Grabando micrófono...";
        else if (recording is not null && wasCaptureRecording)
            CaptureStatusText.Text = recording.GetMetrics().StopReason switch
            {
                CaptureStopReason.Manual => "Grabación detenida manualmente.",
                CaptureStopReason.DurationLimit => $"Grabación finalizada por límite de {recording.MaximumDurationSeconds} segundos.",
                CaptureStopReason.ApplicationClosing => "Grabación cancelada por cierre.",
                CaptureStopReason.Error => "Grabación finalizada con error.",
                _ => "Micrófono detenido por el dispositivo."
            };
        wasCaptureRecording = capture.IsRecording;
    }

    private async void SaveCapture_Click(object sender, RoutedEventArgs e)
    {
        var recording = capture.Recording;
        if (recording is null) return;
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Audio WAV (*.wav)|*.wav", FileName = "captura.wav" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            await Task.Run(() => recording.SaveWav(dialog.FileName));
            CaptureStatusText.Text = $"WAV guardado: {dialog.FileName}";
        }
        catch (Exception ex) { CaptureStatusText.Text = $"No se pudo guardar WAV: {ex.Message}"; }
    }

    private async void AnalyzeTone_Click(object sender, RoutedEventArgs e)
    {
        var recording = capture.Recording;
        if (recording is null || capture.IsRecording || captureBusy || analyzing) return;
        if (!double.TryParse(AnalysisStartTextBox.Text, out double startSeconds) ||
            !double.IsFinite(startSeconds) || startSeconds < 0)
        {
            AnalysisResultText.Text = "Ingresá un instante inicial válido, mayor o igual a cero.";
            return;
        }
        double startSample = Math.Round(startSeconds * recording.SampleRate);
        int length = (int)Math.Round(0.10 * recording.SampleRate);
        int count = recording.GetStatus().Count;
        if (startSample > count || length > count - startSample)
        {
            AnalysisResultText.Text = "La ventana completa de 0,10 s debe estar dentro de la grabación.";
            return;
        }
        analyzing = true;
        AnalyzeToneButton.IsEnabled = false;
        AnalysisResultText.Text = "Analizando...";
        try
        {
            var result = await Task.Run(() =>
            {
                float[] samples = recording.GetSamples();
                return toneDetector.Analyze(samples.AsSpan((int)startSample, length), recording.SampleRate);
            });
            if (!closing)
                AnalysisResultText.Text = $"{recording.SampleRate} Hz · energía 1200 Hz: {result.Energy0:G6} · energía 1900 Hz: {result.Energy1:G6}\n" +
                    (result.Bit is int bit ? $"Bit detectado: {bit}." : $"No concluyente: {result.Reason}");
        }
        catch (Exception ex)
        {
            if (!closing) AnalysisResultText.Text = $"No se pudo analizar: {ex.Message}";
        }
        finally
        {
            analyzing = false;
            RefreshCapture();
        }
    }

    private async void DecodeSequence_Click(object sender, RoutedEventArgs e)
    {
        var recording = capture.Recording;
        if (recording is null || capture.IsRecording || captureBusy || analyzing) return;
        if (!double.TryParse(SequenceStartTextBox.Text, out double start) || !double.IsFinite(start) || start < 0 ||
            !int.TryParse(SequenceBitCountTextBox.Text, out int count) || count < 1 || count > BfskSequenceDecoder.MaximumBits)
        {
            DecodedSequenceText.Text = "Ingresá un inicio finito no negativo y una cantidad de 1 a 64 bits.";
            SequenceDiagnosticsText.Clear();
            return;
        }
        analyzing = true;
        RefreshCapture();
        DecodedSequenceText.Text = "Analizando secuencia...";
        SequenceDiagnosticsText.Clear();
        try
        {
            var result = await Task.Run(() => new BfskSequenceDecoder(toneDetector)
                .Decode(recording.GetSamples(), recording.SampleRate, start, count));
            if (closing) return;
            DecodedSequenceText.Text = $"{result.Bits}\nConcluyentes: {result.ConclusiveCount} · no concluyentes: {result.InconclusiveCount}\n" +
                (result.IsComplete ? "Todos los símbolos fueron concluyentes (sin verificación de integridad)." : "La secuencia no se recuperó completamente.");
            SequenceDiagnosticsText.Text = string.Join(Environment.NewLine, result.Symbols.Select(s =>
                $"Bit {s.Index + 1}: {s.Detection.Bit?.ToString() ?? "?"} · muestras [{s.StartSample}, {s.EndSample}) · " +
                $"análisis [{s.AnalysisStartSample}, {s.AnalysisStartSample + s.AnalysisSampleCount}) · " +
                $"E0={s.Detection.Energy0:G5}, E1={s.Detection.Energy1:G5} · {s.Detection.Reason}"));
        }
        catch (Exception ex)
        {
            if (!closing) DecodedSequenceText.Text = $"No se pudo decodificar: {ex.Message}";
        }
        finally
        {
            analyzing = false;
            RefreshCapture();
        }
    }

    private async void Synchronize_Click(object sender, RoutedEventArgs e)
    {
        var recording = capture.Recording;
        if (recording is null || capture.IsRecording || captureBusy || analyzing) return;
        if (!int.TryParse(SyncBitCountTextBox.Text, out int count) || count < 1 || count > BfskFrameBuilder.MaximumDataBits)
        {
            SynchronizationText.Text = "Ingresá entre 1 y 16 bits de datos.";
            return;
        }
        analyzing = true;
        RefreshCapture();
        SynchronizationText.Text = "Buscando trama...";
        CancellationToken token = windowLifetime.Token;
        try
        {
            var result = await Task.Run(() => new BfskFrameSynchronizer(toneDetector)
                .Find(recording.GetSamples(), recording.SampleRate, count, token), token);
            if (closing) return;
            SynchronizationText.Text = result.Found
                ? $"Inicio estimado: {result.StartSeconds:0.000} s · datos desde: {result.DataStartSeconds:0.000} s\n" +
                  $"Preámbulo: {result.Preamble}\nMarcador: {result.Marker}\nDatos: {result.Data!.Bits}\n{result.Reason}"
                : $"No se encontró una trama válida: {result.Reason}";
        }
        catch (OperationCanceledException) when (closing) { }
        catch (Exception ex)
        {
            if (!closing) SynchronizationText.Text = $"No se pudo sincronizar: {ex.Message}";
        }
        finally { analyzing = false; RefreshCapture(); }
    }

    private async void AnalyzeFdm_Click(object sender, RoutedEventArgs e)
    {
        var recording = capture.Recording;
        if (recording is null || capture.IsRecording || captureBusy || analyzing) return;
        if (!double.TryParse(FdmAnalysisStartTextBox.Text, out double start) || !double.IsFinite(start) || start < 0)
        {
            FdmDetectionText.Text = "Ingresá un instante inicial finito no negativo.";
            return;
        }
        double startSample = Math.Round(start * recording.SampleRate);
        int length = (int)Math.Round(0.10 * recording.SampleRate);
        if (startSample > recording.GetStatus().Count - length)
        {
            FdmDetectionText.Text = "La ventana completa debe estar dentro de la grabación.";
            return;
        }
        analyzing = true;
        RefreshCapture();
        FdmDetectionText.Text = "Analizando FDM...";
        try
        {
            var result = await Task.Run(() => new FdmToneDetector().Analyze(
                recording.GetSamples().AsSpan((int)startSample, length), recording.SampleRate));
            if (closing) return;
            static string Describe(BfskDetectionResult channel) => channel.Bit is int bit
                ? $"bit {bit}" : $"no concluyente: {channel.Reason}";
            FdmDetectionText.Text = $"{recording.SampleRate} Hz · ventana de 0,10 s\n" +
                $"Canal A: {Describe(result.ChannelA)}\nE1200={result.ChannelA.Energy0:G6} · E1900={result.ChannelA.Energy1:G6}\n" +
                $"Canal B: {Describe(result.ChannelB)}\nE3000={result.ChannelB.Energy0:G6} · E3700={result.ChannelB.Energy1:G6}";
        }
        catch (Exception ex) { if (!closing) FdmDetectionText.Text = $"No se pudo analizar FDM: {ex.Message}"; }
        finally { analyzing = false; RefreshCapture(); }
    }

    private async void DecodeFdm_Click(object sender, RoutedEventArgs e)
    {
        var recording = capture.Recording;
        if (recording is null || capture.IsRecording || captureBusy || analyzing) return;
        FdmDiagnosticsText.Clear();
        if (!double.TryParse(FdmDecodeStartTextBox.Text, out double start) || !double.IsFinite(start) || start < 0 ||
            !int.TryParse(FdmDecodeCountTextBox.Text, out int count) || count < 1 || count > FdmSequenceDecoder.MaximumSymbols)
        {
            FdmDecodedText.Text = "Ingresá un inicio finito no negativo y entre 1 y 16 símbolos.";
            return;
        }
        analyzing = true;
        RefreshCapture();
        FdmDecodedText.Text = "Decodificando FDM...";
        try
        {
            bool frames = FdmDecodeFramesCheckBox.IsChecked == true;
            var result = await Task.Run(() =>
            {
                var decoder = new FdmSequenceDecoder();
                float[] samples = recording.GetSamples();
                return frames ? decoder.DecodeFrameSymbols(samples, recording.SampleRate, start, count)
                    : decoder.Decode(samples, recording.SampleRate, start, count);
            });
            if (closing) return;
            static string Describe(string name, BfskSequenceResult channel) =>
                $"Canal {name}: {channel.Bits}\nConcluyentes: {channel.ConclusiveCount} · no concluyentes: {channel.InconclusiveCount} · " +
                (channel.IsComplete ? "recuperación completa (sin verificación de integridad)." : "recuperación parcial.");
            FdmDecodedText.Text = Describe("A", result.ChannelA) + "\n" + Describe("B", result.ChannelB);
            FdmDiagnosticsText.Text = string.Join(Environment.NewLine, result.ChannelA.Symbols.Select((a, i) =>
            {
                var b = result.ChannelB.Symbols[i];
                return $"Símbolo {i + 1} · muestras [{a.StartSample}, {a.EndSample}) · análisis [{a.AnalysisStartSample}, {a.AnalysisStartSample + a.AnalysisSampleCount})\n" +
                    $"A={a.Detection.Bit?.ToString() ?? "?"}, E1200={a.Detection.Energy0:G5}, E1900={a.Detection.Energy1:G5}: {a.Detection.Reason}\n" +
                    $"B={b.Detection.Bit?.ToString() ?? "?"}, E3000={b.Detection.Energy0:G5}, E3700={b.Detection.Energy1:G5}: {b.Detection.Reason}";
            }));
        }
        catch (Exception ex) { if (!closing) FdmDecodedText.Text = $"No se pudo decodificar FDM: {ex.Message}"; }
        finally { analyzing = false; RefreshCapture(); }
    }

    private void CancelFdmSync_Click(object sender, RoutedEventArgs e) => fdmSyncCancellation?.Cancel();

    private async void SynchronizeFdm_Click(object sender, RoutedEventArgs e)
    {
        var recording = capture.Recording;
        if (recording is null || capture.IsRecording || captureBusy || analyzing) return;
        if (!int.TryParse(FdmSyncCountTextBox.Text, out int count) || count < 1 || count > BfskFrameBuilder.MaximumDataBits)
        {
            FdmSyncResultText.Text = "Ingresá entre 1 y 16 bits de datos por canal.";
            return;
        }
        analyzing = true;
        RefreshCapture();
        FdmSyncResultText.Text = "Buscando cabeceras FDM...";
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(windowLifetime.Token);
        fdmSyncCancellation = cancellation;
        CancelFdmSyncButton.IsEnabled = true;
        try
        {
            var result = await Task.Run(() => new FdmFrameSynchronizer()
                .Find(recording.GetSamples(), recording.SampleRate, count, cancellation.Token), cancellation.Token);
            if (closing) return;
            string Describe(string name, FdmSynchronizedChannel? channel)
            {
                if (channel is null) return $"Canal {name}: sin cabecera localizada.";
                return $"Canal {name} · cabecera {(channel.HeaderValid ? "válida" : "NO válida")}\n" +
                    $"Preámbulo: {channel.Header.Bits[..BfskFrameBuilder.Preamble.Length]} · marcador: {channel.Header.Bits[BfskFrameBuilder.Preamble.Length..]}\n" +
                    $"Cabecera: {channel.Header.ConclusiveCount} concluyentes, {channel.Header.InconclusiveCount} no concluyentes\n" +
                    $"Datos: {channel.Data.Bits} · {channel.Data.ConclusiveCount} concluyentes, {channel.Data.InconclusiveCount} no concluyentes";
            }
            string state = result.State switch { FdmSynchronizationState.Complete => "Completa", FdmSynchronizationState.Partial => "Parcial", _ => "Fallida" };
            FdmSyncResultText.Text = $"Sincronización: {state}\n{result.Reason}\n" +
                (result.StartSeconds.HasValue ? $"Inicio estimado: {result.StartSeconds:0.000} s · datos: {result.DataStartSeconds:0.000} s\n" : "") +
                Describe("A", result.ChannelA) + "\n" + Describe("B", result.ChannelB);
        }
        catch (OperationCanceledException) { if (!closing) FdmSyncResultText.Text = "Búsqueda FDM cancelada."; }
        catch (Exception ex) { if (!closing) FdmSyncResultText.Text = $"No se pudo sincronizar FDM: {ex.Message}"; }
        finally
        {
            fdmSyncCancellation = null;
            CancelFdmSyncButton.IsEnabled = false;
            analyzing = false;
            RefreshCapture();
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        if (closeReady) return;
        if (e.Cancel && !closing) return;
        e.Cancel = true;
        if (closing) return;
        closing = true;
        IsEnabled = false;
        captureTimer.Stop();

        // Salir primero del cierre actual de WPF, incluso si la limpieza termina sincrónicamente.
        Dispatcher.BeginInvoke(new Action(() => _ = CompleteCloseAsync()));
    }

    private async Task CompleteCloseAsync()
    {
        try
        {
            try
            {
                windowLifetime.Cancel();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError($"Error al cancelar el audio durante el cierre: {ex}");
            }

            // DisposeAsync espera la sesión y la liberación del recorder de NAudio.
            await capture.DisposeAsync();
            if (capture.Error is not null)
                System.Diagnostics.Trace.TraceError($"Error de captura durante el cierre: {capture.Error}");
        }
        catch (Exception ex)
        {
            // La limpieza no debe dejar una excepción sin observar ni impedir el cierre final.
            System.Diagnostics.Trace.TraceError($"Error al liberar el micrófono durante el cierre: {ex}");
        }
        finally
        {
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                closeReady = true;
                Close();
            }));
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        windowLifetime.Dispose();
        base.OnClosed(e);
    }
}
