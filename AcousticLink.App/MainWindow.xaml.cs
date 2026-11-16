using System.Windows;
using System.Windows.Controls;
using AcousticLink.App.Audio;
using AcousticLink.App.Signals;
using AcousticLink.App.Protocol;
using AcousticLink.App.Experiments;
using AcousticLink.App.Visualization;

namespace AcousticLink.App;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly BfskToneGenerator toneGenerator = new();
    private readonly FdmToneMixer fdmMixer = new();
    private readonly FdmBitstreamGenerator fdmBitstreamGenerator = new();
    private readonly BfskFrameBuilder frameBuilder = new();
    private readonly AudioPlaybackService playback = new();
    private readonly CancellationTokenSource windowLifetime = new();
    private readonly AudioCaptureService capture = new();
    private readonly AudioRecordingSelection audioSelection = new();
    private readonly System.Windows.Threading.DispatcherTimer captureTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private bool closing;
    private bool closeReady;
    private bool captureBusy;
    private bool wasCaptureRecording;
    private bool analyzing;
    private bool playing;
    private bool fdmTextsValid;
    private CapturedAudio? waveformAudio;
    private CapturedAudio? liveWaveformAudio;
    private IncrementalWaveform? liveWaveform;
    private readonly float[] liveWaveformBuffer = new float[8192];
    private CancellationTokenSource? fdmSyncCancellation;
    private CancellationTokenSource? textReceiveCancellation;
    private CancellationTokenSource? experimentCancellation;
    private FdmExperimentResult? lastExperiment;
    private readonly BfskToneDetector toneDetector = new();

    public MainWindow()
    {
        InitializeComponent();
        InitializeWorkspace();
        InitializeSignalMonitor();
        InitializeContinuousListening();
        InitializeComparison();
        ReceptionAView.TextRequested += () => { ReceiverTools.SelectedIndex = 1; ReceivedFrameExplorer.OpenText(0); };
        ReceptionBView.TextRequested += () => { ReceiverTools.SelectedIndex = 1; ReceivedFrameExplorer.OpenText(1); };
        FdmProfileComboBox.ItemsSource = FdmAmplitudeProfiles.All;
        FdmProfileComboBox.SelectedIndex = 0;
        ExperimentProfileComboBox.Items.Add("No informado / desconocido");
        foreach (var profile in FdmAmplitudeProfiles.All) ExperimentProfileComboBox.Items.Add(profile);
        ExperimentProfileComboBox.SelectedIndex = 0;
        UpdateFramePreview();
        UpdateFdmSequencesInfo();
        UpdateFdmFramesInfo();
        UpdateFdmTextInfo();
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
        var profile = SelectedFdmProfile;
        SetPlaybackControls(false);
        FdmStatusText.Text = $"Reproduciendo FDM A={(bitA ? 1 : 0)}, B={(bitB ? 1 : 0)}...";
        try
        {
            float[] samples = fdmMixer.Generate(bitA, bitB, profile: profile);
            RememberDetailedSignal(samples, FdmToneMixer.SampleRate, $"Tonos FDM A={(bitA ? 1 : 0)}, B={(bitB ? 1 : 0)} · {FdmAmplitudeProfiles.Get(profile).Label}", bitA ? "1" : "0", bitB ? "1" : "0", profile, 1);
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
        var profile = SelectedFdmProfile;
        try { FdmBitstreamGenerator.ValidateSequences(a, b); }
        catch (ArgumentException ex) { FdmSequencesStatusText.Text = ex.Message; return; }
        SetPlaybackControls(false);
        FdmSequencesStatusText.Text = "Reproduciendo secuencias FDM...";
        CancellationToken token = windowLifetime.Token;
        try
        {
            float[] samples = await Task.Run(() => fdmBitstreamGenerator.Generate(a, b, profile), token);
            RememberDetailedSignal(samples, FdmBitstreamGenerator.SampleRate, $"Secuencias FDM · {FdmAmplitudeProfiles.Get(profile).Label}", a, b, profile);
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
        var profile = SelectedFdmProfile;
        try { FdmBitstreamGenerator.ValidateSequences(a, b); }
        catch (ArgumentException ex) { FdmFramesStatusText.Text = ex.Message; return; }
        SetPlaybackControls(false);
        FdmFramesStatusText.Text = "Reproduciendo ambas tramas...";
        CancellationToken token = windowLifetime.Token;
        try
        {
            float[] samples = await Task.Run(() => fdmBitstreamGenerator.GenerateFrames(a, b, profile), token);
            RememberDetailedSignal(samples, FdmBitstreamGenerator.SampleRate, $"Tramas binarias FDM · {FdmAmplitudeProfiles.Get(profile).Label}", FdmBitstreamGenerator.BuildFrames(a, b).ChannelA, FdmBitstreamGenerator.BuildFrames(a, b).ChannelB, profile);
            await playback.PlayAsync(samples, FdmBitstreamGenerator.SampleRate, token);
            FdmFramesStatusText.Text = "Reproducción de tramas FDM finalizada.";
        }
        catch (OperationCanceledException) when (closing) { }
        catch (Exception ex) { FdmFramesStatusText.Text = ex.Message; ShowPlaybackError(ex); }
        finally { SetPlaybackControls(true); }
    }

    private void FdmText_TextChanged(object sender, TextChangedEventArgs e)
    { InvalidateWalkthrough("Cambió el mensaje del emisor. Prepará un recorrido nuevo."); UpdateFdmTextInfo(); MarkGeneratedSignalOutdated(); }
    private void HammingTransmit_Changed(object sender, RoutedEventArgs e)
    { InvalidateWalkthrough("Cambió Hamming del emisor. Prepará un recorrido nuevo."); UpdateFdmTextInfo(); MarkGeneratedSignalOutdated(); }

    private void MarkGeneratedSignalOutdated()
    {
        if (FdmTextStatusText is null || playing) return;
        if (!fdmTextsValid) FdmTextStatusText.Text = FdmTextInfoText.Text;
        else if (generatedSignal is not null)
            FdmTextStatusText.Text = "El último buffer digital corresponde a una preparación anterior. Prepará otra vista o transmití el mensaje actual.";
    }

    private void UpdateFdmTextInfo()
    {
        if (FdmTextInfoText is null) return;
        if (IsBfskTransmit) { UpdateBfskTextInfo(); return; }
        bool validationStatus = !playing && FdmTextStatusText.Text.Length > 0 && FdmTextStatusText.Text == FdmTextInfoText.Text;
        var a = FdmTextInputValidation.Check(FdmTextATextBox.Text, "A");
        var b = FdmTextInputValidation.Check(FdmTextBTextBox.Text, "B");
        fdmTextsValid = a.Error is null && b.Error is null;
        TransmitFdmTextButton.IsEnabled = fdmTextsValid && !playing;
        void ShowInput(System.Windows.Controls.TextBlock label, FdmTextInputCheck input)
        {
            label.Text = input.Error ?? $"{input.ByteCount} / {FdmTextFrameBuilder.MaximumUtf8Bytes} bytes UTF-8";
            label.Foreground = (System.Windows.Media.Brush)FindResource(input.Error is null ? "MutedBrush" : "WarningBrush");
        }
        ShowInput(TransmitABytesText, a); ShowInput(TransmitBBytesText, b);
        if (!fdmTextsValid)
        {
            FdmTextInfoText.Text = string.Join("\n", new[] { a.Error, b.Error }.Where(error => error is not null));
            if (validationStatus) FdmTextStatusText.Text = FdmTextInfoText.Text;
            TransmitDurationText.Text = "Duración no disponible: corregí los canales indicados.";
            return;
        }
        try
        {
            var frames = new FdmTextFrameBuilder().Build(FdmTextATextBox.Text, FdmTextBTextBox.Text, UseHammingTransmitCheckBox.IsChecked == true);
            TransmitABytesText.Text = $"{frames.ChannelA.Utf8ByteCount} / 8 bytes UTF-8";
            TransmitBBytesText.Text = $"{frames.ChannelB.Utf8ByteCount} / 8 bytes UTF-8";
            TransmitDurationText.Text = $"{frames.TotalSymbols * FdmBitstreamGenerator.SymbolDurationSeconds:0.0} s de transmisión";
            static string Describe(string name, FdmTextChannelFrame channel) =>
                $"{name}: {channel.Utf8ByteCount} bytes UTF-8 · bloque original: {channel.OriginalBlockBitCount} bits · " +
                (channel.UsesHamming ? $"bloque Hamming: {channel.EncodedBlockBitCount} bits · " : "sin Hamming · ") +
                $"cabecera: {channel.HeaderBitCount} bits · trama real: {channel.RealBitCount} bits · relleno: {channel.PaddingBitCount} bits\n";
            FdmTextInfoText.Text = Describe("A", frames.ChannelA) + Describe("B", frames.ChannelB) +
                $"Total: {frames.TotalSymbols} símbolos · duración conjunta: {frames.TotalSymbols * FdmBitstreamGenerator.SymbolDurationSeconds:0.0} s";
            if (validationStatus) FdmTextStatusText.Text = "";
        }
        catch (ArgumentException)
        {
            fdmTextsValid = false; TransmitFdmTextButton.IsEnabled = false;
            FdmTextInfoText.Text = "No se pudo preparar la trama FDM. Revisá los textos de ambos canales.";
            TransmitDurationText.Text = "Duración no disponible: no se pudo preparar una trama válida.";
        }
    }

    private async void TransmitFdmText_Click(object sender, RoutedEventArgs e)
    {
        if (IsBfskTransmit) { await TransmitBfskTextAsync(); return; }
        string a = FdmTextATextBox.Text, b = FdmTextBTextBox.Text;
        bool useHamming = UseHammingTransmitCheckBox.IsChecked == true;
        var profile = SelectedFdmProfile;
        UpdateFdmTextInfo();
        if (!fdmTextsValid) { FdmTextStatusText.Text = FdmTextInfoText.Text; return; }
        try { _ = new FdmTextFrameBuilder().Build(a, b, useHamming); }
        catch (ArgumentException) { FdmTextStatusText.Text = "No se pudo preparar la trama FDM. Revisá los textos de ambos canales."; return; }
        SetPlaybackControls(false);
        FdmTextStatusText.Text = useHamming ? "Transmitiendo mensajes con Hamming (7,4)..." : "Transmitiendo mensajes sin Hamming...";
        CancellationToken token = windowLifetime.Token;
        try
        {
            float[] samples = await Task.Run(() => fdmBitstreamGenerator.GenerateTextFrames(a, b, useHamming, profile), token);
            var monitorFrames = new FdmTextFrameBuilder().Build(a, b, useHamming);
            RememberDetailedSignal(samples, FdmBitstreamGenerator.SampleRate, $"Mensajes FDM · {FdmAmplitudeProfiles.Get(profile).Label} · Hamming {(useHamming ? "activado" : "desactivado")}", monitorFrames.ChannelA.TransmittedBits, monitorFrames.ChannelB.TransmittedBits, profile, frames: monitorFrames);
            await playback.PlayAsync(samples, FdmBitstreamGenerator.SampleRate, token);
            if (generatedSignal is not null && generatedSignal.Samples.Equals(new ReadOnlyMemory<float>(samples))) { generatedSignal = generatedSignal with { Description = generatedSignal.Description + " · transmisión reproducida (recepción no verificada)" }; if (generatedMap is not null) generatedMap = generatedMap.WithSignal(generatedSignal); }
            FdmTextStatusText.Text = "Reproducción de mensajes finalizada (recepción aún no verificada).";
        }
        catch (OperationCanceledException) when (closing) { }
        catch (Exception ex) { FdmTextStatusText.Text = ex.Message; ShowPlaybackError(ex); }
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

    private FdmAmplitudeProfile SelectedFdmProfile =>
        (FdmProfileComboBox.SelectedItem as FdmAmplitudeSetting)?.Profile ?? FdmAmplitudeProfile.Original;

    private void SetPlaybackControls(bool enabled)
    {
        playing = !enabled;
        FdmProfileComboBox.IsEnabled = enabled;
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
        FdmTextATextBox.IsEnabled = enabled;
        FdmTextBTextBox.IsEnabled = enabled;
        TransmitFdmTextButton.IsEnabled = enabled && fdmTextsValid;
        UseHammingTransmitCheckBox.IsEnabled = enabled;
        TransmitModeComboBox.IsEnabled = enabled;
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
        if (calibrationBusy || calibrationCapture.IsRecording) { CaptureStatusText.Text = "Detené la calibración antes de iniciar una captura manual."; return; }
        if (continuousListening is not null) { CaptureStatusText.Text = "Detené la escucha continua antes de iniciar una captura manual."; return; }
        StartCaptureButton.IsEnabled = false;
        captureBusy = true;
        SaveCaptureButton.IsEnabled = false;
        bool decodeDuringCapture = DecodeDuringCaptureCheckBox.IsChecked == true;
        DecodeDuringCaptureCheckBox.IsEnabled = UseHammingReceiveCheckBox.IsEnabled = RecoverReceiveErasuresCheckBox.IsEnabled = ReceiverModeComboBox.IsEnabled = false;
        CaptureStatusText.Text = "Inicializando micrófono...";
        try
        {
            int seconds = int.Parse((string)((ComboBoxItem)CaptureDurationComboBox.SelectedItem).Tag);
            await capture.StartAsync(seconds);
            if (closing) return; // No publicar una sesión nueva si el cierre comenzó durante la inicialización.
            if (capture.Recording is { } started)
            {
                audioSelection.SelectMicrophone(started, capture.FormatDescription);
                InvalidateAudioAnalysis();
                ExperimentProfileComboBox.SelectedIndex = 0;
                StartManualDiagnostics(started, decodeDuringCapture);
                if (decodeDuringCapture) StartLiveReception(started);
            }
        }
        catch (Exception ex) { CaptureStatusText.Text = $"No se pudo iniciar el micrófono: {ex.Message}"; }
        finally { captureBusy = false; if (!closing) RefreshCapture(); }
    }

    private async void StopCapture_Click(object sender, RoutedEventArgs e) => await StopCaptureSafelyAsync();

    private async Task StopCaptureSafelyAsync()
    {
        StopCaptureButton.IsEnabled = false;
        captureBusy = true;
        try { await capture.StopAsync(); }
        catch (OperationCanceledException) { if (!closing) CaptureStatusText.Text = "Parada cancelada."; }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError($"Error al detener captura: {ex}");
            if (!closing) CaptureStatusText.Text = $"No se pudo detener el micrófono: {ex.Message}";
        }
        finally { captureBusy = false; if (!closing) RefreshCapture(); }
    }

    // Solo al reemplazar el audio seleccionado, no al repetir un análisis ni cambiar sus opciones.
    private void InvalidateAudioAnalysis()
    {
        InvalidateWalkthrough("La fuente o modalidad cambió. Prepará un recorrido nuevo.");
        continuousSegmentRetained = false;
        continuousHistorySelected = false;
        ContinuousHistoryComboBox.SelectedIndex = -1;
        StopLiveReception();
        manualDiagnostics = null; manualDiagnosticsAudio = null;
        ShowReceiverDiagnostics(null);
        presentedReception = null; presentedReceptionSource = null; presentedReceptionSamples = 0;
        LiveReceptionInspector.Clear(); ReceiverSignalMonitor.ClearReceptionActivity();
        InvalidateObservedMonitor();
        const string pending = "Audio seleccionado cambiado. Pendiente de analizar esta grabación.";
        AnalysisResultText.Text = pending;
        DecodedSequenceText.Text = pending;
        SequenceDiagnosticsText.Clear();
        SynchronizationText.Text = pending;
        FdmDetectionText.Text = pending;
        FdmDecodedText.Text = pending;
        FdmDiagnosticsText.Clear();
        FdmSyncResultText.Text = pending;
        ReceivedFdmText.Text = pending;
        ReceptionAView.Clear(); ReceptionBView.Clear();
        ReceivedFrameExplorer.Clear();
        MetricsAView.Clear(); MetricsBView.Clear();
        AnalysisSummaryHeadline.Text = "Sin mediciones. Seleccioná audio en Receptor y medí la grabación.";
        HammingWordTraceView.Clear();
        TechnicalDiagnosticView.Clear();
        AnalysisPipelineText.Text = "Señal → bits → Hamming → CRC y mensaje: No disponible";
        waveformAudio = null;
        liveWaveformAudio = null;
        liveWaveform = null;
        ReceivedWaveform.SetData([], 0);
        UpdateWaveformScale();
        WaveformInfoText.Text = "Sin señal finalizada disponible";
        ReceivedChannelAText.Text = "Sin analizar";
        ReceivedChannelBText.Text = "Sin analizar";
        ReceivedChannelAText.Foreground = System.Windows.Media.Brushes.LightGray;
        ReceivedChannelBText.Foreground = System.Windows.Media.Brushes.LightGray;
        ExperimentChannelAText.Text = "Sin analizar";
        ExperimentChannelBText.Text = "Sin analizar";
        lastExperiment = null;
        ExperimentSummaryText.Text = "Audio seleccionado cambiado. Realizá una nueva medición antes de exportar CSV.";
        ExperimentDetailsText.Clear();
        ExportExperimentButton.IsEnabled = false;
    }

    private void RefreshCapture()
    {
        if (closing) return;
        RefreshObservedMonitor();
        GeneralStateText.Text = captureBusy ? "Operando micrófono…" : capture.IsRecording ? "Capturando audio"
            : analyzing ? "Procesando audio…" : playing ? "Reproduciendo audio"
            : audioSelection.Current is null ? "Sin audio seleccionado" : "Audio seleccionado · sin operación activa";
        var selectedAudio = audioSelection.Current;
        WorkspaceAudioStatusText.Text = selectedAudio is null ? "Sin fuente de audio"
            : $"{System.IO.Path.GetFileName(selectedAudio.Source)} · {selectedAudio.Audio.SampleRate} Hz · {selectedAudio.Audio.GetMetrics().DurationSeconds:0.00} s";
        WorkspaceAudioStatusText.ToolTip = selectedAudio?.Source;
        var live = capture.IsRecording ? capture.Recording?.GetMetrics() : null;
        LiveCaptureText.Text = captureBusy ? "Iniciando o finalizando captura…" : live.HasValue
            ? $"Grabando · {live.Value.DurationSeconds:0.00} s de muestras · RMS {live.Value.Rms:G6} · pico {live.Value.Peak:G6}"
            : audioSelection.Current?.Audio.GetMetrics().StopReason is { } reason && reason != CaptureStopReason.None
                ? reason == CaptureStopReason.Imported ? "WAV seleccionado" : "Captura finalizada"
                : "Sin captura activa";
        CaptureRmsBar.Value = live.HasValue ? Math.Min(1, live.Value.Rms) : 0;
        StartCaptureButton.IsEnabled = !capture.IsRecording && !captureBusy && !analyzing;
        LoadWavButton.IsEnabled = !capture.IsRecording && !captureBusy && !analyzing;
        StopCaptureButton.IsEnabled = capture.IsRecording && !captureBusy;
        CaptureDurationComboBox.IsEnabled = !capture.IsRecording && !captureBusy;
        var recording = audioSelection.Current?.Audio;
        QueueLiveReception();
        DecodeDuringCaptureCheckBox.IsEnabled = !capture.IsRecording && !captureBusy;
        ReceiverModeComboBox.IsEnabled = !capture.IsRecording && !captureBusy && !analyzing && continuousListening is null;
        CaptureProgressBar.Value = recording is null ? 0 : Math.Min(1, recording.GetMetrics().DurationSeconds / recording.MaximumDurationSeconds);
        if (capture.IsRecording && !captureBusy && ReferenceEquals(recording, capture.Recording) && recording is not null)
            UpdateLiveWaveform(recording);
        bool differentModes = UseHammingReceiveCheckBox.IsChecked != ExperimentHammingCheckBox.IsChecked ||
            (UseHammingReceiveCheckBox.IsChecked == true && ExperimentHammingCheckBox.IsChecked == true &&
             RecoverReceiveErasuresCheckBox.IsChecked != RecoverExperimentErasuresCheckBox.IsChecked);
        AnalysisConfigurationWarning.Text = differentModes
            ? "ADVERTENCIA: Receptor y mediciones tienen modalidades distintas. No se cambian automáticamente."
            : "Receptor y mediciones: modalidades coincidentes, opciones independientes.";
        if (!continuousSegmentRetained && !continuousHistorySelected && continuousListening is null && !capture.IsRecording && !captureBusy && recording is not null && !ReferenceEquals(waveformAudio, recording))
            _ = UpdateWaveformAsync(recording);
        var metrics = recording?.GetStatus() ?? (Count: 0, Peak: 0f);
        SaveCaptureButton.IsEnabled = !capture.IsRecording && !captureBusy && metrics.Count > 0;
        AnalyzeToneButton.IsEnabled = !capture.IsRecording && !captureBusy && !analyzing && metrics.Count > 0;
        DecodeSequenceButton.IsEnabled = !capture.IsRecording && !captureBusy && !analyzing && metrics.Count > 0;
        SynchronizeButton.IsEnabled = !capture.IsRecording && !captureBusy && !analyzing && metrics.Count > 0;
        AnalyzeFdmButton.IsEnabled = !capture.IsRecording && !captureBusy && !analyzing && metrics.Count > 0;
        DecodeFdmButton.IsEnabled = !capture.IsRecording && !captureBusy && !analyzing && metrics.Count > 0;
        SynchronizeFdmButton.IsEnabled = !capture.IsRecording && !captureBusy && !analyzing && metrics.Count > 0;
        ReceiveFdmTextButton.IsEnabled = !capture.IsRecording && !captureBusy && !analyzing && metrics.Count > 0;
        UseHammingReceiveCheckBox.IsEnabled = !analyzing && !capture.IsRecording && !captureBusy;
        RecoverReceiveErasuresCheckBox.IsEnabled = !analyzing && !capture.IsRecording && !captureBusy && UseHammingReceiveCheckBox.IsChecked == true;
        AnalyzeExperimentButton.IsEnabled = !capture.IsRecording && !captureBusy && !analyzing && metrics.Count > 0;
        ExportExperimentButton.IsEnabled = lastExperiment is not null && !analyzing && !capture.IsRecording && !captureBusy;
        ExperimentExpectedATextBox.IsEnabled = !analyzing;
        ExperimentExpectedBTextBox.IsEnabled = !analyzing;
        ExperimentHammingCheckBox.IsEnabled = !analyzing;
        RecoverExperimentErasuresCheckBox.IsEnabled = !analyzing && ExperimentHammingCheckBox.IsChecked == true;
        ExperimentProfileComboBox.IsEnabled = !analyzing;
        AudioSourceText.Text = audioSelection.Current?.Source ?? "Sin audio seleccionado.";
        CaptureFormatText.Text = audioSelection.Current?.FormatDescription ?? "Sin formato seleccionado.";
        if (recording is not null)
        {
            var details = recording.GetMetrics();
            CaptureMetricsText.Text = $"Duración: {details.DurationSeconds:0.00} / {recording.MaximumDurationSeconds} s · {details.Count} muestras · RMS: {details.Rms:0.0000} · pico: {details.Peak:0.0000}";
        }
        if (capture.Error is not null && ReferenceEquals(recording, capture.Recording) && wasCaptureRecording)
            CaptureStatusText.Text = $"Error del micrófono: {capture.Error}";
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
        if (continuousListening is null) RefreshContinuousControls();
        if (continuousListening is null && !continuousHistorySelected && !continuousSegmentRetained) RefreshReceptionActivity();
        else if (continuousListening is not null) RefreshContinuousControls();
        RefreshManualDiagnostics();
        if (continuousHistorySelected)
        {
            DisableManualAudioTools();
            if (continuousListening is null)
            {
                WorkspaceAudioStatusText.Text = "Histórico de escucha continua · sin micrófono activo";
                LiveCaptureText.Text = "Inspección histórica · no captura nueva";
            }
        }
        RefreshContinuousRetainedContext();
    }

    private void WaveformScale_Changed(object sender, SelectionChangedEventArgs e) => UpdateWaveformScale();

    private void UpdateWaveformScale()
    {
        if (ReceivedWaveform is null || WaveformScaleText is null) return;
        ReceivedWaveform.UseAutoScale = WaveformScaleComboBox.SelectedIndex == 1;
        WaveformScaleText.Text = $"Pico absoluto: {ReceivedWaveform.PeakAbsolute:G6} · escala ±{ReceivedWaveform.VerticalLimit:G6}. " +
            "Solo visual: no modifica las muestras. En escala ±1 los valores superiores quedan fuera del área del gráfico.";
    }

    private void UpdateLiveWaveform(CapturedAudio recording)
    {
        try
        {
            if (!ReferenceEquals(liveWaveformAudio, recording))
            {
                liveWaveformAudio = recording;
                liveWaveform = new IncrementalWaveform(recording.SampleCapacity);
            }
            // Máximo acotado por tick; solo muestras nuevas, fuera del callback de NAudio.
            for (int i = 0; i < 8; i++)
            {
                int read = recording.CopySamples(liveWaveform!.SampleCount, liveWaveformBuffer);
                if (read == 0) break;
                liveWaveform.Append(liveWaveformBuffer.AsSpan(0, read));
            }
            ReceivedWaveform.SetData(liveWaveform!.Snapshot(), recording.MaximumDurationSeconds, liveWaveform.DisplayFraction);
            UpdateWaveformScale();
            WaveformInfoText.Text = $"Grabando · {recording.SampleRate} Hz · {liveWaveform.SampleCount / (double)recording.SampleRate:0.00} s dibujados / {recording.MaximumDurationSeconds} s · RMS y pico acumulados";
        }
        catch (Exception ex) { WaveformInfoText.Text = $"No se pudo representar la captura: {ex.Message}"; }
    }

    private async Task UpdateWaveformAsync(CapturedAudio recording)
    {
        waveformAudio = recording;
        WaveformInfoText.Text = "Preparando forma de onda real…";
        try
        {
            var reduced = await Task.Run(() => WaveformView.Reduce(recording.GetSamples()), windowLifetime.Token);
            if (closing || !ReferenceEquals(audioSelection.Current?.Audio, recording)) return;
            var metrics = recording.GetMetrics();
            ReceivedWaveform.SetData(reduced, metrics.DurationSeconds);
            UpdateWaveformScale();
            WaveformInfoText.Text = $"{recording.SampleRate} Hz · {metrics.DurationSeconds:0.000} s · {metrics.Count} muestras · amplitud sin normalizar";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!closing && ReferenceEquals(audioSelection.Current?.Audio, recording)) WaveformInfoText.Text = $"No se pudo representar la señal: {ex.Message}"; }
    }

    private async void LoadWav_Click(object sender, RoutedEventArgs e)
    {
        if (capture.IsRecording || captureBusy || analyzing) return;
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Grabaciones WAV (*.wav)|*.wav", CheckFileExists = true, Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        analyzing = true;
        RefreshCapture();
        CaptureStatusText.Text = "Importando WAV...";
        CancellationToken token = windowLifetime.Token;
        try
        {
            await Task.Run(() => audioSelection.ImportWav(dialog.FileName, token), token);
            if (!closing)
            {
                InvalidateAudioAnalysis();
                // Un nuevo archivo no hereda la declaración de perfil de la grabación anterior.
                ExperimentProfileComboBox.SelectedIndex = 0;
                CaptureStatusText.Text = "WAV importado. Elegí la herramienta de análisis y la modalidad Hamming correspondiente.";
            }
        }
        catch (OperationCanceledException) { if (!closing) CaptureStatusText.Text = "Importación cancelada; se conserva la selección anterior."; }
        catch (Exception ex) { if (!closing) CaptureStatusText.Text = $"No se pudo cargar el WAV; se conserva el audio anterior. {ex.Message}"; }
        finally { analyzing = false; RefreshCapture(); }
    }

    private async void SaveCapture_Click(object sender, RoutedEventArgs e)
    {
        var recording = audioSelection.Current?.Audio;
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
        var recording = audioSelection.Current?.Audio;
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
        var recording = audioSelection.Current?.Audio;
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
        var recording = audioSelection.Current?.Audio;
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
        var recording = audioSelection.Current?.Audio;
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
        var recording = audioSelection.Current?.Audio;
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
        var recording = audioSelection.Current?.Audio;
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

    private void CancelReceiveFdmText_Click(object sender, RoutedEventArgs e)
    {
        textReceiveCancellation?.Cancel();
        CancelLiveReception();
    }
    private void OpenAnalysis_Click(object sender, RoutedEventArgs e) => MainSectionsTabs.SelectedIndex = 3;

    private async void ReceiveFdmText_Click(object sender, RoutedEventArgs e)
    {
        if (IsBfskReceive) { await ReceiveBfskTextAsync(); return; }
        var recording = audioSelection.Current?.Audio;
        if (recording is null || capture.IsRecording || captureBusy || analyzing) return;
        StopLiveReception();
        var diagnostics = StartHistoricalDiagnostics(recording);
        analyzing = true;
        RefreshCapture();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(windowLifetime.Token);
        textReceiveCancellation = cancellation;
        CancelReceiveFdmTextButton.IsEnabled = true;
        ReceivedFdmText.Text = "Buscando cabeceras y recuperando mensajes...";
        ReceptionAView.Clear("Analizando…"); ReceptionBView.Clear("Analizando…");
        ReceivedFrameExplorer.Clear("Analizando…");
        ReceivedChannelAText.Text = "Sin resultado · análisis en curso";
        ReceivedChannelBText.Text = "Sin resultado · análisis en curso";
        ReceivedChannelAText.Foreground = System.Windows.Media.Brushes.LightGray;
        ReceivedChannelBText.Foreground = System.Windows.Media.Brushes.LightGray;
        bool useHamming = UseHammingReceiveCheckBox.IsChecked == true;
        bool recoverErasures = useHamming && RecoverReceiveErasuresCheckBox.IsChecked == true;
        try
        {
            var result = await Task.Run(() => new FdmTextReceiver().Receive(recording.GetSamples(), recording.SampleRate, cancellation.Token, useHamming, recoverErasures), cancellation.Token);
            if (closing) return;
            ShowFdmReception(result, recording);
            FinishHistoricalDiagnostics(diagnostics, recording, result);
        }
        catch (OperationCanceledException) { if (!closing) ReceivedFdmText.Text = "Recepción cancelada."; diagnostics.Stop("Análisis posterior cancelado."); ShowReceiverDiagnostics(diagnostics); }
        catch (Exception ex) { if (!closing) ReceivedFdmText.Text = $"No se pudo recibir: {ex.Message}"; diagnostics.Stop("Análisis posterior detenido por error: " + ex.Message); ShowReceiverDiagnostics(diagnostics); }
        finally
        {
            textReceiveCancellation = null;
            if (ReceivedChannelAText.Text == "Sin resultado · análisis en curso")
            {
                ReceivedChannelAText.Text = ReceivedChannelBText.Text = "Sin resultado nuevo. " + ReceivedFdmText.Text;
                ReceptionAView.Clear(ReceivedFdmText.Text); ReceptionBView.Clear(ReceivedFdmText.Text);
                ReceivedFrameExplorer.Clear(ReceivedFdmText.Text);
            }
            CancelReceiveFdmTextButton.IsEnabled = false;
            analyzing = false;
            RefreshCapture();
        }
    }

    private void ShowFdmReception(FdmTextReceiveResult result, CapturedAudio recording, double? through = null)
    {
        InvalidateWalkthrough("Hay un resultado nuevo. Elegí inspeccionar la recepción actual.");
        static string Describe(string name, FdmTextChannelResult channel)
        {
            string stats = "";
            if (channel.UsesHamming)
            {
                var h = channel.HammingDiagnostics;
                stats = h is null ? "\nHamming: sin palabras decodificadas." :
                    $"\nHamming ({(channel.HammingCoversCompleteBlock ? "bloque completo" : "longitud / fragmento")}): {h.WordCount} palabras · " +
                    $"síndrome cero: {h.ZeroSyndromeWordCount} · no nulo: {h.CorrectedWordCount} · inversiones: {h.AppliedBitFlipCount}\n" +
                    $"Borrados recuperados: {h.RecoveredErasureCount} · palabras recuperadas por borrados: {h.ErasureRecoveredWordCount} · irrecuperables: {h.UnrecoverableWordCount}\n" +
                    string.Join("; ", h.Words.Where(w => w.FlippedPosition.HasValue).Select(w => $"palabra {w.WordIndex + 1}, síndrome {w.Syndrome}, posición invertida {w.FlippedPosition}"));
                stats += $"\nRecuperación de borrados: {(channel.ErasureRecoveryEnabled ? "habilitada" : "deshabilitada")}. Los bits conocidos se suponen correctos; la integridad final depende del CRC.";
            }
            return $"Modo: {(channel.UsesHamming ? "con Hamming (7,4)" : "sin Hamming")}\n" +
            $"Canal {name} · cabecera: {(channel.HeaderValid ? "validada" : "no validada")} · bytes: {channel.ByteLength?.ToString() ?? "desconocidos"} · " +
            $"CRC: {(channel.CrcValid is true ? "válido" : channel.CrcValid is false ? "incorrecto" : "no comprobado")}\n" +
            (channel.State == FdmTextReceiveState.Valid ? $"Texto: {channel.Text}\n" : "") + channel.Reason + stats;
        }
        ReceivedFdmText.Text = $"{(IsBfskReceive ? "BFSK — un canal" : "FDM — dos canales")} · sincronización: {result.Synchronization.State} · inicio: {result.Synchronization.StartSeconds:0.000} s\n" +
            result.Synchronization.Reason + "\n" + Describe("A", result.ChannelA) + (IsBfskReceive ? "" : "\n" + Describe("B", result.ChannelB));
        void ShowChannel(TextBlock target, FdmTextChannelResult channel)
        {
            target.Text = $"Sincronización: {result.Synchronization.State} · cabecera {(channel.HeaderValid ? "válida" : "no validada")}\n" +
                $"CRC: {(channel.CrcValid is true ? "válido" : channel.CrcValid is false ? "incorrecto" : "no comprobado")} · bytes: {channel.ByteLength?.ToString() ?? "?"}\n" +
                (channel.State == FdmTextReceiveState.Valid ? $"Texto: {channel.Text}\n" : "") + channel.Reason;
            target.Foreground = channel.State == FdmTextReceiveState.Valid ? System.Windows.Media.Brushes.LightGreen
                : channel.CrcValid is false ? System.Windows.Media.Brushes.Salmon : System.Windows.Media.Brushes.Khaki;
        }
        ShowChannel(ReceivedChannelAText, result.ChannelA);
        ShowChannel(ReceivedChannelBText, result.ChannelB);
        ReceptionAView.Show(result.ChannelA, result.Synchronization, "A");
        ReceptionBView.Show(result.ChannelB, result.Synchronization, "B");
        ReceivedFrameExplorer.Show(result, through.HasValue);
        presentedReception = result; presentedReceptionSource = recording;
        presentedReceptionSamples = through.HasValue ? (int)Math.Round(through.Value * recording.SampleRate) : recording.GetStatus().Count;
        RefreshReceptionActivity();
        if (ReferenceEquals(ReceiverSignalMonitor.Data?.Signal.SourceId, recording) && ReceiverSignalMonitor.Data.Signal.Duration >= (through ?? 0)) ReceiverSignalMonitor.SetReception(result, through ?? recording.GetMetrics().DurationSeconds);
        if (ReferenceEquals(DashboardSignalMonitor.Data?.Signal.SourceId, recording) && DashboardSignalMonitor.Data.Signal.Duration >= (through ?? 0)) DashboardSignalMonitor.SetReception(result, through ?? recording.GetMetrics().DurationSeconds);
    }

    private void CancelExperiment_Click(object sender, RoutedEventArgs e) => experimentCancellation?.Cancel();

    private async void AnalyzeExperiment_Click(object sender, RoutedEventArgs e)
    {
        var selected = audioSelection.Current;
        if (selected is null || capture.IsRecording || captureBusy || analyzing) return;
        string a = ExperimentExpectedATextBox.Text, b = ExperimentExpectedBTextBox.Text;
        bool hamming = ExperimentHammingCheckBox.IsChecked == true;
        bool recoverErasures = hamming && RecoverExperimentErasuresCheckBox.IsChecked == true;
        FdmAmplitudeProfile? declaredProfile = (ExperimentProfileComboBox.SelectedItem as FdmAmplitudeSetting)?.Profile;
        lastExperiment = null;
        ExperimentDetailsText.Clear();
        ExperimentChannelAText.Text = "Sin resultado · medición en curso";
        MetricsAView.Clear("Midiendo…"); MetricsBView.Clear("Midiendo…");
        HammingWordTraceView.Clear("Midiendo…");
        TechnicalDiagnosticView.Clear("Midiendo…");
        AnalysisPipelineText.Text = "Análisis de la grabación en curso…";
        ExperimentChannelBText.Text = "Sin resultado · medición en curso";
        analyzing = true;
        RefreshCapture();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(windowLifetime.Token);
        experimentCancellation = cancellation;
        CancelExperimentButton.IsEnabled = true;
        ExperimentSummaryText.Text = "Midiendo contra las referencias indicadas...";
        AnalysisSummaryHeadline.Text = "Medición en curso · resultados aún no disponibles";
        try
        {
            var result = await Task.Run(() => new FdmExperimentAnalyzer().Analyze(selected.Audio.GetSamples(), selected.Audio.SampleRate,
                a, b, hamming, selected.Source, cancellation.Token, declaredProfile, recoverErasures), cancellation.Token);
            if (closing) return;
            lastExperiment = result;
            MetricsAView.Show(result.ChannelA, result.UsesHamming);
            MetricsBView.Show(result.ChannelB, result.UsesHamming);
            HammingWordTraceView.Show(result);
            TechnicalDiagnosticView.Show(result, selected.Audio.GetStatus().Count);
            string Detected(FdmChannelExperiment c) => c.Before is { } b ? $"{b.ComparedBits}/{b.ExpectedCount} bits concluyentes" : "No disponible";
            string Crc(FdmChannelExperiment c) => c.Reception.CrcValid is true ? "válido" : c.Reception.CrcValid is false ? "inválido" : "no comprobado";
            AnalysisPipelineText.Text = $"1. Audio {result.AudioDurationSeconds:0.00} s · {result.SampleRate} Hz → " +
                $"2. Bits A: {Detected(result.ChannelA)}, B: {Detected(result.ChannelB)} → " +
                $"3. Hamming: {(!result.UsesHamming ? "desactivado" : result.ChannelA.Hamming is null && result.ChannelB.Hamming is null ? "No disponible" : "palabras disponibles procesadas")} → " +
                $"4. CRC A: {Crc(result.ChannelA)} · B: {Crc(result.ChannelB)}";
            static string Describe(string title, BitComparison? comparison) => comparison is null ? $"{title}: no calculable (sin alineación validada)." :
                $"{title}: esperados {comparison.ExpectedCount}, comparados {comparison.ComparedBits}, errores {comparison.IncorrectBits}, " +
                $"no concluyentes {comparison.InconclusiveBits}, faltantes {comparison.MissingBits}, BER " +
                (comparison.Ber.HasValue ? $"{comparison.Ber.Value:G5}" : "no calculable") + $", cobertura {comparison.Coverage:P1}.";
            string ChannelSummary(FdmChannelExperiment channel)
            {
                string crc = channel.Reception.CrcValid is true ? "válido" : channel.Reception.CrcValid is false ? "inválido" : "no comprobado";
                string summary = $"Canal {channel.Channel} · cabecera {(channel.Reception.HeaderValid ? "validada" : "no validada")} · CRC {crc}\n" +
                    Describe(hamming ? "Antes (bits codificados)" : "Bloque original", channel.Before);
                if (hamming)
                {
                    summary += "\n" + Describe("Después (bits originales)", channel.After);
                    if (channel.Hamming is { } h)
                        summary += $"\nHamming: {h.AnalyzedWords} palabras analizadas, {h.NonzeroSyndromes} síndromes no nulos, " +
                            $"{h.AppliedBitFlips} inversiones intentadas, {h.InconclusiveWords} palabras con '?', {h.IncompleteWords} incompletas. " +
                            $"Borrados recuperados: {h.RecoveredErasures}; palabras recuperadas por borrados: {h.ErasureRecoveredWords}; irrecuperables: {h.UnrecoverableWords}.";
                }
                return summary;
            }
            ExperimentSummaryText.Text = $"Origen analizado: {result.AudioSource}\nPerfil declarado por el usuario: " +
                (result.DeclaredAmplitudeProfile.HasValue ? FdmAmplitudeProfiles.Get(result.DeclaredAmplitudeProfile.Value).Label : "no informado") +
                $"\n{result.SampleRate} Hz · {result.AudioDurationSeconds:0.000} s · " +
                (result.UsesHamming ? $"recuperación de borrados {(result.ErasureRecoveryEnabled ? "habilitada" : "deshabilitada")} · " : "") +
                $"sincronización {result.Synchronization.State}";
            ExperimentChannelAText.Text = ChannelSummary(result.ChannelA);
            ExperimentChannelBText.Text = ChannelSummary(result.ChannelB);
            ExperimentSummaryText.Text += $"\nMedición UTC: {result.AnalyzedAt:O}";
            AnalysisSummaryHeadline.Text = $"Audio analizado: {result.AudioDurationSeconds:0.000} s · {result.SampleRate} Hz · " +
                $"CRC A: {Crc(result.ChannelA)} · CRC B: {Crc(result.ChannelB)}. Referencias declaradas solo para métricas.";
            ExperimentDetailsText.Text = result.Synchronization.Reason + "\n" + string.Join("\n\n", new[] { result.ChannelA, result.ChannelB }.Select(c =>
                $"Canal {c.Channel}: {c.Diagnostic}\nReferencia original: {c.OriginalReferenceBits}\nReferencia transmitida: {c.TransmittedReferenceBits}\n" +
                $"Recibidos: {c.Before?.ObservedBits ?? "no evaluables"}\nReconstruidos: {c.After?.ObservedBits ?? "no corresponde/no evaluables"}"));
        }
        catch (OperationCanceledException) { if (!closing) ExperimentSummaryText.Text = "Medición cancelada; no se generaron resultados."; }
        catch (Exception ex) { if (!closing) ExperimentSummaryText.Text = $"No se pudo medir: {ex.Message}"; }
        finally
        {
            experimentCancellation = null;
            if (lastExperiment is null)
            {
                AnalysisSummaryHeadline.Text = ExperimentSummaryText.Text;
                ExperimentChannelAText.Text = ExperimentChannelBText.Text = "Sin resultado nuevo. " + ExperimentSummaryText.Text;
                MetricsAView.Clear(ExperimentSummaryText.Text); MetricsBView.Clear(ExperimentSummaryText.Text);
                HammingWordTraceView.Clear(ExperimentSummaryText.Text);
                TechnicalDiagnosticView.Clear(ExperimentSummaryText.Text);
                AnalysisPipelineText.Text = "Sin resultado nuevo. " + ExperimentSummaryText.Text;
            }
            CancelExperimentButton.IsEnabled = false;
            analyzing = false;
            RefreshCapture();
        }
    }

    private async void ExportExperiment_Click(object sender, RoutedEventArgs e)
    {
        var result = lastExperiment;
        if (result is null || analyzing || capture.IsRecording || captureBusy) return;
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Resultados CSV (*.csv)|*.csv", FileName = "medicion-fdm.csv" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            await Task.Run(() => FdmExperimentCsv.Save(dialog.FileName, result));
            if (!closing && ReferenceEquals(lastExperiment, result)) ExperimentSummaryText.Text += $"\nCSV guardado: {dialog.FileName}";
        }
        catch (Exception ex) { if (!closing && ReferenceEquals(lastExperiment, result)) ExperimentSummaryText.Text += $"\nNo se pudo guardar CSV: {ex.Message}"; }
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
        monitorTimer.Stop();
        streamRevision++;
        StopLiveReception();

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
            await StopContinuousListeningAsync();
            await endingLiveReception;
            try { await capture.DisposeAsync(); }
            finally { await calibrationCapture.DisposeAsync(); }
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
