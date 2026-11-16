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
        Require(!Find<Button>("CalibrationCaptureButton").IsEnabled && !Find<Button>("CalibrationWavButton").IsEnabled,
            "Prueba y WAV esperan referencia");
        Snapshot("inicial-1366x768.png");
        var mode = Find<ComboBox>("CalibrationModeCombo");
        mode.SelectedIndex = 1; Pump();
        Require(Find<TextBlock>("CalibrationFrequenciesText").Text.Contains("3700") &&
            Find<TextBlock>("CalibrationNoiseText").Text == "No disponible", "Cambio FDM invalida referencia");
        mode.SelectedIndex = 0; Pump();
        void SetSyntheticReference(CalibrationMode calibrationMode)
        {
            var reference = new CalibrationNoise(calibrationMode, "Micrófono simulado · condiciones constantes", "Sintético", DateTimeOffset.UtcNow,
                new CalibrationLevel(0.003, -50.5, 0.01, 5, 48000, false),
                calibrationMode == CalibrationMode.Bfsk ? [0.0001, 0.0001] : [0.0001, 0.0001, 0.0001, 0.0001]);
            typeof(MainWindow).GetField("calibrationNoise", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, reference);
            Find<TextBlock>("CalibrationNoiseText").Text = "5.00 s · 48000 Hz · RMS digital 0.003000 · −50.5 dBFS · pico 0.010 · sin clipping detectado\nOrigen: referencia sintética del check";
            Find<TextBlock>("CalibrationNoiseSummary").Text = "Disponible";
            Find<TextBlock>("CalibrationNoiseMetrics").Text = "RMS 0.003000 · −50.5 dBFS\nPico 0.010 · sin clipping detectado";
            Find<TextBlock>("CalibrationNoiseStepText").Text = "Lista · seguí con la prueba en el receptor";
            Find<Button>("CalibrationCaptureButton").IsEnabled = Find<Button>("CalibrationWavButton").IsEnabled = true;
            Find<TextBlock>("CalibrationStateText").Text = "Referencia sintética disponible · sin captura acústica física. Seguí con la prueba.";
            Snapshot(calibrationMode == CalibrationMode.Bfsk ? "referencia-1366x768.png" : "referencia-fdm-1366x768.png");
        }
        SetSyntheticReference(CalibrationMode.Bfsk);
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
        Find<TextBlock>("CalibrationStateText").Text = "Prueba sintética localizada · sin ensayo acústico físico";
        Snapshot("bfsk-1366x768.png");
        window.Width = 1180; window.Height = 740; Pump(); Snapshot("bfsk-1180x740.png");
        window.Width = 1366 / 1.25; window.Height = 768 / 1.25; Pump(); Snapshot("bfsk-logical125.png");
        window.Width = 820; window.Height = 600; Pump(); Snapshot("bfsk-min-width.png");
        Require(Find<Border>("CalibrationQuickActions").IsVisible, "Acción rápida visible en ancho mínimo");
        window.Width = 1366; window.Height = 768; Pump();
        typeof(MainWindow).GetMethod("ClearCalibrationTest", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
        Require(Find<TextBlock>("CalibrationSignalSummary").Text == "Pendiente" &&
            ReferenceEquals(Find<TextBlock>("CalibrationSignalSummary").Foreground, window.FindResource("WarningBrush")),
            "Nueva prueba elimina el color de éxito anterior");
        var inconclusive = result with { StartSeconds = null, Status = "Medición inconclusa · ensayo sintético", Tones = [], Recommendations = ["Secuencia no localizada: repetí la captura."] };
        typeof(MainWindow).GetMethod("ShowCalibrationResult", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [inconclusive]);
        Require(Find<TextBlock>("CalibrationSignalSummary").Text == "Inconclusa", "Resultado inconcluso inequívoco");
        Snapshot("inconcluso-1366x768.png");
        var imported = result with { Source = "WAV importado", Status = "Secuencia localizada · WAV sintético", Tones = result.Tones.Select(t => t with { SnrDb = null, Status = "SNR no comparable: procedencia WAV" }).ToArray() };
        typeof(MainWindow).GetMethod("ShowCalibrationResult", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [imported]);
        Require(Find<TextBlock>("CalibrationSignalMetrics").Text.Contains("no comparable") &&
            Find<TextBlock>("CalibrationSignalHeading").Text.Contains("WAV"), "Procedencia WAV y SNR no comparable");
        Snapshot("wav-1366x768.png");
        mode.SelectedIndex = 1; Pump();
        Require(Find<StackPanel>("CalibrationToneRows").Children.Count == 0 &&
            Find<TextBlock>("CalibrationSignalText").Text == "No disponible" &&
            !Find<Button>("CalibrationCaptureButton").IsEnabled && !Find<Button>("CalibrationWavButton").IsEnabled,
            "Resultados tardíos y acciones invalidados por modalidad");
        SetSyntheticReference(CalibrationMode.Fdm);
        var fdm = result with { Mode = CalibrationMode.Fdm, Status = "Secuencia localizada · ensayo sintético", Tones = [.. result.Tones,
            new CalibrationTone(3000, 0.0001, 0.008, 18.9, "Disponible"), new CalibrationTone(3700, 0.0001, 0.006, 17.7, "Disponible")] };
        typeof(MainWindow).GetMethod("ShowCalibrationResult", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [fdm]);
        Require(Find<StackPanel>("CalibrationToneRows").Children.Count == 4, "Cuatro portadoras FDM");
        Snapshot("fdm-1366x768.png");
        void Snapshot(string name)
        {
            if (args.Length == 0) return;
            Directory.CreateDirectory(args[0]);
            window.UpdateLayout();
            var image = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96,
                System.Windows.Media.PixelFormats.Pbgra32);
            image.Render(window);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
            using var output = File.Create(Path.Combine(args[0], name)); encoder.Save(output);
        }
        window.Close(); Pump(); app.Shutdown();
    }
    catch (Exception ex) { failure = ex; Application.Current?.Shutdown(); }
});
thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
if (failure is not null) throw new InvalidOperationException("WPF calibración falló", failure);
Console.WriteLine("WPF calibración: navegación, modalidad, procedencia, barras e invalidación: OK");
static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
static void Require(bool ok, string label) { if (!ok) throw new Exception(label); }
