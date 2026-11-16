#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property PublishAot=false
#:property PublishTrimmed=false
#:project ../AcousticLink.App/AcousticLink.App.csproj

using System.Reflection;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AcousticLink.App;
using AcousticLink.App.Experiments;

Exception? failure = null;
var thread = new Thread(() =>
{
    try
    {
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        var window = new MainWindow { ShowInTaskbar = false, Left = -10000, Top = -10000, Width = 1366, Height = 768 };
        ((CheckBox)window.FindName("AutoListenCheckBox")).IsChecked = false;
        window.Show(); Pump();
        T Find<T>(string name) => (T)window.FindName(name);
        Find<TabControl>("MainSectionsTabs").SelectedIndex = 4;
        Pump(); window.UpdateLayout();
        Require(Find<TextBlock>("CalibrationStateText").Text.Contains("referencia"), "Estado inicial");
        var mode = Find<ComboBox>("CalibrationModeCombo");
        mode.SelectedIndex = 1; Pump();
        Require(Find<TextBlock>("CalibrationFrequenciesText").Text.Contains("3700") &&
            Find<TextBlock>("CalibrationNoiseText").Text == "No disponible", "Cambio FDM invalida referencia");
        mode.SelectedIndex = 0; Pump();
        var result = new CalibrationResult(CalibrationMode.Bfsk, "Micrófono",
            new CalibrationLevel(0.1, -20, 0.2, 4, 48000, false), 1,
            [new CalibrationTone(1200, 0.0001, 0.01, 19.96, "Disponible"),
             new CalibrationTone(1900, 0.0001, 0.005, 16.9, "Disponible")],
            "Prueba sintética localizada", ["Observe los niveles."]);
        typeof(MainWindow).GetMethod("ShowCalibrationResult", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [result]);
        Pump(); window.UpdateLayout();
        Require(Find<StackPanel>("CalibrationToneRows").Children.Count == 2 &&
            Find<TextBlock>("CalibrationSignalHeading").Text.Contains("MICRÓFONO"), "BFSK sin canal B");
        Require(Find<StackPanel>("CalibrationToneRows").Children.Cast<StackPanel>().All(row =>
            row.Children.OfType<Grid>().Count() == 2), "Barras de ruido y prueba");
        if (args.Length > 0)
        {
            var image = new System.Windows.Media.Imaging.RenderTargetBitmap(1366, 768, 96, 96,
                System.Windows.Media.PixelFormats.Pbgra32);
            image.Render(window);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
            using var output = File.Create(args[0]); encoder.Save(output);
        }
        mode.SelectedIndex = 1; Pump();
        Require(Find<StackPanel>("CalibrationToneRows").Children.Count == 0 &&
            Find<TextBlock>("CalibrationSignalText").Text == "No disponible", "Resultados tardíos invalidados por modalidad");
        window.Close(); Pump(); app.Shutdown();
    }
    catch (Exception ex) { failure = ex; Application.Current?.Shutdown(); }
});
thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
if (failure is not null) throw new InvalidOperationException("WPF calibración falló", failure);
Console.WriteLine("WPF calibración: navegación, modalidad, procedencia, barras e invalidación: OK");
static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
static void Require(bool ok, string label) { if (!ok) throw new Exception(label); }
