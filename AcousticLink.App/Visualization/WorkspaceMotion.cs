using System.Windows;
using System.Windows.Media.Animation;

namespace AcousticLink.App.Visualization;

// Transiciones de presentación; no representan tiempos ni progreso del módem.
internal static class WorkspaceMotion
{
    public static void Reveal(UIElement target)
    {
        target.BeginAnimation(UIElement.OpacityProperty, null);
        if (!SystemParameters.ClientAreaAnimation) return;
        target.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0.45, 1, TimeSpan.FromMilliseconds(150)) { FillBehavior = FillBehavior.Stop });
    }
}
