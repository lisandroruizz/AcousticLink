using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AcousticLink.App.Audio;
using AcousticLink.App.Experiments;
using AcousticLink.App.Signals;
using AcousticLink.App.Visualization;

namespace AcousticLink.App;

public partial class MainWindow
{
    private readonly AudioCaptureService calibrationCapture = new();
    private readonly AudioPlaybackService calibrationPlayback = new();
    private CancellationTokenSource? calibrationCancellation;
    private CalibrationNoise? calibrationNoise;
    private int calibrationRevision;
    private bool calibrationBusy;
    private bool calibrationEmitting;
    private CalibrationMode SelectedCalibrationMode => CalibrationModeCombo.SelectedIndex == 1 ? CalibrationMode.Fdm : CalibrationMode.Bfsk;

    private void CalibrationWorkspace_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool stacked = CalibrationWorkspace.ActualWidth < 900;
        CalibrationQuickActions.Visibility = stacked ? Visibility.Visible : Visibility.Collapsed;
        CalibrationWorkspace.ColumnDefinitions[0].Width = stacked ? new GridLength(1, GridUnitType.Star) : new GridLength(340);
        CalibrationWorkspace.ColumnDefinitions[1].Width = stacked ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        CalibrationWorkspace.RowDefinitions[1].Height = stacked ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        CalibrationWorkspace.RowDefinitions[2].Height = stacked ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        CalibrationStepsScroll.MaxHeight = stacked ? Math.Max(145, CalibrationWorkspace.ActualHeight * 0.31) : double.PositiveInfinity;
        Grid.SetColumn(CalibrationResultsScroll, stacked ? 0 : 1);
        Grid.SetRow(CalibrationResultsScroll, stacked ? 2 : 1);
    }

    private void CalibrationMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (CalibrationNoiseText is null) return;
        InvalidateCalibration("Modalidad cambiada; capture una nueva referencia de ruido.");
        CalibrationFrequenciesText.Text = SelectedCalibrationMode == CalibrationMode.Bfsk
            ? "Canal A: 1200 y 1900 Hz · salida digital 48 kHz"
            : "Canal A: 1200 y 1900 Hz · canal B: 3000 y 3700 Hz · salida digital 48 kHz";
    }

    private void CalibrationDevice_Changed(object sender, TextChangedEventArgs e)
    {
        if (CalibrationNoiseText is not null && calibrationNoise is not null)
            InvalidateCalibration("Condiciones declaradas cambiadas; capture una nueva referencia.");
    }

    private void InvalidateCalibration(string reason)
    {
        calibrationRevision++;
        calibrationCancellation?.Cancel();
        calibrationNoise = null;
        CalibrationCaptureButton.IsEnabled = CalibrationWavButton.IsEnabled = false;
        CalibrationNoiseText.Text = CalibrationSignalText.Text = "No disponible";
        CalibrationNoiseSummary.Text = CalibrationSignalSummary.Text = "Pendiente";
        CalibrationNoiseSummary.Foreground = (Brush)FindResource("WarningBrush");
        CalibrationSignalSummary.Foreground = (Brush)FindResource("WarningBrush");
        CalibrationNoiseMetrics.Text = CalibrationSignalMetrics.Text = "Sin medición";
        CalibrationNoiseStepText.Text = "Referencia invalidada · repetí la captura";
        CalibrationSignalHeading.Text = "PRUEBA · PENDIENTE";
        CalibrationToneRows.Children.Clear();
        CalibrationToneEmptyText.Visibility = Visibility.Visible;
        CalibrationToneEmptyText.Text = "Referencia desactualizada. Capturá una nueva referencia antes de repetir la prueba.";
        CalibrationAdviceText.Text = "La calibración caracteriza el canal; valide mensajes mediante el receptor y CRC.";
        CalibrationStateText.Text = reason;
        CalibrationStateText.Foreground = (Brush)FindResource("WarningBrush");
    }

    private async void CalibrationNoise_Click(object sender, RoutedEventArgs e) => await CaptureCalibrationAsync(true);
    private async void CalibrationCapture_Click(object sender, RoutedEventArgs e) => await CaptureCalibrationAsync(false);

    private async Task CaptureCalibrationAsync(bool noiseStage)
    {
        if (calibrationBusy || calibrationEmitting || capture.IsRecording || captureBusy || continuousListening is not null)
        {
            CalibrationStateText.Text = "Detenga primero la captura o escucha normal y cualquier emisión de calibración.";
            return;
        }
        if (!noiseStage && calibrationNoise is null)
        {
            CalibrationStateText.Text = "Capture primero una referencia de ruido con la misma modalidad y dispositivo.";
            return;
        }
        int seconds = noiseStage ? int.Parse(((ComboBoxItem)CalibrationNoiseDuration.SelectedItem).Tag.ToString()!) : 10;
        int revision = ++calibrationRevision;
        var mode = SelectedCalibrationMode;
        string device = CalibrationDeviceText.Text.Trim();
        if (device.Length == 0) { CalibrationStateText.Text = "Identifique el dispositivo y las condiciones del ensayo."; return; }
        calibrationBusy = true;
        if (noiseStage) { calibrationNoise = null; CalibrationNoiseText.Text = "Midiendo…"; CalibrationNoiseSummary.Text = "Capturando…"; CalibrationNoiseSummary.Foreground = (Brush)FindResource("ChannelABrush"); CalibrationNoiseMetrics.Text = $"Objetivo: {seconds} s de audio real"; CalibrationNoiseStepText.Text = "Captura en curso"; ClearCalibrationTest(); }
        else ClearCalibrationTest();
        CalibrationNoiseButton.IsEnabled = CalibrationCaptureButton.IsEnabled = CalibrationWavButton.IsEnabled = CalibrationEmitButton.IsEnabled = false;
        CalibrationModeCombo.IsEnabled = CalibrationDeviceText.IsEnabled = false;
        CalibrationCancelButton.IsEnabled = true;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(windowLifetime.Token);
        calibrationCancellation = cancellation;
        try
        {
            CalibrationStateText.Text = noiseStage ? "Iniciando referencia de ruido…" : "Iniciando captura de tonos…";
            CalibrationStateText.Foreground = (Brush)FindResource("ChannelABrush");
            await calibrationCapture.StartAsync(seconds, cancellation.Token);
            CalibrationDeviceState.Text = calibrationCapture.FormatDescription;
            while (calibrationCapture.IsRecording && !calibrationCapture.SessionCompleted)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var status = calibrationCapture.Recording?.GetMetrics();
                CalibrationStateText.Text = $"{(noiseStage ? "Ruido" : "Prueba")}: {status?.DurationSeconds ?? 0:0.0}/{seconds} s de muestras reales";
                await Task.Delay(200, cancellation.Token);
            }
            await calibrationCapture.StopAsync();
            cancellation.Token.ThrowIfCancellationRequested();
            var recording = calibrationCapture.Recording ?? throw new InvalidOperationException("No se recibieron muestras.");
            var metrics = recording.GetMetrics();
            if (metrics.StopReason == CaptureStopReason.Error || calibrationCapture.Error is not null)
                throw new InvalidOperationException(calibrationCapture.Error ?? "La captura finalizó con error.");
            if (metrics.DurationSeconds < seconds * 0.8)
                throw new InvalidOperationException($"Captura incompleta: {metrics.DurationSeconds:0.00} s reales de {seconds} s previstos.");
            float[] samples = recording.GetSamples();
            if (noiseStage)
            {
                var result = await Task.Run(() => AcousticCalibration.MeasureNoise(samples, recording.SampleRate,
                    mode, device + " · " + calibrationCapture.FormatDescription, "Micrófono", cancellation.Token), cancellation.Token);
                if (revision != calibrationRevision || closing) return;
                calibrationNoise = result;
                CalibrationNoiseText.Text = DescribeLevel(result.Level) + $"\nOrigen: {result.Source}\n{result.DeviceDescription}\n" +
                    string.Join("\n", AcousticCalibration.Frequencies(mode).Select((frequency, i) =>
                        $"{frequency:0} Hz: {FormatPower(result.BandPowers[i])}"));
                CalibrationNoiseSummary.Text = "Disponible";
                CalibrationNoiseSummary.Foreground = (Brush)FindResource("SuccessBrush");
                CalibrationNoiseMetrics.Text = ShortLevel(result.Level);
                CalibrationNoiseStepText.Text = "Lista · seguí con la prueba en el receptor";
                CalibrationStateText.Text = "Referencia obtenida. Inicie la captura en el receptor y emita la prueba desde el otro equipo.";
                CalibrationStateText.Foreground = (Brush)FindResource("SuccessBrush");
            }
            else
            {
                var reference = calibrationNoise!;
                if (reference.DeviceDescription != device + " · " + calibrationCapture.FormatDescription)
                    throw new InvalidOperationException("El formato o las condiciones declaradas difieren de la referencia; vuelva a medir el ruido.");
                var result = await Task.Run(() => AcousticCalibration.Analyze(samples, recording.SampleRate, mode,
                    reference, "Micrófono", cancellation.Token), cancellation.Token);
                if (revision != calibrationRevision || closing) return;
                ShowCalibrationResult(result);
            }
        }
        catch (OperationCanceledException)
        {
            if (!closing && revision == calibrationRevision)
            {
                if (noiseStage) { CalibrationNoiseText.Text = "No disponible: captura cancelada."; CalibrationNoiseSummary.Text = "Cancelada"; CalibrationNoiseSummary.Foreground = (Brush)FindResource("WarningBrush"); CalibrationNoiseMetrics.Text = "Datos parciales descartados"; CalibrationNoiseStepText.Text = "Repetí la referencia"; }
                else { CalibrationSignalSummary.Text = "Cancelada"; CalibrationSignalSummary.Foreground = (Brush)FindResource("WarningBrush"); CalibrationSignalMetrics.Text = "Datos parciales descartados"; }
                CalibrationStateText.Text = "Captura cancelada. Los datos parciales no se evalúan.";
                CalibrationStateText.Foreground = (Brush)FindResource("WarningBrush");
            }
        }
        catch (Exception ex)
        {
            if (!closing && revision == calibrationRevision)
            {
                if (noiseStage) { CalibrationNoiseText.Text = "No disponible: captura inconclusa."; CalibrationNoiseSummary.Text = "Inconclusa"; CalibrationNoiseSummary.Foreground = (Brush)FindResource("ErrorBrush"); CalibrationNoiseMetrics.Text = "Sin referencia válida"; CalibrationNoiseStepText.Text = "Repetí la referencia"; }
                else { CalibrationSignalSummary.Text = "Inconclusa"; CalibrationSignalSummary.Foreground = (Brush)FindResource("WarningBrush"); CalibrationSignalMetrics.Text = "Sin prueba válida"; }
                CalibrationStateText.Text = "Calibración inconclusa: " + ex.Message;
                CalibrationStateText.Foreground = (Brush)FindResource("ErrorBrush");
            }
        }
        finally
        {
            try { await calibrationCapture.StopAsync(); } catch (Exception ex) { if (!closing) CalibrationDeviceState.Text = "Error al liberar micrófono: " + ex.Message; }
            if (ReferenceEquals(calibrationCancellation, cancellation)) calibrationCancellation = null;
            calibrationBusy = false;
            if (!closing)
            {
                CalibrationNoiseButton.IsEnabled = true;
                CalibrationCaptureButton.IsEnabled = CalibrationWavButton.IsEnabled = calibrationNoise is not null;
                CalibrationEmitButton.IsEnabled = true;
                CalibrationModeCombo.IsEnabled = CalibrationDeviceText.IsEnabled = true;
                CalibrationCancelButton.IsEnabled = calibrationEmitting;
                CalibrationDeviceState.Text = calibrationCapture.IsRecording ? calibrationCapture.FormatDescription : "Dispositivo: inactivo · último formato: " + calibrationCapture.FormatDescription;
            }
        }
    }

    private async void CalibrationEmit_Click(object sender, RoutedEventArgs e)
    {
        if (closing) return;
        if (calibrationEmitting || calibrationBusy || playing)
        {
            CalibrationStateText.Text = "Esperá a que termine la operación actual antes de iniciar la prueba.";
            return;
        }
        calibrationEmitting = true;
        SetPlaybackControls(false);
        CalibrationEmitButton.IsEnabled = false;
        CalibrationNoiseButton.IsEnabled = CalibrationCaptureButton.IsEnabled = CalibrationWavButton.IsEnabled = false;
        CalibrationCancelButton.IsEnabled = true;
        CalibrationModeCombo.IsEnabled = CalibrationDeviceText.IsEnabled = false;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(windowLifetime.Token);
        calibrationCancellation = cancellation;
        try
        {
            var mode = SelectedCalibrationMode;
            CalibrationStateText.Text = "Reproduciendo señal digital en este equipo; esto no confirma recepción: " + string.Join(" → ", AcousticCalibration.Frequencies(mode).Select(f => $"{f:0} Hz"));
            CalibrationStateText.Foreground = (Brush)FindResource("ChannelABrush");
            await calibrationPlayback.PlayAsync(AcousticCalibration.Generate(mode), BfskToneGenerator.SampleRate, cancellation.Token);
            if (!closing) { CalibrationStateText.Text = "Prueba emitida digitalmente; su recepción acústica debe medirse en el otro equipo."; CalibrationStateText.Foreground = (Brush)FindResource("WarningBrush"); }
        }
        catch (OperationCanceledException) { if (!closing) { CalibrationStateText.Text = "Emisión cancelada."; CalibrationStateText.Foreground = (Brush)FindResource("WarningBrush"); } }
        catch (Exception ex) { if (!closing) { CalibrationStateText.Text = "No se pudo reproducir la prueba: " + ex.Message; CalibrationStateText.Foreground = (Brush)FindResource("ErrorBrush"); } }
        finally
        {
            if (ReferenceEquals(calibrationCancellation, cancellation)) calibrationCancellation = null;
            calibrationEmitting = false;
            SetPlaybackControls(true);
            if (!closing)
            {
                CalibrationEmitButton.IsEnabled = CalibrationNoiseButton.IsEnabled = true;
                CalibrationCaptureButton.IsEnabled = CalibrationWavButton.IsEnabled = calibrationNoise is not null;
                CalibrationModeCombo.IsEnabled = CalibrationDeviceText.IsEnabled = true;
                CalibrationCancelButton.IsEnabled = calibrationBusy;
            }
        }
    }

    private async void CalibrationWav_Click(object sender, RoutedEventArgs e)
    {
        if (calibrationBusy || calibrationEmitting || calibrationNoise is null) { CalibrationStateText.Text = "Se requiere una referencia de ruido antes de analizar el WAV."; return; }
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Audio WAV (*.wav)|*.wav" };
        if (dialog.ShowDialog(this) != true) return;
        int revision = ++calibrationRevision;
        var mode = SelectedCalibrationMode;
        var reference = calibrationNoise;
        calibrationBusy = true;
        ClearCalibrationTest();
        CalibrationCancelButton.IsEnabled = true;
        CalibrationNoiseButton.IsEnabled = CalibrationCaptureButton.IsEnabled = CalibrationWavButton.IsEnabled = CalibrationEmitButton.IsEnabled = false;
        CalibrationModeCombo.IsEnabled = CalibrationDeviceText.IsEnabled = false;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(windowLifetime.Token);
        calibrationCancellation = cancellation;
        try
        {
            CalibrationStateText.Text = "Validando y analizando WAV…";
            CalibrationStateText.Foreground = (Brush)FindResource("ChannelABrush");
            var result = await Task.Run(() =>
            {
                var selection = new AudioRecordingSelection();
                selection.ImportWav(dialog.FileName, cancellation.Token);
                var audio = selection.Current!.Audio;
                return AcousticCalibration.Analyze(audio.GetSamples(), audio.SampleRate, mode, reference!, selection.Current.Source,
                    cancellation.Token, comparableReference: false);
            }, cancellation.Token);
            if (revision == calibrationRevision && !closing) ShowCalibrationResult(result);
        }
        catch (OperationCanceledException) { if (!closing && revision == calibrationRevision) { CalibrationStateText.Text = "Análisis WAV cancelado."; CalibrationStateText.Foreground = (Brush)FindResource("WarningBrush"); CalibrationSignalSummary.Text = "Cancelado"; CalibrationSignalSummary.Foreground = (Brush)FindResource("WarningBrush"); } }
        catch (Exception ex) { if (!closing && revision == calibrationRevision) { CalibrationStateText.Text = "WAV inconcluso: " + ex.Message; CalibrationStateText.Foreground = (Brush)FindResource("ErrorBrush"); CalibrationSignalSummary.Text = "Inconclusa"; CalibrationSignalSummary.Foreground = (Brush)FindResource("WarningBrush"); CalibrationSignalMetrics.Text = "Sin prueba válida"; } }
        finally
        {
            if (ReferenceEquals(calibrationCancellation, cancellation)) calibrationCancellation = null;
            calibrationBusy = false;
            if (!closing) { CalibrationNoiseButton.IsEnabled = CalibrationEmitButton.IsEnabled = true; CalibrationCaptureButton.IsEnabled = CalibrationWavButton.IsEnabled = calibrationNoise is not null; CalibrationCancelButton.IsEnabled = false; CalibrationModeCombo.IsEnabled = CalibrationDeviceText.IsEnabled = true; }
        }
    }

    private void CalibrationCancel_Click(object sender, RoutedEventArgs e) => calibrationCancellation?.Cancel();

    private void ClearCalibrationTest()
    {
        CalibrationSignalText.Text = "No disponible";
        CalibrationSignalHeading.Text = "PRUEBA · PENDIENTE";
        CalibrationSignalSummary.Text = "Pendiente";
        CalibrationSignalSummary.Foreground = (Brush)FindResource("WarningBrush");
        CalibrationSignalMetrics.Text = "Sin medición";
        CalibrationToneRows.Children.Clear();
        CalibrationToneEmptyText.Visibility = Visibility.Visible;
        CalibrationToneEmptyText.Text = "Prueba pendiente. Capturá o importá una señal conocida después de la referencia.";
        CalibrationAdviceText.Text = "Ensayo pendiente. La recepción de mensajes requiere validación CRC.";
    }

    private static string DescribeLevel(CalibrationLevel level) =>
        $"{level.DurationSeconds:0.00} s · {level.SampleRate} Hz · RMS digital {level.Rms:0.000000} · " +
        $"{(double.IsNegativeInfinity(level.Dbfs) ? "−∞" : level.Dbfs.ToString("0.0"))} dBFS · pico {level.Peak:0.000} · " +
        (level.PossibleClipping ? "posible clipping" : "sin clipping detectado");

    private static string ShortLevel(CalibrationLevel level) =>
        $"RMS {level.Rms:0.000000} · {(double.IsNegativeInfinity(level.Dbfs) ? "−∞" : level.Dbfs.ToString("0.0"))} dBFS\n" +
        $"Pico {level.Peak:0.000} · {(level.PossibleClipping ? "posible clipping" : "sin clipping detectado")}";

    private static string FormatPower(double power) => power > 0
        ? $"{CarrierEnergyMap.Decibels(power):0.0} dB re potencia digital 1"
        : "sin energía medible (−∞ dB)";

    private void ShowCalibrationResult(CalibrationResult result)
    {
        CalibrationStateText.Text = result.Status;
        CalibrationStateText.Foreground = (Brush)FindResource(result.StartSeconds.HasValue ? "SuccessBrush" : "WarningBrush");
        CalibrationSignalHeading.Text = result.Source == "Micrófono" ? "PRUEBA · MICRÓFONO" : "PRUEBA · WAV IMPORTADO";
        CalibrationSignalSummary.Text = result.StartSeconds.HasValue ? "Secuencia localizada" : "Inconclusa";
        CalibrationSignalSummary.Foreground = (Brush)FindResource(result.StartSeconds.HasValue ? "SuccessBrush" : "WarningBrush");
        var snr = result.Tones.Where(t => t.SnrDb.HasValue).Select(t => $"{t.Frequency:0} Hz {t.SnrDb:0.0} dB");
        CalibrationSignalMetrics.Text = ShortLevel(result.Level) + "\nSNR por frecuencia: " +
            (result.Source != "Micrófono" ? "no comparable · WAV" : snr.Any() ? string.Join(" · ", snr) : "no disponible");
        CalibrationSignalText.Text = DescribeLevel(result.Level) + $"\nOrigen: {result.Source}\n" +
            (result.StartSeconds.HasValue ? $"Inicio estimado de la secuencia: {result.StartSeconds:0.00} s" : "Inicio: no disponible");
        CalibrationToneRows.Children.Clear();
        CalibrationToneEmptyText.Visibility = result.Tones.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CalibrationToneEmptyText.Text = result.Tones.Count == 0
            ? "Sin energías de prueba disponibles. La secuencia no produjo bandas evaluables; consultá la lectura y repetí la captura."
            : string.Empty;
        double maximum = result.Tones.Count == 0 ? 0 : result.Tones.Max(t => t.ReceivedPower);
        foreach (var tone in result.Tones)
        {
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            string channel = tone.Frequency < FdmToneMixer.ChannelBBit0Frequency ? "A" : "B";
            row.Children.Add(new TextBlock { Text = $"Canal {channel} · {tone.Frequency:0} Hz", FontWeight = FontWeights.SemiBold });
            void AddBar(string label, double power, Brush color)
            {
                var line = new Grid { Margin = new Thickness(0, 3, 0, 0) };
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                line.ColumnDefinitions.Add(new ColumnDefinition());
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
                line.Children.Add(new TextBlock { Text = label, Foreground = (Brush)FindResource("MutedBrush") });
                var bar = new ProgressBar { Minimum = 0, Maximum = 60, Height = 11,
                    Value = power > 0 && maximum > 0 ? Math.Clamp(60 + 10 * Math.Log10(power / maximum), 0, 60) : 0,
                    Foreground = color, Background = (Brush)FindResource("CardBrush") };
                Grid.SetColumn(bar, 1); line.Children.Add(bar);
                var value = new TextBlock { Text = power > 0 ? $"{CarrierEnergyMap.Decibels(power):0.0} dB" : "No disponible",
                    Margin = new Thickness(8, 0, 0, 0), Foreground = (Brush)FindResource("MutedBrush") };
                Grid.SetColumn(value, 2); line.Children.Add(value);
                row.Children.Add(line);
            }
            AddBar("Ruido", tone.NoisePower, (Brush)FindResource("ChannelABrush"));
            AddBar("Prueba", tone.ReceivedPower, (Brush)FindResource("ChannelBBrush"));
            row.Children.Add(new TextBlock { Text = tone.SnrDb.HasValue ? $"SNR condicional estimada en banda: {tone.SnrDb:0.0} dB · mismo micrófono/ganancia declarados" : tone.Status, Foreground = (Brush)FindResource("MutedBrush") });
            CalibrationToneRows.Children.Add(row);
        }
        CalibrationAdviceText.Text = string.Join("\n", result.Recommendations.Select((item, index) => $"{index + 1}. {item}"));
    }
}
