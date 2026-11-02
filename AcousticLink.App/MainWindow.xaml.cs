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

    public MainWindow()
    {
        InitializeComponent();
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

    protected override void OnClosed(EventArgs e)
    {
        windowLifetime.Cancel();
        windowLifetime.Dispose();
        base.OnClosed(e);
    }
}
