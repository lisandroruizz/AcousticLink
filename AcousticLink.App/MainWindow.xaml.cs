using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace AcousticLink.App
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly Signals.BfskToneGenerator toneGenerator = new();
        private readonly Audio.AudioPlaybackService playback = new();
        private readonly CancellationTokenSource windowLifetime = new();

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void PlayBit0_Click(object sender, RoutedEventArgs e) => await PlayToneAsync(false);
        private async void PlayBit1_Click(object sender, RoutedEventArgs e) => await PlayToneAsync(true);

        private async Task PlayToneAsync(bool bit)
        {
            Bit0Button.IsEnabled = Bit1Button.IsEnabled = false;
            StatusText.Text = "Reproduciendo tono...";
            try
            {
                float[] samples = toneGenerator.Generate(bit, durationSeconds: 1);
                await playback.PlayAsync(samples, Signals.BfskToneGenerator.SampleRate, windowLifetime.Token);
                StatusText.Text = "Reproducción finalizada.";
            }
            catch (OperationCanceledException) when (windowLifetime.IsCancellationRequested)
            {
                // Cierre normal de la ventana.
            }
            catch (Exception ex)
            {
                StatusText.Text = "No se pudo reproducir el tono.";
                if (!windowLifetime.IsCancellationRequested)
                    MessageBox.Show(this, $"No se pudo inicializar o reproducir el audio.\n{ex.Message}",
                        "AcousticLink", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Bit0Button.IsEnabled = Bit1Button.IsEnabled = true;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            windowLifetime.Cancel();
            windowLifetime.Dispose();
            base.OnClosed(e);
        }
    }
}
