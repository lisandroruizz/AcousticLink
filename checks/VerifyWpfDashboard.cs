#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property PublishAot=false
#:property PublishTrimmed=false
#:project ../AcousticLink.App/AcousticLink.App.csproj

using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AcousticLink.App;

// Prueba de enlaces y estados WPF sin reproducir ni abrir dispositivos de audio.
Exception? failure = null;
var thread = new Thread(() =>
{
    try
    {
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        var window = new MainWindow { ShowInTaskbar = false, Left = -10000, Top = -10000 };
        window.Show();
        Pump();
        var root = (Grid)window.Content;
        var tabs = root.Children.OfType<TabControl>().Single();
        Require(tabs.Items.Count == 4 && tabs.SelectedIndex == 0, "Cuatro secciones, dashboard inicial");
        T Find<T>(string name) => (T)window.FindName(name);
        var editorA = Find<TextBox>("FdmTextATextBox");
        var dashboard = (StackPanel)((ScrollViewer)((TabItem)tabs.Items[0]).Content).Content;
        var channelGrid = dashboard.Children.OfType<Grid>().First();
        var dashboardA = ((StackPanel)channelGrid.Children.OfType<Border>().First().Child).Children.OfType<TextBox>().Single();
        Require(dashboardA.Text == editorA.Text && dashboardA.Text == "Hola", "Enlace inicial a editor en pestaña inactiva");
        dashboardA.Text = "ñ";
        Pump();
        Require(editorA.Text == "ñ", "Dashboard modifica el texto real");
        tabs.SelectedIndex = 1;
        Pump();
        editorA.Text = "Hola";
        Pump();
        tabs.SelectedIndex = 0;
        Pump();
        Require(dashboardA.Text == "Hola", "Navegación conserva y comparte valores");
        var configuration = (StackPanel)dashboard.Children.OfType<Border>().First().Child;
        var dashboardHamming = configuration.Children.OfType<CheckBox>().Single();
        var dashboardProfile = configuration.Children.OfType<ComboBox>().Single();
        dashboardHamming.IsChecked = true;
        dashboardProfile.SelectedIndex = 1;
        Pump();
        Require(Find<CheckBox>("UseHammingTransmitCheckBox").IsChecked == true &&
            Find<ComboBox>("FdmProfileComboBox").SelectedIndex == 1 &&
            Find<TextBlock>("FdmTextInfoText").Text.Contains("Hamming"), "Hamming y perfil compartidos");
        dashboardHamming.IsChecked = false;
        dashboardProfile.SelectedIndex = 0;
        Pump();
        Require(Find<CheckBox>("UseHammingTransmitCheckBox").IsChecked == false &&
            Find<ComboBox>("FdmProfileComboBox").SelectedIndex == 0, "Regreso a modo original");
        window.Width = 820;
        window.Height = 600;
        Pump();
        foreach (TabItem tab in tabs.Items)
        {
            tabs.SelectedItem = tab;
            Pump();
            Require(((ScrollViewer)tab.Content).ViewportWidth > 0, "Contenido medible al tamaño mínimo");
        }
        void Invoke(string name, params object[] args) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
        Invoke("SetPlaybackControls", false);
        Pump();
        Require(!dashboardA.IsEnabled, "Dashboard respeta bloqueo de reproducción");
        Invoke("SetPlaybackControls", true);
        Find<TextBlock>("ReceivedChannelAText").Text = "Texto anterior";
        Find<TextBlock>("ExperimentChannelAText").Text = "Métrica anterior";
        Find<Button>("ExportExperimentButton").IsEnabled = true;
        Invoke("InvalidateAudioAnalysis");
        Pump();
        Require(Find<TextBlock>("ReceivedChannelAText").Text == "Sin analizar" &&
            Find<TextBlock>("ExperimentChannelAText").Text == "Sin analizar" &&
            !Find<Button>("ExportExperimentButton").IsEnabled, "Invalidación de tarjetas y CSV");
        window.Close();
        Pump();
        app.Shutdown();
    }
    catch (Exception ex) { failure = ex; }
});
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();
if (failure is not null) throw new InvalidOperationException("Verificación WPF fallida", failure);
Console.WriteLine("WPF: recursos oscuros, cuatro pestañas, bindings entre vistas, bloqueo e invalidación: OK");
static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
static void Require(bool value, string message)
{
    if (!value) throw new InvalidOperationException(message);
}
