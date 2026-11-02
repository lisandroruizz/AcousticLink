using System.Windows;
using System.Windows.Controls;
using AcousticLink.App.Audio;
using AcousticLink.App.Signals;

namespace AcousticLink.App;

public partial class MainWindow : Window
{
    private readonly BfskToneGenerator toneGenerator = new();
    private readonly AudioPlaybackService playback = new();
    private readonly CancellationTokenSource windowLifetime = new();
    private readonly AudioCaptureService capture = new();
    private readonly System.Windows.Threading.DispatcherTimer captureTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private bool closing;
    private bool closeReady;
    private bool captureBusy;
    private bool wasCaptureRecording;

    public MainWindow()
    {
        InitializeComponent();
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

    private async void PlaySequence_Click(object sender, RoutedEventArgs e)
    {
        float[] samples;
        try
        {
            samples = toneGenerator.GenerateSequence(BitSequenceTextBox.Text);
        }
        catch (ArgumentException ex)
        {
            StatusText.Text = ex.Message;
            return;
        }

        SetPlaybackControls(false);
        StatusText.Text = "Reproduciendo secuencia BFSK...";
        try
        {
            await playback.PlayAsync(samples, BfskToneGenerator.SampleRate, windowLifetime.Token);
            StatusText.Text = "Reproducción de la secuencia finalizada.";
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

    private void BitSequenceTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (SequenceInfoText is null)
            return;
        string bits = BitSequenceTextBox.Text;
        double duration = bits.Length * BfskToneGenerator.DefaultBitDurationSeconds;
        SequenceInfoText.Text = bits.Length <= BfskToneGenerator.MaximumSequenceLength
            ? $"{bits.Length} bits · duración estimada: {duration:0.0} s"
            : $"{bits.Length} bits · máximo permitido: {BfskToneGenerator.MaximumSequenceLength}";
    }

    private void SetPlaybackControls(bool enabled)
    {
        Bit0Button.IsEnabled = enabled;
        Bit1Button.IsEnabled = enabled;
        BitSequenceTextBox.IsEnabled = enabled;
        PlaySequenceButton.IsEnabled = enabled;
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
        try { await capture.StartAsync(); }
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
        var recording = capture.Recording;
        var metrics = recording?.GetStatus() ?? (Count: 0, Peak: 0f);
        SaveCaptureButton.IsEnabled = !capture.IsRecording && !captureBusy && metrics.Count > 0;
        CaptureFormatText.Text = capture.FormatDescription;
        if (recording is not null)
            CaptureMetricsText.Text = $"Duración: {metrics.Count / (double)recording.SampleRate:0.00} s · {metrics.Count} muestras · pico: {metrics.Peak:0.0000}";
        if (capture.Error is not null) CaptureStatusText.Text = $"Error del micrófono: {capture.Error}";
        else if (capture.IsRecording) CaptureStatusText.Text = "Grabando micrófono...";
        else if (recording is not null && wasCaptureRecording) CaptureStatusText.Text = "Micrófono detenido (máximo 10 s).";
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
