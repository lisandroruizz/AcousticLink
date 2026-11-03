#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property PublishAot=false
#:property PublishTrimmed=false
#:project ../AcousticLink.App/AcousticLink.App.csproj

using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AcousticLink.App;
using AcousticLink.App.Experiments;
using AcousticLink.App.Protocol;
using AcousticLink.App.Visualization;

Exception? failure = null;
var thread = new Thread(() =>
{
    try
    {
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown }; app.InitializeComponent();
        var window = new MainWindow { Width = 1366, Height = 768, Left = -10000, Top = -10000, ShowInTaskbar = false };
        window.Show();
        var tabs = ((Grid)window.Content).Children.OfType<TabControl>().Single();
        tabs.SelectedIndex = 3;
        var analysis = (Grid)((ScrollViewer)((TabItem)tabs.Items[3]).Content).Content;
        var inner = analysis.Children.OfType<TabControl>().Single();
        Require(inner.Items.Count == 3 && ((TabItem)inner.Items[0]).Header.ToString() == "Resumen", "Navegación interna");
        var before = FdmExperimentAnalyzer.Compare("00000000", "00?001");
        var after = FdmExperimentAnalyzer.Compare("0000", "00??");
        var reception = new FdmTextChannelResult(FdmTextReceiveState.InconclusiveBits, true, 4, null, null, null, "Incompleto");
        var channel = new FdmChannelExperiment("A", "a", "0000", "00000000", before, after, null, reception, "");
        ((MetricsView)window.FindName("MetricsAView")).Show(channel, true);
        ((MetricsView)window.FindName("MetricsBView")).Show(channel with { Channel = "B" }, true);
        Pump();
        var summary = (ScrollViewer)((TabItem)inner.Items[0]).Content;
        Require(summary.ViewportWidth > 1000 && summary.ViewportHeight > 250, "Resumen usa ancho y alto disponibles");
        // El bloque de configuración está cerrado al entrar; los resultados son la vista principal.
        Require(analysis.Children.OfType<Expander>().Single().IsExpanded == false, "Configuración secundaria");
        foreach (TabItem tab in inner.Items) { inner.SelectedItem = tab; Pump(); }
        Require(tabs.Items.Count == 4, "Se preservan las cuatro secciones principales");
        window.Close(); Pump(); app.Shutdown();
    }
    catch (Exception ex) { failure = ex; }
});
thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
if (failure is not null) throw new InvalidOperationException("Distribución compacta", failure);
Console.WriteLine("1366×768: navegación interna, resumen en paralelo, configuración secundaria y estados incompletos: OK");
static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
static void Require(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
