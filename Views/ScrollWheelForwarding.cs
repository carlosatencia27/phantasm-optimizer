using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PC_Optimizer.Views;

/// <summary>
/// La rueda (y el touchpad) deben desplazar la página con el cursor en cualquier lugar. Un
/// ScrollViewer interno (de una lista, un cuadro de texto, un ComboBox...) se queda el evento aunque
/// no tenga nada que desplazar. Este manejador de clase, registrado una sola vez para TODOS los
/// ScrollViewer, comprueba si el interno puede moverse en esa dirección; si no, desplaza el
/// ScrollViewer ancestro más cercano que sí pueda.
/// </summary>
public static class ScrollWheelForwarding
{
    private static bool _registered;

    public static void Register()
    {
        if (_registered) return;
        _registered = true;
        EventManager.RegisterClassHandler(typeof(ScrollViewer), UIElement.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(OnPreviewMouseWheel));
    }

    /// <summary>¿Puede este ScrollViewer moverse en vertical en el sentido de la rueda?</summary>
    internal static bool CanScroll(ScrollViewer sv, int delta) =>
        sv.ScrollableHeight > 0 &&
        (delta > 0 ? sv.VerticalOffset > 0 : sv.VerticalOffset < sv.ScrollableHeight);

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || e.Delta == 0 || sender is not ScrollViewer inner) return;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return; // Ctrl+rueda: zoom de otros controles
        if (CanScroll(inner, e.Delta)) return; // el interno se encarga

        for (DependencyObject? d = Parent(inner); d != null; d = Parent(d))
        {
            if (d is ScrollViewer outer && CanScroll(outer, e.Delta))
            {
                // Misma distancia que la rueda estándar de WPF (líneas × 16); proporcional al delta,
                // así el touchpad de precisión se desplaza suave.
                double lines = SystemParameters.WheelScrollLines;
                if (lines <= 0) lines = 3;
                outer.ScrollToVerticalOffset(outer.VerticalOffset - e.Delta / 120.0 * lines * 16);
                e.Handled = true;
                return;
            }
        }
    }

    private static DependencyObject? Parent(DependencyObject d) =>
        d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
}
