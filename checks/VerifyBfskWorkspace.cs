#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property PublishAot=false
#:property PublishTrimmed=false
#:project ../AcousticLink.App/AcousticLink.App.csproj

using System.Windows;
using System.IO;
using System.Windows.Controls;
using System.Windows.Threading;
using AcousticLink.App;
using AcousticLink.App.Visualization;

Exception? failure = null;
var thread = new Thread(() =>
{
    try
    {
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        var window = new MainWindow { ShowInTaskbar = false, Left = -10000, Top = -10000 };
        ((CheckBox)window.FindName("AutoListenCheckBox")).IsChecked = false;
        window.Show(); Pump();
        T Find<T>(string name) => (T)window.FindName(name);
        var transmit = Find<ComboBox>("TransmitModeComboBox");
        var receive = Find<ComboBox>("ReceiverModeComboBox");
        Require(transmit.SelectedIndex == 1 && receive.SelectedIndex == 1, "FDM predeterminado");
        transmit.SelectedIndex = 0; Pump();
        Require(Find<StackPanel>("TransmitChannelBPanel").Visibility == Visibility.Collapsed &&
            Find<StackPanel>("FdmProfilePanel").Visibility == Visibility.Collapsed &&
            Find<TextBlock>("FdmTextInfoText").Text.Contains("sin relleno ni canal B") &&
            Find<TextBlock>("TransmitDurationText").Text.Contains("s de transmisión"), "Emisor BFSK de un canal");
        var dashboard = (StackPanel)Find<ScrollViewer>("DashboardOperationScroll").Content;
        Require(dashboard.Children.OfType<ComboBox>().Single().SelectedIndex == 0 &&
            dashboard.Children.OfType<Grid>().First().Children.OfType<Border>().Last().Visibility == Visibility.Collapsed,
            "Panel general refleja BFSK");
        receive.SelectedIndex = 0; Pump();
        Require(Find<StackPanel>("ReceptionChannelBPanel").Visibility == Visibility.Collapsed &&
            Find<TabItem>("ReceptionBDetailTab").Visibility == Visibility.Collapsed &&
            Find<FrameExplorerView>("ReceivedFrameExplorer").SingleChannel &&
            Find<SignalMonitorView>("ReceiverSignalMonitor").SingleChannel &&
            Find<ReceptionProgressView>("LiveReceptionInspector").SingleChannel,
            "Receptor BFSK oculta segundo canal");
        if (args.Length > 0)
        {
            window.Width = 1366; window.Height = 768; Pump(); window.UpdateLayout();
            var image = new System.Windows.Media.Imaging.RenderTargetBitmap(1366, 768, 96, 96,
                System.Windows.Media.PixelFormats.Pbgra32);
            image.Render(window);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
            using var output = File.Create(args[0]); encoder.Save(output);
        }
        if (args.Length > 1)
        {
            Find<TabControl>("MainSectionsTabs").SelectedIndex = 2; Pump(); window.UpdateLayout();
            var frame = new DispatcherFrame();
            var settle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            settle.Tick += (_, _) => { settle.Stop(); frame.Continue = false; };
            settle.Start(); Dispatcher.PushFrame(frame);
            Pump(); window.UpdateLayout();
            var image = new System.Windows.Media.Imaging.RenderTargetBitmap(1366, 768, 96, 96,
                System.Windows.Media.PixelFormats.Pbgra32);
            image.Render(window);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
            using var output = File.Create(args[1]); encoder.Save(output);
        }
        transmit.SelectedIndex = 1; receive.SelectedIndex = 1; Pump();
        Require(Find<StackPanel>("TransmitChannelBPanel").Visibility == Visibility.Visible &&
            Find<StackPanel>("ReceptionChannelBPanel").Visibility == Visibility.Visible &&
            !Find<FrameExplorerView>("ReceivedFrameExplorer").SingleChannel,
            "FDM conserva dos canales");
        window.Close(); Pump(); app.Shutdown();
    }
    catch (Exception ex) { failure = ex; Application.Current?.Shutdown(); }
});
thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
if (failure is not null) throw new InvalidOperationException("WPF BFSK falló", failure);
Console.WriteLine("WPF BFSK/FDM: selectores, contenido y controles por modalidad OK");
static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
static void Require(bool ok, string label) { if (!ok) throw new Exception(label); }
