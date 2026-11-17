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
        Require(tabs.Items.Count == 5 && tabs.SelectedIndex == 0, "Cinco secciones, dashboard inicial");
        T Find<T>(string name) => (T)window.FindName(name);
        Require(((System.Windows.Media.FontFamily)app.FindResource("MainFont")).Source.Contains("Segoe UI") && ((System.Windows.Media.FontFamily)app.FindResource("CodeFont")).Source.Contains("Consolas"), "Fuentes compartidas y alternativas disponibles");
        foreach (string name in new[] { "UseHammingTransmitCheckBox", "UseHammingReceiveCheckBox", "ExperimentHammingCheckBox", "RecoverReceiveErasuresCheckBox", "RecoverExperimentErasuresCheckBox" })
        {
            var option = Find<Wpf.Ui.Controls.ToggleSwitch>(name);
            Require(option.IsChecked == false && !option.IsThreeState && Equals(option.OffContent, "Desactivado"), "Interruptores conservan predeterminados y estado semántico");
        }
        Require(Find<ComboBox>("FdmProfileComboBox").Style.BasedOn is not null, "Selector conserva la plantilla del tema oscuro");
        var navigation = Find<Wpf.Ui.Controls.NavigationView>("WorkspaceNavigation");
        Require(navigation.MenuItems.Count == tabs.Items.Count, "Navegación lateral WPF UI");
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
        var workspace = Find<Grid>("DashboardWorkspaceGrid");
        var channelGrid = Find<Grid>("DashboardTransmitGrid");
        Require(ReferenceEquals(workspace, dashboard.Children.OfType<Grid>().Single()) &&
            Find<AcousticLink.App.Visualization.SignalMiniView>("MiniSignalMonitor") is not null,
            "Panel general prioriza la actividad de señal y mantiene los canales operativos");
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
        typeof(MainWindow).GetField("generatedSignal", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window,
            new AcousticLink.App.Visualization.MonitorSignal(new float[4800], 48000,
                AcousticLink.App.Visualization.MonitorSignalOrigin.Generated, "Vista digital anterior"));
        editorA.Text = "Otro"; Pump();
        Require(Find<TextBlock>("FdmTextStatusText").Text.Contains("preparación anterior"), "Edición identifica buffer digital anterior");
        editorA.Text = "Hola"; Pump();
        typeof(MainWindow).GetField("generatedSignal", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, null);
        Find<TextBlock>("FdmTextStatusText").Text = "";
        window.Width = 820;
        window.Height = 600;
        tabs.SelectedIndex = 1;
        Pump();
        Require(Grid.GetRow(Find<StackPanel>("TransmitDetailsPanel")) == 1 &&
            Find<Button>("TransmitFdmTextButton").ActualHeight > 0,
            "Emisor apila configuración y conserva Transmitir al ancho mínimo");
        tabs.SelectedIndex = 0; Pump();
        Require(Grid.GetRow(Find<Border>("DashboardSignalCard")) == 1 &&
            Find<Border>("DashboardConfiguration").ActualWidth > 300 &&
            workspace.DesiredSize.Width <= workspace.ActualWidth + 1,
            "Panel general apila operación y captura en ventana estrecha");
        var captureCardText = ((StackPanel)Find<Border>("DashboardSignalCard").Child).Children.OfType<TextBlock>().ToArray();
        var generalState = Find<TextBlock>("GeneralStateText");
        var liveCapture = Find<TextBlock>("LiveCaptureText");
        string previousState = generalState.Text, previousCapture = liveCapture.Text;
        liveCapture.Text = "Grabando · ensayo sintético"; Pump();
        Require(captureCardText.Any(text => text.Text == "Grabando · ensayo sintético"),
            "La captura manual se refleja en el resumen del receptor");
        liveCapture.Text = previousCapture;
        tabs.SelectedIndex = 1; tools.SelectedIndex = 1; Pump();
        var technicalTabs = (TabControl)((TabItem)tools.Items[1]).Content;
        foreach (int index in new[] { 0, 1, 2, 3 })
        {
            technicalTabs.SelectedIndex = index; Pump();
            var technicalGrid = Find<Grid>(new[] { "BfskTechnicalGrid", "FdmTonesGrid", "FdmSequencesGrid", "FdmFramesGrid" }[index]);
            Require(technicalGrid.ActualWidth > 300 && technicalGrid.Children.OfType<Border>().All(card => card.ActualHeight > 0),
                $"Prueba técnica {index} conserva ancho útil y controles visibles");
        }
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
        if (args.Length > 1)
        {
            System.IO.Directory.CreateDirectory(args[1]);
            void Capture(string name, int width, int height, int mode, int tool, int section = 1, int technical = -1,
                string? state = null, string? captureState = null)
            {
                window.Width = width; window.Height = height;
                Find<ComboBox>("TransmitModeComboBox").SelectedIndex = mode;
                tabs.SelectedIndex = section;
                tools.SelectedIndex = tool;
                if (tool == 1)
                    ((TabControl)((TabItem)tools.Items[1]).Content).SelectedIndex = technical < 0 ? mode == 0 ? 0 : 1 : technical;
                Pump();
                var frame = new DispatcherFrame();
                var settle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                settle.Tick += (_, _) => { settle.Stop(); frame.Continue = false; };
                settle.Start(); Dispatcher.PushFrame(frame);
                tabs.BeginAnimation(UIElement.OpacityProperty, null);
                ((System.Windows.Media.TranslateTransform)tabs.RenderTransform).BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
                if (state is not null) generalState.Text = state;
                if (captureState is not null) liveCapture.Text = captureState;
                window.UpdateLayout();
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(window);
                var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
                png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using var file = System.IO.File.Create(System.IO.Path.Combine(args[1], name)); png.Save(file);
            }
            Capture("fdm-1180.png", 1180, 740, 1, 0);
            Capture("bfsk-1180.png", 1180, 740, 0, 0);
            Capture("fdm-1366.png", 1366, 768, 1, 0);
            Capture("bfsk-1366.png", 1366, 768, 0, 0);
            Capture("fdm-125-logical.png", 1093, 614, 1, 0);
            Capture("fdm-minimum.png", 820, 600, 1, 0);
            Capture("technical-bfsk.png", 1180, 740, 0, 1);
            Capture("technical-fdm.png", 1180, 740, 1, 1);
            Capture("technical-fdm-minimum.png", 820, 600, 1, 1);
            Capture("dashboard-fdm-1180.png", 1180, 740, 1, 0, 0);
            Capture("dashboard-bfsk-1180.png", 1180, 740, 0, 0, 0);
            Capture("dashboard-fdm-1366.png", 1366, 768, 1, 0, 0);
            Capture("dashboard-bfsk-1366.png", 1366, 768, 0, 0, 0);
            Capture("dashboard-logical125.png", 1093, 614, 1, 0, 0);
            Capture("dashboard-minimum.png", 820, 600, 1, 0, 0);
            var dashboardScroll = Find<ScrollViewer>("DashboardOperationScroll");
            Require(Grid.GetRow(Find<Border>("DashboardSignalCard")) == 1 &&
                dashboardScroll.ExtentWidth <= dashboardScroll.ViewportWidth + 1 &&
                Find<AcousticLink.App.Visualization.SignalMiniView>("MiniSignalMonitor").Height == 72,
                "Vista compacta apila inspector, evita desplazamiento horizontal y reduce señal vacía");
            dashboardScroll.ScrollToEnd(); Pump();
            Require(configuration.Children.OfType<Button>().Single().IsVisible,
                "Transmisión directa permanece accesible con desplazamiento vertical");
            dashboardScroll.ScrollToTop(); Pump();
            var mini = Find<AcousticLink.App.Visualization.SignalMiniView>("MiniSignalMonitor");
            var controlledSamples = Enumerable.Range(0, 4800).Select(i => (float)(0.1 * Math.Sin(2 * Math.PI * 1200 * i / 48000))).ToArray();
            var digital = AcousticLink.App.Visualization.CarrierEnergyMap.Analyze(new(
                controlledSamples, 48000, AcousticLink.App.Visualization.MonitorSignalOrigin.Generated, "Ensayo sintético de interfaz"));
            generalState.Text = "Reproduciendo audio · ensayo sintético";
            mini.Update(digital, [], 0, digital.Signal.Duration, true);
            Require(mini.Origin == AcousticLink.App.Visualization.MonitorSignalOrigin.Generated && mini.Height == 212,
                "Salida digital sintética conserva procedencia y área de señal");
            Capture("dashboard-synthetic-output.png", 1366, 768, 1, 0, 0,
                state: "Reproduciendo audio · ensayo sintético");
            var microphone = AcousticLink.App.Visualization.CarrierEnergyMap.Analyze(new(
                controlledSamples, 48000, AcousticLink.App.Visualization.MonitorSignalOrigin.Microphone, "Ensayo sintético de interfaz"));
            generalState.Text = "Capturando audio · ensayo sintético";
            liveCapture.Text = "Grabando · ensayo sintético";
            mini.Update(microphone, [], 0, microphone.Signal.Duration, true);
            Require(mini.Origin == AcousticLink.App.Visualization.MonitorSignalOrigin.Microphone,
                "Captura sintética se identifica como origen de micrófono sin declarar recepción válida");
            Capture("dashboard-synthetic-capture.png", 1366, 768, 1, 0, 0,
                state: "Capturando audio · ensayo sintético", captureState: "Grabando · ensayo sintético");
            generalState.Text = previousState;
            liveCapture.Text = previousCapture;
            mini.Update(null, [], 0, 0, false);
            Find<TabControl>("DashboardTools").SelectedIndex = 1;
            Find<ComboBox>("MonitorSourceComboBox").SelectedIndex = 0;
            Capture("dashboard-monitor-empty.png", 1180, 740, 1, 0, 0);
            var syntheticSamples = Enumerable.Range(0, 48000).Select(i => (float)(0.1 * Math.Sin(2 * Math.PI * 1200 * i / 48000))).ToArray();
            var syntheticSignal = new AcousticLink.App.Visualization.MonitorSignal(syntheticSamples, 48000,
                AcousticLink.App.Visualization.MonitorSignalOrigin.Generated, "Señal sintética del check");
            var syntheticMap = AcousticLink.App.Visualization.CarrierEnergyMap.Analyze(syntheticSignal);
            typeof(MainWindow).GetField("generatedSignal", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, syntheticSignal);
            typeof(MainWindow).GetField("generatedMap", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, syntheticMap);
            Find<ComboBox>("MonitorSourceComboBox").SelectedIndex = 1;
            Capture("dashboard-monitor-synthetic.png", 1180, 740, 1, 0, 0);
            Find<TabControl>("DashboardTools").SelectedIndex = 0;
            typeof(MainWindow).GetField("generatedMap", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, null);
            typeof(MainWindow).GetField("generatedSignal", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, null);
            Capture("technical-bfsk-final.png", 1180, 740, 0, 1, 1, 0);
            Capture("technical-fdm-tones-final.png", 1180, 740, 1, 1, 1, 1);
            Capture("technical-fdm-sequences-final.png", 1180, 740, 1, 1, 1, 2);
            Capture("technical-fdm-frames-final.png", 1180, 740, 1, 1, 1, 3);
            Capture("technical-fdm-minimum-final.png", 820, 600, 1, 1, 1, 1);
            Capture("generated-empty.png", 1180, 740, 1, 2);
            var generatedSamples = Enumerable.Range(0, 48000).Select(i => (float)(0.1 * Math.Sin(2 * Math.PI * 1200 * i / 48000))).ToArray();
            var generatedCheckSignal = new AcousticLink.App.Visualization.MonitorSignal(generatedSamples, 48000,
                AcousticLink.App.Visualization.MonitorSignalOrigin.Generated, "Señal digital sintética del check");
            Find<AcousticLink.App.Visualization.SignalMonitorView>("GeneratedSignalMonitor").Show(
                AcousticLink.App.Visualization.CarrierEnergyMap.Analyze(generatedCheckSignal));
            Capture("generated-synthetic.png", 1180, 740, 1, 2);
            Capture("generated-synthetic-minimum.png", 820, 600, 1, 2);
            tools.SelectedIndex = 0;
            Find<TextBox>("FdmTextBTextBox").Text = "ñññññ";
            Capture("fdm-invalid.png", 1180, 740, 1, 0);
            Find<TextBox>("FdmTextBTextBox").Text = "🎵🎵";
            Capture("fdm-utf8.png", 1180, 740, 1, 0);
            Find<TextBox>("FdmTextBTextBox").Text = "Mundo";
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
