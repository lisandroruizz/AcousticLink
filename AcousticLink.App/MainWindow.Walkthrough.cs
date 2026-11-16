using System.Windows;
using System.Windows.Controls;
using AcousticLink.App.Protocol;
using AcousticLink.App.Signals;
using AcousticLink.App.Visualization;

namespace AcousticLink.App;

public partial class MainWindow
{
    private int walkthroughRequest;

    private void WalkthroughProfile_Changed(object sender, SelectionChangedEventArgs e)
    {
        InvalidateWalkthrough("Cambió el perfil de amplitud. Prepará un recorrido nuevo.");
        MarkGeneratedSignalOutdated();
    }

    private async void WalkthroughTransmitter_Click(object sender, RoutedEventArgs e)
    {
        int request = ++walkthroughRequest;
        string a = FdmTextATextBox.Text, b = FdmTextBTextBox.Text;
        bool bfsk = IsBfskTransmit, hamming = UseHammingTransmitCheckBox.IsChecked == true;
        var profile = SelectedFdmProfile;
        WalkthroughView.Clear("Preparando el buffer digital del mensaje actual…");
        try
        {
            var data = await Task.Run(() =>
            {
                float[] samples = bfsk ? new BfskTextModem().Generate(a, hamming)
                    : new FdmBitstreamGenerator().GenerateTextFrames(a, b, hamming, profile);
                return TransmissionWalkthrough.Transmitter(a, bfsk ? null : b, hamming, samples);
            }, windowLifetime.Token);
            if (!closing && request == walkthroughRequest && bfsk == IsBfskTransmit && profile == SelectedFdmProfile && hamming == (UseHammingTransmitCheckBox.IsChecked == true)
                && a == FdmTextATextBox.Text && (bfsk || b == FdmTextBTextBox.Text)) WalkthroughView.Show(data);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!closing && request == walkthroughRequest) WalkthroughView.Clear("No se pudo preparar el emisor: " + ex.Message); }
    }

    private async void WalkthroughReceiver_Click(object sender, RoutedEventArgs e)
    {
        int request = ++walkthroughRequest;
        var historical = ContinuousHistoryComboBox.SelectedItem as ContinuousReception;
        var selected = audioSelection.Current;
        var audio = selected?.Audio;
        var live = audio is not null && ReferenceEquals(liveReception?.Audio, audio) ? liveReception?.Snapshot : null;
        bool bfsk;
        FdmTextReceiveResult? result;
        float[]? samples;
        int rate;
        string source;
        if (historical is not null)
        {
            bfsk = historical.IsBfsk; result = historical.Result; samples = historical.Samples; rate = historical.SampleRate;
            source = $"Histórico #{historical.Id} · sesión {historical.SessionId:N} · inicio {historical.StartSample} muestras";
        }
        else
        {
            bfsk = IsBfskReceive;
            result = live?.ObservedResult ?? (audio is not null && ReferenceEquals(presentedReceptionSource, audio) ? presentedReception : null);
            samples = null; rate = audio?.SampleRate ?? 0;
            source = selected is null ? "Sin captura seleccionada" : $"{selected.Source} · {selected.FormatDescription}" +
                (live is null ? " · análisis posterior" : $" · recepción incremental, {live.SampleCount} muestras procesadas");
        }
        if (result is null || (samples is null && audio is null) || rate <= 0)
        { WalkthroughView.Clear("No hay un resultado del receptor para la fuente seleccionada. Analizá la captura o el WAV primero."); return; }
        if (historical is null && bfsk != IsBfskReceive)
        { WalkthroughView.Clear("La modalidad del resultado no coincide con la selección actual."); return; }
        WalkthroughView.Clear("Preparando la forma de onda del resultado seleccionado…");
        try
        {
            var prepared = await Task.Run(() =>
            {
                float[] actual = samples ?? audio!.GetSamples();
                return (Count: actual.Length, Buckets: WaveformView.Reduce(actual));
            }, windowLifetime.Token);
            if (closing || request != walkthroughRequest || historical is not null && !ReferenceEquals(ContinuousHistoryComboBox.SelectedItem, historical)
                || historical is null && (!ReferenceEquals(audioSelection.Current?.Audio, audio) ||
                    (live is not null ? !ReferenceEquals(liveReception?.Snapshot, live) : !ReferenceEquals(presentedReception, result)) || bfsk != IsBfskReceive)) return;
            WalkthroughView.Show(TransmissionWalkthrough.Receiver(result, bfsk, source, rate, prepared.Count, prepared.Buckets));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!closing && request == walkthroughRequest) WalkthroughView.Clear("No se pudo inspeccionar el audio: " + ex.Message); }
    }

    private void InvalidateWalkthrough(string reason)
    {
        walkthroughRequest++;
        WalkthroughView?.Clear(reason);
    }
}
