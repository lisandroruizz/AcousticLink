using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Wpf.Ui.Controls;

namespace AcousticLink.App;

public partial class MainWindow
{
    private double receiverInspectorWidth = 280, analysisSettingsWidth = 240;
    private bool receiverNarrow;
    private void ReceiverWorkspace_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool narrow = e.NewSize.Width < 900;
        if (IsInitialized && narrow && !receiverNarrow)
        {
            ReceiverInspectorToggle.IsChecked = false;
            WorkspacePanel_Click(ReceiverInspectorToggle, new RoutedEventArgs());
        }
        receiverNarrow = narrow;
    }
    private void WorkspacePanel_Click(object sender, RoutedEventArgs e)
    {
        bool reception = ReferenceEquals(sender, ReceiverInspectorToggle);
        bool open = reception ? ReceiverInspectorToggle.IsChecked == true : AnalysisSettingsToggle.IsChecked == true;
        var column = reception ? ReceiverInspectorColumn : AnalysisSettingsColumn;
        var divider = reception ? ReceiverDividerColumn : AnalysisDividerColumn;
        var panel = reception ? ReceiverInspector : AnalysisSettings;
        var splitter = reception ? ReceiverInspectorSplitter : AnalysisSettingsSplitter;
        column.MinWidth = open ? reception ? 240 : 220 : 0;
        column.MaxWidth = open ? reception ? 480 : 380 : double.PositiveInfinity;
        if (!open && column.ActualWidth > 0)
        {
            if (reception) receiverInspectorWidth = column.ActualWidth; else analysisSettingsWidth = column.ActualWidth;
        }
        column.Width = new GridLength(open ? reception ? receiverInspectorWidth : analysisSettingsWidth : 0);
        divider.Width = new GridLength(open ? 6 : 0);
        panel.Visibility = splitter.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        if (open) AcousticLink.App.Visualization.WorkspaceMotion.Reveal(panel);
    }

    private void InternalTool_Changed(object sender, SelectionChangedEventArgs e)
    {
        // Los SelectionChanged de selectores internos también son eventos enrutados.
        if (!ReferenceEquals(e.OriginalSource, sender) || !IsInitialized) return;
        if (sender is TabControl { SelectedContent: UIElement content })
            AcousticLink.App.Visualization.WorkspaceMotion.Reveal(content);
    }
    private void InitializeWorkspace()
    {
        UpdateWorkspaceSection(false);
        // WPF UI mantiene los botones de ventana y el arrastre de la barra de título.
        // La navegación comienza debajo de la barra de título; no debe reservarla otra vez.
    }

    private void Navigation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is NavigationViewItem item && int.TryParse(item.Tag?.ToString(), out int index))
            MainSectionsTabs.SelectedIndex = index;
    }

    private void WorkspaceSection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized || !ReferenceEquals(e.OriginalSource, MainSectionsTabs)) return;
        UpdateWorkspaceSection(true);
    }

    private void UpdateWorkspaceSection(bool animate)
    {
        ActiveSectionText.Text = ((TabItem)MainSectionsTabs.SelectedItem).Header.ToString();
        bool motion = animate && SystemParameters.ClientAreaAnimation;
        foreach (var item in WorkspaceNavigation.MenuItems.OfType<NavigationViewItem>())
        {
            bool active = item.Tag?.ToString() == MainSectionsTabs.SelectedIndex.ToString();
            item.IsActive = active;
            var color = active ? Color.FromRgb(36, 60, 91) : Colors.Transparent;
            Color previous = (item.Background as SolidColorBrush)?.Color ?? Colors.Transparent;
            var brush = new SolidColorBrush(color); item.Background = brush;
            item.BeginAnimation(OpacityProperty, null);
            if (motion && active) item.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0.6, 1, TimeSpan.FromMilliseconds(160)) { FillBehavior = FillBehavior.Stop });
            if (motion) brush.BeginAnimation(SolidColorBrush.ColorProperty,
                new ColorAnimation(previous, color, TimeSpan.FromMilliseconds(160)) { FillBehavior = FillBehavior.Stop });
        }
        MainSectionsTabs.BeginAnimation(OpacityProperty, null);
        var shift = (TranslateTransform)MainSectionsTabs.RenderTransform;
        shift.BeginAnimation(TranslateTransform.XProperty, null);
        if (!motion) return;
        var duration = TimeSpan.FromMilliseconds(180);
        MainSectionsTabs.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0.4, 1, duration) { FillBehavior = FillBehavior.Stop });
        shift.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(10, 0, duration) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
    }
}
