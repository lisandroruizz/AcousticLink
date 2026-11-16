using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AcousticLink.App.Visualization;

public sealed class ReceiverSessionDiagnosticsView : UserControl
{
    private readonly TextBlock title = Text(18, Brushes.White), source = Text(12, Brushes.LightGray),
        acoustic = Text(13, Brushes.LightSkyBlue), sync = Text(13, Brushes.Khaki),
        channels = Text(13, Brushes.LightGray), counts = Text(13, Brushes.LightGray),
        samples = Text(12, Brushes.LightGray), note = Text(12, Brushes.LightGray);
    private readonly ListBox timeline = new() { MinHeight = 140,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        HorizontalAlignment = HorizontalAlignment.Stretch };
    private Guid lastSession;
    private int lastEventCount = -1;
    private TimeSpan lastEventTime;

    public ReceiverSessionDiagnosticsView()
    {
        var panel = new StackPanel { Margin = new Thickness(12, 8, 12, 8) };
        title.FontWeight = FontWeights.SemiBold;
        panel.Children.Add(title); panel.Children.Add(source);
        panel.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 8) });
        panel.Children.Add(acoustic); panel.Children.Add(sync); panel.Children.Add(channels);
        panel.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 8) });
        panel.Children.Add(counts); panel.Children.Add(samples); panel.Children.Add(note);
        panel.Children.Add(new TextBlock { Text = "CRONOLOGÍA · tiempo relativo a esta sesión", FontSize = 13,
            FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 5) });
        timeline.Background = Brushes.Transparent;
        ScrollViewer.SetHorizontalScrollBarVisibility(timeline, ScrollBarVisibility.Disabled);
        panel.Children.Add(timeline);
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Clear();
    }

    private static TextBlock Text(double size, Brush color) => new()
    { FontSize = size, Foreground = color, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) };

    public void Clear()
    {
        title.Text = "Sin sesión de recepción";
        source.Text = "Iniciá una captura, escucha continua o análisis WAV.";
        acoustic.Text = sync.Text = channels.Text = counts.Text = samples.Text = note.Text = "";
        timeline.ItemsSource = null; lastSession = Guid.Empty; lastEventCount = -1;
    }

    public void Show(ReceiverDiagnosticSnapshot value)
    {
        title.Text = $"{value.Mode} · {(value.IsActive ? "en curso" : "finalizada")}";
        source.Text = $"{value.Source} · {(value.IsLive ? "sesión de micrófono" : "análisis posterior")} · " +
            $"sesión {value.SessionId.ToString("N")[..8]} · {value.Elapsed:mm\\:ss\\.fff}";
        source.ToolTip = value.SessionId.ToString();
        string activitySource = value.IsAcousticSource ? "Micrófono" : "Audio del archivo";
        acoustic.Text = value.CapturedSamples == 0 ? $"{activitySource}: esperando muestras."
            : value.AcousticActivityObserved
                ? $"{activitySource}: pico digital ≥ {ReceiverSessionDiagnostics.ActivityPeakThreshold:0.###}; no demuestra transmisión."
                : $"{activitySource}: muestras presentes, pico aún < {ReceiverSessionDiagnostics.ActivityPeakThreshold:0.###}.";
        sync.Text = $"Sincronización: {value.CandidateHeaders} candidatas completas observadas · {value.UnconfirmedCandidates} no confirmadas · " +
            $"{value.ConfirmedHeaders} cabeceras validadas. Una espera sin cabecera no es un fallo.";
        channels.Text = value.IsBfsk ? $"Canal A: {State(value.ChannelAState)} · BFSK sin canal B"
            : $"Canal A: {State(value.ChannelAState)} · canal B: {State(value.ChannelBState)}";
        counts.Text = $"Transmisiones reconocidas finalizadas: {value.CompletedTransmissions} · tramas de canal evaluadas: {value.EvaluatedChannelFrames} · " +
            $"mensajes válidos: {value.ValidMessages} · rechazadas: {value.RejectedChannelFrames} " +
            $"(CRC: {value.CrcRejections}; incompletas: {value.IncompleteChannelFrames}).";
        samples.Text = $"Muestras disponibles: {value.CapturedSamples:N0} · entregadas al lector en bloques completos: {value.ProcessedSamples:N0}" +
            (value.SampleRate > 0 ? $" · {value.SampleRate:N0} Hz · tiempo de sesión {value.Elapsed.TotalSeconds:0.0} s" : "");
        note.Text = $"Fallos de sincronización identificables: {value.SynchronizationFailures} · errores de dispositivo: {value.DeviceErrors}. " +
            $"Cronología: últimos {value.Events.Count}/{ReceiverSessionDiagnostics.EventLimit}; los contadores abarcan toda la sesión.";
        if (lastSession == value.SessionId && lastEventCount == value.Events.Count &&
            (value.Events.Count == 0 || lastEventTime == value.Events[^1].At)) return;
        lastSession = value.SessionId; lastEventCount = value.Events.Count;
        lastEventTime = value.Events.Count == 0 ? TimeSpan.Zero : value.Events[^1].At;
        timeline.ItemsSource = value.Events.Select(e => new ListBoxItem
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = new TextBlock { Text = $"{e.At:mm\\:ss\\.fff} · {e.Stage}: {e.Detail}",
                TextWrapping = TextWrapping.Wrap, FontSize = 12 }
        }).ToArray();
        if (timeline.Items.Count > 0) timeline.ScrollIntoView(timeline.Items[^1]);
    }

    private static string State(string? state) => state switch
    {
        null => "sin resultado",
        "Valid" => "mensaje válido",
        "HeaderNotSynchronized" => "buscando cabecera",
        "AmbiguousSynchronization" => "sincronización ambigua",
        "InvalidLength" => "longitud inválida",
        "InconclusiveLength" => "longitud inconclusa",
        "Truncated" => "contenido incompleto",
        "InconclusiveBits" => "símbolos inconclusos",
        "CrcIncorrect" => "CRC rechazado",
        "InvalidUtf8" => "UTF-8 inválido",
        _ => state
    };
}
