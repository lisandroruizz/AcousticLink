using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Wpf.Ui.Controls;

namespace AcousticLink.App;

public partial class MainWindow
{
    private void DashboardLayout_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateDashboardLayout();

    private void UpdateDashboardLayout()
    {
        if (DashboardTransmitGrid is null || DashboardSecondaryGrid is null || DashboardReceptionGrid is null ||
            TransmitChannelBPanel is null || ReceptionChannelBPanel is null) return;
        ArrangeDashboardPair(DashboardTransmitGrid, DashboardTransmitACard, DashboardTransmitGrid.Children.OfType<Border>().Last(),
            DashboardTransmitGrid.ActualWidth < 640, TransmitChannelBPanel.Visibility == Visibility.Collapsed);
        ArrangeDashboardPair(DashboardSecondaryGrid, DashboardConfiguration, DashboardSignalCard,
            DashboardSecondaryGrid.ActualWidth < 640, false);
        ArrangeDashboardPair(DashboardReceptionGrid, DashboardReceptionACard, DashboardReceptionBCard,
            DashboardReceptionGrid.ActualWidth < 390, ReceptionChannelBPanel.Visibility == Visibility.Collapsed);
    }

    private static void ArrangeDashboardPair(Grid grid, Border first, Border second, bool stacked, bool single)
    {
        grid.ColumnDefinitions[1].Width = new GridLength(stacked || single ? 0 : 16);
        Grid.SetColumnSpan(first, stacked || single ? 3 : 1);
        Grid.SetColumn(second, stacked ? 0 : 2);
        Grid.SetColumnSpan(second, stacked ? 3 : 1);
        Grid.SetRow(second, stacked ? 1 : 0);
    }

    private void TechnicalLayout_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not Grid grid) return;
        bool stacked = e.NewSize.Width < 660;
        grid.ColumnDefinitions[1].Width = new GridLength(stacked ? 0 : 12);
        var second = (Border)grid.Children[1];
        var action = (Border)grid.Children[2];
        Grid.SetColumnSpan((Border)grid.Children[0], stacked ? 3 : 1);
        Grid.SetColumn(second, stacked ? 0 : 2);
        Grid.SetColumnSpan(second, stacked ? 3 : 1);
        Grid.SetRow(second, stacked ? 1 : 0);
        Grid.SetRow(action, stacked ? 2 : 1);
    }

    private double receiverInspectorWidth = 280, analysisSettingsWidth = 240;
    private bool receiverNarrow;
    private bool receiverAutoHidden;
    private bool receiverAutoSizing;
    private void ReceiverWorkspace_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool narrow = e.NewSize.Width < 900;
        if (IsInitialized && narrow && !receiverNarrow)
        {
            receiverAutoHidden = ReceiverInspectorToggle.IsChecked == true;
            if (receiverAutoHidden)
            {
                receiverAutoSizing = true;
                ReceiverInspectorToggle.IsChecked = false;
                WorkspacePanel_Click(ReceiverInspectorToggle, new RoutedEventArgs());
                receiverAutoSizing = false;
            }
        }
        else if (IsInitialized && !narrow && receiverNarrow && receiverAutoHidden)
        {
            receiverAutoSizing = true;
            ReceiverInspectorToggle.IsChecked = true;
            WorkspacePanel_Click(ReceiverInspectorToggle, new RoutedEventArgs());
            receiverAutoSizing = false;
            receiverAutoHidden = false;
        }
        receiverNarrow = narrow;
    }
    private void WorkspacePanel_Click(object sender, RoutedEventArgs e)
    {
        bool reception = ReferenceEquals(sender, ReceiverInspectorToggle);
        if (reception && !receiverAutoSizing) receiverAutoHidden = false;
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
