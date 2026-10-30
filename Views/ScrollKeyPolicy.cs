using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace PC_Optimizer.Views;

/// <summary>
/// Decide si las teclas de desplazamiento de la ventana (RePág, AvPág, flechas, Inicio, Fin) deben
/// dejarse al control que tiene el foco. Se separa de MainWindow para poder probarlo sin ventana.
/// </summary>
public static class ScrollKeyPolicy
{
    /// <summary>
    /// true si el foco está en un cuadro de texto, contraseña o lista desplegable, o dentro de una
    /// lista/tabla/árbol/menú con selección: allí las flechas mueven la selección o el cursor, no la página.
    /// </summary>
    public static bool ControlOwnsKeys(object? focused)
    {
        if (focused is System.Windows.Controls.Primitives.TextBoxBase or PasswordBox or System.Windows.Controls.ComboBox) return true;
        for (DependencyObject? d = focused as DependencyObject; d != null;
             d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
        {
            if (d is Selector or System.Windows.Controls.TreeView or Menu) return true;
        }
        return false;
    }
}
