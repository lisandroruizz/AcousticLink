using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace AcousticLink.App.Visualization;

public sealed class TransmissionWalkthroughView : UserControl
{
    private WalkthroughData? data;
    private int stepIndex, page;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private readonly TextBlock source = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray };
    private readonly TextBlock title = new() { FontSize = 20, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock explanation = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray, Margin = new Thickness(0, 4, 0, 8) };
    private readonly TextBlock detail = new() { Text = "Seleccioná una fila para inspeccionar su relación con la etapa.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
    private readonly TextBlock count = new();
    private readonly ListBox stages = new() { MinWidth = 170 };
    private readonly ListBox items = new() { FontFamily = new FontFamily("Consolas"), FontSize = 13 };
    private readonly WaveformView waveform = new() { Height = 180 };
    private readonly StackPanel graph = new() { Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock waveformLabel = new() { Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap };
    private readonly Button previous = new() { Content = "← Anterior" }, next = new() { Content = "Siguiente →" },
        restart = new() { Content = "Reiniciar" }, play = new() { Content = "Reproducir etapas" },
        beforePage = new() { Content = "◀" }, afterPage = new() { Content = "▶" };

    public TransmissionWalkthroughView()
    {
        var root = new Grid { Margin = new Thickness(10) };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new());
        root.Children.Add(source);
        var controls = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) };
        foreach (var button in new[] { previous, next, restart, play }) { button.Margin = new Thickness(0, 0, 6, 0); button.Padding = new Thickness(9, 5, 9, 5); controls.Children.Add(button); }
        Grid.SetRow(controls, 1); root.Children.Add(controls);
        var content = new Grid(); content.ColumnDefinitions.Add(new() { Width = new GridLength(190) }); content.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        stages.SelectionChanged += (_, _) => { if (stages.SelectedIndex >= 0) { stepIndex = stages.SelectedIndex; page = 0; Render(); } };
        content.Children.Add(stages);
        var right = new Grid { Margin = new Thickness(12, 0, 0, 0) };
        right.RowDefinitions.Add(new() { Height = GridLength.Auto }); right.RowDefinitions.Add(new() { Height = GridLength.Auto });
        right.RowDefinitions.Add(new()); right.RowDefinitions.Add(new() { Height = GridLength.Auto });
        right.RowDefinitions.Add(new() { Height = GridLength.Auto });
        right.Children.Add(title); Grid.SetRow(explanation, 1); right.Children.Add(explanation);
        var list = new Grid(); list.RowDefinitions.Add(new()); list.RowDefinitions.Add(new() { Height = GridLength.Auto });
        items.SelectionChanged += (_, _) => { if (items.SelectedItem is WalkthroughItem item) { detail.Text = $"{item.Label}\n{item.Value}\n{item.Detail}"; waveform.SetSelection(item.StartSeconds, item.EndSeconds); } };
        list.Children.Add(items);
        var pagination = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 5, 0, 5) };
        pagination.Children.Add(beforePage); count.Margin = new Thickness(8, 4, 8, 0); pagination.Children.Add(count); pagination.Children.Add(afterPage);
        Grid.SetRow(pagination, 1); list.Children.Add(pagination); Grid.SetRow(list, 2); right.Children.Add(list);
        var detailBorder = new Border { Background = new SolidColorBrush(Color.FromRgb(28, 46, 67)),
            Child = new ScrollViewer { Content = detail, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            CornerRadius = new CornerRadius(5), MinHeight = 65, MaxHeight = 85 };
        Grid.SetRow(detailBorder, 3); right.Children.Add(detailBorder);
        graph.Children.Add(waveformLabel); graph.Children.Add(waveform);
        Grid.SetRow(graph, 4); right.Children.Add(graph);
        Grid.SetColumn(right, 1); content.Children.Add(right); Grid.SetRow(content, 2); root.Children.Add(content);
        Content = root;
        var row = new FrameworkElementFactory(typeof(StackPanel));
        var label = new FrameworkElementFactory(typeof(TextBlock)); label.SetBinding(TextBlock.TextProperty, new Binding(nameof(WalkthroughItem.Label)));
        label.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold); row.AppendChild(label);
        var value = new FrameworkElementFactory(typeof(TextBlock)); value.SetBinding(TextBlock.TextProperty, new Binding(nameof(WalkthroughItem.Value)));
        value.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); value.SetValue(TextBlock.ForegroundProperty, Brushes.LightGray);
        value.SetBinding(TextBlock.ToolTipProperty, new Binding(nameof(WalkthroughItem.Value))); row.AppendChild(value);
        items.ItemTemplate = new DataTemplate { VisualTree = row };
        previous.Click += (_, _) => Move(-1); next.Click += (_, _) => Move(1);
        restart.Click += (_, _) => { Stop(); Select(0); }; play.Click += (_, _) => { if (timer.IsEnabled) Stop(); else { timer.Start(); play.Content = "Pausar etapas"; } };
        beforePage.Click += (_, _) => { page--; Render(); }; afterPage.Click += (_, _) => { page++; Render(); };
        timer.Tick += (_, _) => { if (data is null || stepIndex >= data.Steps.Count - 1) Stop(); else Move(1); };
        Unloaded += (_, _) => Stop();
        Clear("Elegí un mensaje o un resultado de recepción para comenzar.");
    }

    public void Show(WalkthroughData value)
    {
        Stop(); data = value; stepIndex = page = 0;
        stages.ItemsSource = value.Steps.Select((s, i) => $"{i + 1:00}  {s.Title}").ToArray();
        waveform.SetData(value.Waveform, value.Duration); waveform.SetSelection(null, null); waveformLabel.Text = value.WaveformLabel;
        source.Text = $"{value.Source} · {value.Mode} · recorrido de presentación (1,5 s por etapa; no equivale al tiempo de señal)";
        Select(0);
    }

    public void Clear(string reason)
    {
        Stop(); data = null; stages.ItemsSource = null; items.ItemsSource = null;
        source.Text = reason; title.Text = "Sin recorrido disponible"; explanation.Text = "Los datos faltantes no se reconstruyen a partir de una referencia.";
        detail.Text = ""; waveform.SetData([], 0); waveform.SetSelection(null, null); waveformLabel.Text = ""; count.Text = "";
        UpdateButtons();
    }

    private void Move(int delta) => Select(Math.Clamp(stepIndex + delta, 0, data?.Steps.Count - 1 ?? 0));
    private void Select(int index) { stepIndex = index; stages.SelectedIndex = index; page = 0; Render(); }
    private void Stop() { timer.Stop(); play.Content = "Reproducir etapas"; }
    private void Render()
    {
        if (data is null) return;
        var step = data.Steps[stepIndex]; title.Text = $"{stepIndex + 1:00} / {data.Steps.Count:00} · {step.Title}";
        explanation.Text = step.Explanation;
        graph.Visibility = (data.Mode == "BFSK" || data.Mode == "FDM") &&
            (step.Title is "Audio disponible" or "Símbolos estimados" or "Símbolos y frecuencias" or "Señal digital")
            ? Visibility.Visible : Visibility.Collapsed;
        int pages = Math.Max(1, (step.Items.Count + 15) / 16); page = Math.Clamp(page, 0, pages - 1);
        waveform.SetSelection(null, null);
        items.ItemsSource = step.Items.Skip(page * 16).Take(16).ToArray();
        detail.Text = step.Items.Count == 0 ? "No hay datos de esta etapa en el resultado seleccionado." : "Seleccioná una fila para inspeccionar valor, límites y procedencia.";
        count.Text = $"{page + 1}/{pages} · {step.Items.Count} elementos";
        beforePage.IsEnabled = page > 0; afterPage.IsEnabled = page < pages - 1;
        UpdateButtons();
    }
    private void UpdateButtons()
    {
        previous.IsEnabled = data is not null && stepIndex > 0; next.IsEnabled = data is not null && stepIndex < data.Steps.Count - 1;
        restart.IsEnabled = play.IsEnabled = data is not null;
    }
}
