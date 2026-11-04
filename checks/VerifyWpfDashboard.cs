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
        // Aislar la prueba de la preferencia personal; no escribirla ni abrir el micrófono.
        ((System.Windows.Controls.CheckBox)window.FindName("AutoListenCheckBox")).IsChecked = false;
        window.Show();
        Pump();
        var root = (Grid)window.Content;
        var tabs = (TabControl)window.FindName("MainSectionsTabs");
        Require(tabs.Items.Count == 4 && tabs.SelectedIndex == 0, "Cuatro secciones, dashboard inicial");
        T Find<T>(string name) => (T)window.FindName(name);
        Require(((System.Windows.Media.FontFamily)app.FindResource("MainFont")).Source.Contains("Segoe UI") && ((System.Windows.Media.FontFamily)app.FindResource("CodeFont")).Source.Contains("Consolas"), "Fuentes compartidas y alternativas disponibles");
        foreach (string name in new[] { "UseHammingTransmitCheckBox", "UseHammingReceiveCheckBox", "ExperimentHammingCheckBox", "RecoverReceiveErasuresCheckBox", "RecoverExperimentErasuresCheckBox" })
        {
            var option = Find<Wpf.Ui.Controls.ToggleSwitch>(name);
            Require(option.IsChecked == false && !option.IsThreeState && Equals(option.OffContent, "Desactivado"), "Interruptores conservan predeterminados y estado semántico");
        }
        Require(Find<ComboBox>("FdmProfileComboBox").Style.BasedOn is not null, "Selector conserva la plantilla del tema oscuro");
        var navigation = Find<Wpf.Ui.Controls.NavigationView>("WorkspaceNavigation");
        Require(navigation.MenuItems.Count == 4, "Navegación lateral WPF UI");
        var sendItem = (Wpf.Ui.Controls.NavigationViewItem)navigation.MenuItems[1]!;
        sendItem.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Require(tabs.SelectedIndex == 1 && sendItem.IsActive && Find<TextBlock>("ActiveSectionText").Text == "Emisor", "Selección lateral y encabezado real");
        Require(!SystemParameters.ClientAreaAnimation || tabs.HasAnimatedProperties, "Transición conectada al cambio de sección");
        var paneToggle = (System.Windows.Controls.Primitives.ButtonBase)typeof(Wpf.Ui.Controls.NavigationView).GetProperty("ToggleButton", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(navigation)!;
        paneToggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); Pump(); Require(!navigation.IsPaneOpen, "Botón nativo contrae el panel");
        navigation.IsPaneOpen = true; tabs.SelectedIndex = 0; Pump();
        Require(Find<System.Windows.Controls.Primitives.StatusBar>("WorkspaceStatusBar").ActualHeight > 0, "Barra de estado permanente");
        var tools = Find<TabControl>("TransmitterTools");
        Require(tools.Items.Count == 3 && ((TabItem)tools.Items[1]).Content is TabControl { Items.Count: 4 }, "Herramientas BFSK/FDM conservadas en navegación contextual");
        tabs.SelectedIndex = 1; tools.SelectedIndex = 1; Pump();
        tools.SelectedIndex = 0; Pump();
        var transmit = Find<Button>("TransmitFdmTextButton"); transmit.ApplyTemplate();
        Require(transmit.Template.FindName("KeyboardFocus", transmit) is Border, "Contorno de foco compartido disponible");
        var editorA = Find<TextBox>("FdmTextATextBox");
        var dashboard = (StackPanel)Find<ScrollViewer>("DashboardOperationScroll").Content;
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
        var configuration = (StackPanel)Find<Border>("DashboardConfiguration").Child;
        var dashboardHamming = configuration.Children.OfType<Wpf.Ui.Controls.ToggleSwitch>().Single();
        var dashboardProfile = configuration.Children.OfType<ComboBox>().Single();
        dashboardHamming.IsChecked = true;
        dashboardProfile.SelectedIndex = 1;
        Pump();
        Require(Find<System.Windows.Controls.Primitives.ToggleButton>("UseHammingTransmitCheckBox").IsChecked == true &&
            Find<ComboBox>("FdmProfileComboBox").SelectedIndex == 1 &&
            Find<TextBlock>("FdmTextInfoText").Text.Contains("Hamming"), "Hamming y perfil compartidos");
        dashboardHamming.IsChecked = false;
        dashboardProfile.SelectedIndex = 0;
        Pump();
        Require(Find<System.Windows.Controls.Primitives.ToggleButton>("UseHammingTransmitCheckBox").IsChecked == false &&
            Find<ComboBox>("FdmProfileComboBox").SelectedIndex == 0, "Regreso a modo original");
        editorA.Text = ""; Pump();
        Require(!Find<Button>("TransmitFdmTextButton").IsEnabled && Find<TextBlock>("FdmTextInfoText").Text.Contains("Canal A") && !Find<TextBlock>("FdmTextInfoText").Text.Contains("255"), "Límite FDM y canal específico para texto vacío");
        typeof(MainWindow).GetMethod("TransmitFdmText_Click", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { window, new RoutedEventArgs() });
        Require(!(bool)typeof(MainWindow).GetField("playing", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!, "Invocación inválida no inicia audio");
        editorA.Text = "Hola"; var editorB = Find<TextBox>("FdmTextBTextBox"); editorB.Text = "ñññññ"; Pump();
        Require(!Find<Button>("TransmitFdmTextButton").IsEnabled && Find<TextBlock>("FdmTextInfoText").Text.Contains("Canal B") && Find<TextBlock>("TransmitDurationText").Text.Contains("corregí"), "Duración explica el error de ocho bytes");
        editorB.Text = "🎵🎵"; Pump(); Require(Find<Button>("TransmitFdmTextButton").IsEnabled, "Ocho bytes multibyte admitidos");
        Require(AcousticLink.App.Visualization.FdmTextInputValidation.Check("\uD800", "A").Error!.Contains("Unicode"), "UTF-8 estricto sin exponer parámetros internos");
        editorB.Text = "Mundo"; Pump();
        Require(!Find<TextBlock>("FdmTextInfoText").Text.Contains("Parameter"), "Sin excepciones internas en la presentación");
        window.Width = 820;
        window.Height = 600;
        Pump();
        foreach (TabItem tab in tabs.Items)
        {
            tabs.SelectedItem = tab;
            Pump();
            Require(((FrameworkElement)tab.Content).ActualWidth > 0, "Contenido medible al tamaño mínimo");
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
        if (args.Length > 0)
        {
            window.Width = 1366; window.Height = 768; navigation.IsPaneOpen = true; tabs.SelectedIndex = 1; Pump();
            var frame = new DispatcherFrame();
            var settle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            settle.Tick += (_, _) => { settle.Stop(); frame.Continue = false; }; settle.Start(); Dispatcher.PushFrame(frame);
            tabs.BeginAnimation(UIElement.OpacityProperty, null);
            ((System.Windows.Media.TranslateTransform)tabs.RenderTransform).BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
            window.UpdateLayout();
            var image = new System.Windows.Media.Imaging.RenderTargetBitmap(1366, 768, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            image.Render(window);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
            using var output = System.IO.File.Create(args[0]); encoder.Save(output);
        }
        var selection = (AcousticLink.App.Audio.AudioRecordingSelection)typeof(MainWindow).GetField("audioSelection", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
        var recording = new AcousticLink.App.Audio.CapturedAudio(44100, 1);
        recording.Append(new float[4410], 4410); recording.Complete(AcousticLink.App.Audio.CaptureStopReason.Manual);
        selection.SelectMicrophone(recording, "Formato de comprobación"); Invoke("RefreshCapture"); Pump();
        Require(Find<TextBlock>("WorkspaceAudioStatusText").Text.Contains("44100 Hz") && Find<TextBlock>("WorkspaceAudioStatusText").Text.Contains("Micrófono"), "Barra de estado refleja la selección real y su tasa");
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
Console.WriteLine("WPF: recursos oscuros, navegación lateral, transiciones, bindings entre vistas, bloqueo e invalidación: OK");
static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
static void Require(bool value, string message)
{
    if (!value) throw new InvalidOperationException(message);
}
