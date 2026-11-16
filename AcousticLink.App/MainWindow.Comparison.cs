using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using AcousticLink.App.Experiments;
using AcousticLink.App.Signals;

namespace AcousticLink.App;

public partial class MainWindow
{
    private readonly List<ComparisonTrial> comparisonHistory = [];
    private CancellationTokenSource? comparisonCancellation;

    private void AnalysisSummary_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool stacked = AnalysisSummaryWorkspace.ActualWidth < 790;
        AnalysisSummaryWorkspace.ColumnDefinitions[1].Width = stacked ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(AnalysisChannelBCard, stacked ? 0 : 1);
        Grid.SetRow(AnalysisChannelBCard, stacked ? 1 : 0);
        AnalysisChannelACard.Margin = stacked ? new Thickness(0, 0, 0, 8) : new Thickness(0, 0, 5, 8);
        AnalysisChannelBCard.Margin = stacked ? new Thickness(0, 0, 0, 8) : new Thickness(5, 0, 0, 8);
    }

    private void ComparisonWorkspace_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool stacked = ComparisonWorkspace.ActualWidth < 850;
        ComparisonWorkspace.ColumnDefinitions[0].Width = stacked ? new GridLength(1, GridUnitType.Star) : new GridLength(320);
        ComparisonWorkspace.ColumnDefinitions[1].Width = stacked ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        ComparisonWorkspace.RowDefinitions[0].Height = stacked ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        ComparisonWorkspace.RowDefinitions[1].Height = stacked ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        // En el formato apilado la configuración sigue desplazable sin quedar reducida
        // a una franja que oculte casi todos los selectores.
        ComparisonSettingsScroll.MaxHeight = stacked ? Math.Max(210, Math.Min(300, ComparisonWorkspace.ActualHeight * 0.48)) : double.PositiveInfinity;
        Grid.SetColumn(ComparisonResultsScroll, stacked ? 0 : 1);
        Grid.SetRow(ComparisonResultsScroll, stacked ? 1 : 0);
    }

    private void InitializeComparison()
    {
        ComparisonProfileCombo.ItemsSource = FdmAmplitudeProfiles.All;
        ComparisonProfileCombo.SelectedIndex = 0;
        ComparisonSource_Changed(ComparisonSourceCombo, new RoutedEventArgs());
    }

    private void AnalysisTool_Changed(object sender, SelectionChangedEventArgs e)
    {
        InternalTool_Changed(sender, e);
        if (!IsInitialized || !ReferenceEquals(e.OriginalSource, AnalysisTools)) return;
        bool comparison = (AnalysisTools.SelectedItem as TabItem)?.Header?.ToString() == "Comparación";
        bool walkthrough = (AnalysisTools.SelectedItem as TabItem)?.Header?.ToString() == "Recorrido";
        if ((comparison || walkthrough) && AnalysisSettingsToggle.IsChecked == true)
        {
            AnalysisSettingsToggle.IsChecked = false;
            WorkspacePanel_Click(AnalysisSettingsToggle, new RoutedEventArgs());
        }
        AnalysisLegacyToolbar.Visibility = comparison || walkthrough ? Visibility.Collapsed : Visibility.Visible;
        AnalysisConfigurationWarning.Visibility = comparison || walkthrough ? Visibility.Collapsed : Visibility.Visible;
        if (comparison) ComparisonSource_Changed(ComparisonSourceCombo, e);
    }

    private void ComparisonSource_Changed(object sender, RoutedEventArgs e)
    {
        if (ComparisonModeCombo is null || ComparisonSourceCombo is null || ComparisonHammingCheck is null ||
            ComparisonErasureCheck is null || ComparisonSnrText is null) return;
        bool generated = ComparisonSourceCombo.SelectedIndex == 0;
        ComparisonModeCombo.IsEnabled = !generated;
        ComparisonReferenceCheck.IsEnabled = !generated;
        if (generated) ComparisonReferenceCheck.IsChecked = true;
        ComparisonErasureCheck.IsEnabled = !generated && ComparisonHammingCheck.IsChecked == true;
        if (generated) ComparisonErasureCheck.IsChecked = false;
        ComparisonSnrText.IsEnabled = ComparisonSeedText.IsEnabled = ComparisonRepeatText.IsEnabled = generated;
        if (!generated) ComparisonRepeatText.Text = "1";
        var selected = audioSelection.Current;
        ComparisonSelectedAudioText.Text = generated ? "Fuente actual: señal digital generada; par BFSK/FDM"
            : selected is null ? "Audio seleccionado: No disponible. Elegí una captura finalizada o WAV en Receptor."
            : $"Audio seleccionado: {selected.Source} · {selected.Audio.SampleRate} Hz";
    }

    private void ComparisonCancel_Click(object sender, RoutedEventArgs e) => comparisonCancellation?.Cancel();

    private async void ComparisonRun_Click(object sender, RoutedEventArgs e)
    {
        bool generatedRequest = ComparisonSourceCombo.SelectedIndex == 0;
        if (comparisonCancellation is not null || closing || (!generatedRequest && (capture.IsRecording || captureBusy)))
        { ComparisonStatusText.Text = "Finalizá la captura activa antes de medir."; return; }
        try
        {
            string id = ComparisonIdText.Text.Trim();
            string a = ComparisonAText.Text, b = ComparisonBText.Text;
            bool generated = ComparisonSourceCombo.SelectedIndex == 0;
            bool hamming = ComparisonHammingCheck.IsChecked == true;
            bool recover = hamming && ComparisonErasureCheck.IsChecked == true;
            bool references = ComparisonReferenceCheck.IsChecked == true;
            var profile = ((FdmAmplitudeSetting)ComparisonProfileCombo.SelectedItem).Profile;
            if (id.Length == 0) throw new ArgumentException("Indicá un identificador de serie.");
            if (!int.TryParse(ComparisonRepeatText.Text, out int repeat) || repeat is < 1 or > 10)
                throw new ArgumentException("Las repeticiones deben estar entre 1 y 10.");
            if (!int.TryParse(ComparisonSeedText.Text, out int seed)) throw new ArgumentException("Semilla entera inválida.");
            double? snr = null;
            if (!string.IsNullOrWhiteSpace(ComparisonSnrText.Text))
            {
                if (!double.TryParse(ComparisonSnrText.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double value) ||
                    !double.IsFinite(value) || value is < 0 or > 60)
                    throw new ArgumentException("La SNR debe estar entre 0 y 60 dB.");
                snr = value;
            }
            if (generated && (recover || !references))
                throw new ArgumentException("El par generado usa referencias conocidas y recuperación de borrados desactivada.");
            if (generated || references)
            {
                _ = new AcousticLink.App.Protocol.BfskTextModem().Build(a, hamming);
                if (generated || ComparisonModeCombo.SelectedIndex == 1)
                    _ = new AcousticLink.App.Protocol.BfskTextModem().Build(b, hamming);
            }
            var selected = generated ? null : audioSelection.Current;
            if (!generated && selected is null) throw new InvalidOperationException("Seleccioná primero un WAV o una captura finalizada.");
            if (!generated && repeat != 1) throw new ArgumentException("El mismo WAV o captura es una sola observación; realizá nuevas capturas para repetir el ensayo.");
            var proposedIds = Enumerable.Range(1, repeat).SelectMany(n => generated
                ? new[] { $"{id}-{n}-BFSK", $"{id}-{n}-FDM" } : new[] { $"{id}-{n}" }).ToArray();
            if (comparisonHistory.Any(t => proposedIds.Contains(t.Id, StringComparer.OrdinalIgnoreCase)))
                throw new ArgumentException("El identificador ya existe en el historial. Elegí otra serie.");
            var mode = ComparisonModeCombo.SelectedIndex == 0 ? ExperimentMode.Bfsk : ExperimentMode.Fdm;
            var source = selected?.Source.StartsWith("WAV", StringComparison.OrdinalIgnoreCase) == true
                ? ExperimentSource.ImportedWav : ExperimentSource.Microphone;
            ComparisonRunButton.IsEnabled = false; ComparisonCancelButton.IsEnabled = true;
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(windowLifetime.Token);
            comparisonCancellation = cancellation;
            for (int i = 0; i < repeat; i++)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                ComparisonStatusText.Text = $"Procesando {i + 1}/{repeat}…";
                int index = i;
                if (generated)
                {
                    var pair = await Task.Run(() => new ComparisonExperiment().RunPair($"{id}-{index + 1}", a, b,
                        hamming, snr, checked(seed + index * 3), profile, cancellation.Token), cancellation.Token);
                    comparisonHistory.Add(pair.Bfsk); comparisonHistory.Add(pair.Fdm);
                }
                else
                {
                    var trial = await Task.Run(() => new ComparisonExperiment().AnalyzeObserved($"{id}-{index + 1}",
                        selected!.Audio.GetSamples(), selected.Audio.SampleRate, mode, source, selected.Source,
                        a, b, references, hamming, recover, mode == ExperimentMode.Fdm ? profile : null,
                        cancellation.Token), cancellation.Token);
                    comparisonHistory.Add(trial);
                }
            }
            RefreshComparisonHistory();
            ComparisonStatusText.Text = generated ? $"Finalizado: {repeat} pares generados y procesados. No son mediciones acústicas."
                : $"Finalizado: {repeat} análisis del mismo audio; una sola observación acústica de origen.";
        }
        catch (OperationCanceledException) { ComparisonStatusText.Text = "Cancelado; se conservan los ensayos completos anteriores."; RefreshComparisonHistory(); }
        catch (Exception ex) { ComparisonStatusText.Text = "Error: " + ex.Message; RefreshComparisonHistory(); }
        finally { comparisonCancellation = null; ComparisonRunButton.IsEnabled = true; ComparisonCancelButton.IsEnabled = false; }
    }

    private void RefreshComparisonHistory()
    {
        ComparisonHistoryList.ItemsSource = null;
        ComparisonHistoryList.ItemsSource = comparisonHistory.ToArray();
        if (comparisonHistory.Count >= 2 && comparisonHistory[^1].PairId is not null)
        {
            ComparisonHistoryList.SelectedItems.Add(comparisonHistory[^2]);
            ComparisonHistoryList.SelectedItems.Add(comparisonHistory[^1]);
        }
        else if (comparisonHistory.Count > 0) ComparisonHistoryList.SelectedItem = comparisonHistory[^1];
        ShowComparison();
    }

    private void ComparisonHistory_Changed(object sender, SelectionChangedEventArgs e) => ShowComparison();

    private void ShowComparison()
    {
        if (ComparisonCharts is null || ComparisonHistoryList is null) return;
        var selected = ComparisonHistoryList.SelectedItems.Cast<ComparisonTrial>().ToArray();
        ComparisonCharts.Children.Clear(); ComparisonSpectrumCharts.Children.Clear();
        if (selected.Length == 0)
        { ComparisonSummaryText.Text = "Sin selección."; ComparisonDetailsText.Text = "Seleccioná un ensayo para consultar el detalle."; ComparisonCompatibilityText.Text = "Seleccioná uno o dos ensayos."; return; }
        ComparisonCompatibilityText.Text = selected.Length == 2 ? ComparisonRules.Compatibility(selected[0], selected[1])
            : selected.Length > 2 ? "Selección múltiple: tendencias descriptivas; no se acredita equivalencia entre todos los ensayos."
            : "Ensayo individual; seleccioná su par para comparar.";
        var groups = selected.GroupBy(t => t.Mode).ToArray();
        ComparisonSummaryText.Text = string.Join("\n", groups.Select(group =>
        {
            var s = ComparisonSummary.From(group);
            return $"{group.Key} · n={s.Count} ensayos · {s.ValidFrames}/" +
                (s.ExpectedFrames > 0 ? s.ExpectedFrames.ToString() : "No disponible") + " mensajes válidos / esperados\n" +
                $"{s.DetectedFrames} cabeceras detectadas · {s.CrcRejectedFrames} CRC rechazados · {s.TruncatedFrames} truncadas\n" +
                $"Duración digital media {(s.MeanDurationSeconds.HasValue ? $"{s.MeanDurationSeconds:0.###} s" : "No disponible")} · " +
                $"rendimiento medio {(s.MeanEffectiveBitsPerSecond.HasValue ? $"{s.MeanEffectiveBitsPerSecond:0.###} bit/s" : "No disponible")} · " +
                $"BER agregada {(s.AggregateBerBefore.HasValue ? s.AggregateBerBefore.Value.ToString("0.#####") : "No disponible")}.";
        }));
        ComparisonDetailsText.Text = string.Join("\n", selected.SelectMany(t => t.Channels.Select(c =>
            $"{t.Id} · {c.Channel}: {c.State}, CRC {(c.CrcValid is true ? "válido" : c.CrcValid is false ? "rechazado" : "no comprobado")}, " +
            $"BER {(c.BerBefore.HasValue ? $"{c.BerBefore:0.#####} ({c.IncorrectBits}/{c.ComparedBits})" : "No disponible")}, " +
            $"Hamming síndromes {(c.HammingSyndromes?.ToString() ?? "No disponible")}."))) +
            "\nDuración digital = muestras emitidas / tasa solo para señal generada. El tiempo de procesamiento no mide latencia acústica. Latencia acústica: No disponible.";
        AddBarChart("Duración de transmisión · eje x: s", selected,
            t => t.TransmissionSeconds, "s");
        AddBarChart("Rendimiento útil agregado · eje x: bit/s", selected,
            t => t.EffectiveBitsPerSecond, "bit/s");
        AddBarChart("Rendimiento útil por canal/trama · eje x: bit/s", selected.SelectMany(t => t.Channels.Select(c =>
        {
            double? duration = t.TransmissionSeconds;
            if (t.Mode == ExperimentMode.Bfsk && t.Source == ExperimentSource.Generated)
                duration = c.TransmittedBits * BfskToneGenerator.DefaultBitDurationSeconds;
            double? rate = duration is > 0 ? (c.MatchesReference ? c.UsefulBits : 0) / duration.Value : null;
            return (Name: t.Id + " · " + c.Channel, Value: rate, Source: t.Source, Mode: t.Mode);
        })).ToArray(), "bit/s");
        AddBarChart("BER previa por canal · eje x: errores / bits comparados", selected.SelectMany(t => t.Channels.Select(c =>
            (Name: t.Id + " · " + c.Channel, Value: c.BerBefore, Source: t.Source, Mode: t.Mode))).ToArray(), "");
        AddBarChart("BER posterior a Hamming por canal · eje x: errores / bits comparados", selected.SelectMany(t => t.Channels.Select(c =>
            (Name: t.Id + " · " + c.Channel, Value: c.BerAfter, Source: t.Source, Mode: t.Mode))).ToArray(), "");
        AddBarChart("Mensajes válidos / esperados · eje x: tramas", selected,
            t => t.ValidFrames, "tramas", t => $"{t.ValidFrames}/{(t.ExpectedFrames > 0 ? t.ExpectedFrames.ToString() : "No disponible")}");
        AddBarChart("CRC rechazados · eje x: tramas", selected, t => t.CrcRejectedFrames, "tramas");
        AddBarChart("Tramas detectadas e incompletas · eje x: tramas", selected, t => t.TruncatedFrames, "tramas");
        foreach (var t in selected.Take(2)) AddSpectrum(t);
    }

    private void AddBarChart(string title, IReadOnlyList<ComparisonTrial> trials, Func<ComparisonTrial, double?> value,
        string unit, Func<ComparisonTrial, string>? label = null) => AddBarChart(title,
        trials.Select(t => (t.Id, value(t), t.Source, t.Mode)).ToArray(), unit, label is null ? null : trials.Select(label).ToArray());

    private void AddBarChart(string title, (string Name, double? Value, ExperimentSource Source, ExperimentMode Mode)[] values,
        string unit, string[]? labels = null)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 14) };
        panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) });
        double observedMax = values.Where(v => v.Value.HasValue).Select(v => v.Value!.Value).DefaultIfEmpty(0).Max();
        double max = observedMax > 0 ? observedMax : 1;
        panel.Children.Add(new TextBlock { Text = observedMax > 0 ? $"Escala de barras: 0–{max:0.#####} {unit} (máximo de esta serie)" : "Sin magnitudes positivas disponibles en esta serie.",
            Foreground = (Brush)FindResource("MutedBrush"), FontSize = 12, Margin = new Thickness(0, 0, 0, 3) });
        for (int i = 0; i < values.Length; i++)
        {
            var v = values[i];
            var row = new Grid { Margin = new Thickness(0, 3, 0, 6) };
            row.SetBinding(FrameworkElement.MaxWidthProperty, new Binding("ActualWidth") { Source = ComparisonResultsScroll });
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = $"{v.Name} · {v.Source}: " +
                (v.Value.HasValue ? labels?[i] ?? $"{v.Value:0.###} {unit}" : "No disponible"), FontSize = 12,
                TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = v.Name });
            if (v.Value.HasValue)
            {
                var bar = new ProgressBar { Minimum = 0, Maximum = max, Value = v.Value.Value, Height = 12,
                    Foreground = v.Mode == ExperimentMode.Bfsk ? Brushes.DeepSkyBlue : Brushes.MediumPurple,
                    ToolTip = $"{v.Value:0.#####} {unit}" };
                Grid.SetRow(bar, 1);
                row.Children.Add(bar);
            }
            panel.Children.Add(row);
        }
        ComparisonCharts.Children.Add(panel);
    }

    private void AddSpectrum(ComparisonTrial trial)
    {
        if (trial.Spectrum.Count == 0)
        { ComparisonSpectrumCharts.Children.Add(new TextBlock { Text = trial.Id + ": No disponible; señal demasiado breve o sin energía." }); return; }
        var panel = new StackPanel { Margin = new Thickness(0, 3, 0, 12) };
        panel.Children.Add(new TextBlock { Text = $"{trial.Id} · {trial.Source} · {trial.SampleRate} Hz · Hann 4096 muestras · resolución {trial.SampleRate / 4096.0:0.#} Hz",
            FontSize = 12, TextWrapping = TextWrapping.Wrap });
        var canvas = new Canvas { Width = 530, Height = 165, Background = new SolidColorBrush(Color.FromRgb(16, 27, 43)) };
        var path = new Polyline { Stroke = trial.Mode == ExperimentMode.Bfsk ? Brushes.DeepSkyBlue : Brushes.MediumPurple,
            StrokeThickness = 1.5 };
        double maxHz = trial.Spectrum[^1].Hertz;
        foreach (var point in trial.Spectrum)
            path.Points.Add(new Point(32 + 490 * point.Hertz / maxHz, 8 + 125 * Math.Clamp(-point.RelativeDb / 80, 0, 1)));
        canvas.Children.Add(path);
        var axis = new Line { X1 = 32, Y1 = 8, X2 = 32, Y2 = 135, Stroke = Brushes.Gray };
        canvas.Children.Add(axis);
        canvas.Children.Add(new Line { X1 = 32, Y1 = 135, X2 = 522, Y2 = 135, Stroke = Brushes.Gray });
        foreach (int db in new[] { 0, -40, -80 })
        {
            double y = 8 + 125 * -db / 80.0;
            var tick = new TextBlock { Text = db.ToString(CultureInfo.InvariantCulture), FontSize = 10,
                Foreground = Brushes.LightGray };
            Canvas.SetLeft(tick, 1); Canvas.SetTop(tick, y - 7); canvas.Children.Add(tick);
        }
        foreach (int hz in new[] { 0, 1200, 1900, 3000, 3700, 4200 })
        {
            if (hz > maxHz + trial.SampleRate / 4096.0) continue;
            double x = 32 + 490 * hz / maxHz;
            canvas.Children.Add(new Line { X1 = x, Y1 = 135, X2 = x, Y2 = 139, Stroke = Brushes.Gray });
            var tick = new TextBlock { Text = hz.ToString(CultureInfo.InvariantCulture), FontSize = 10,
                Foreground = Brushes.LightGray };
            Canvas.SetLeft(tick, Math.Clamp(x - 13, 0, 505)); Canvas.SetTop(tick, 141); canvas.Children.Add(tick);
        }
        panel.Children.Add(new Viewbox { Child = canvas, Stretch = Stretch.Uniform, MaxWidth = 700,
            HorizontalAlignment = HorizontalAlignment.Stretch });
        ComparisonSpectrumCharts.Children.Add(panel);
    }

    private async void ComparisonSave_Click(object sender, RoutedEventArgs e)
    {
        if (comparisonHistory.Count == 0) { ComparisonStatusText.Text = "No hay ensayos para guardar."; return; }
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Historial JSON (*.json)|*.json", FileName = "acousticlink-ensayos.json" };
        if (dialog.ShowDialog(this) != true) return;
        try { var snapshot = comparisonHistory.ToArray(); await Task.Run(() => ComparisonHistory.Save(dialog.FileName, snapshot));
            ComparisonStatusText.Text = "Historial guardado: " + dialog.FileName; }
        catch (Exception ex) { ComparisonStatusText.Text = "No se pudo guardar: " + ex.Message; }
    }

    private async void ComparisonLoad_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Historial JSON (*.json)|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var loaded = await Task.Run(() => ComparisonHistory.Load(dialog.FileName));
            comparisonHistory.AddRange(loaded); RefreshComparisonHistory();
            ComparisonStatusText.Text = $"Se cargaron {loaded.Count} ensayos. Historial total: {comparisonHistory.Count}.";
        }
        catch (Exception ex) { ComparisonStatusText.Text = "No se pudo abrir: " + ex.Message; }
    }

    private async void ComparisonCsv_Click(object sender, RoutedEventArgs e)
    {
        if (comparisonHistory.Count == 0) { ComparisonStatusText.Text = "No hay ensayos para exportar."; return; }
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Resultados CSV (*.csv)|*.csv", FileName = "acousticlink-comparacion.csv" };
        if (dialog.ShowDialog(this) != true) return;
        try { var snapshot = comparisonHistory.ToArray(); await Task.Run(() => ComparisonHistory.SaveCsv(dialog.FileName, snapshot));
            ComparisonStatusText.Text = "CSV guardado: " + dialog.FileName; }
        catch (Exception ex) { ComparisonStatusText.Text = "No se pudo exportar: " + ex.Message; }
    }
}
