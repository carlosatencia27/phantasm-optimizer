using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace PC_Optimizer.Views;

/// <summary>
/// Muestra la vista que corresponde a un ViewModel (según los DataTemplate de App.xaml) y la
/// REUTILIZA en las visitas siguientes. Con un ContentControl normal WPF vuelve a construir la vista
/// completa (Panel tiene decenas de filas) cada vez que se cambia de sección; aquí se construye una
/// sola vez y después solo se vuelve a mostrar. No cambia lo que se ve ni los datos.
/// </summary>
public class CachedViewHost : ContentControl
{
    private readonly Dictionary<object, FrameworkElement> _views = new();

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(object), typeof(CachedViewHost),
        new PropertyMetadata(null, (d, e) => ((CachedViewHost)d).ShowFor(e.NewValue)));

    public object? ViewModel
    {
        get => GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    /// <summary>
    /// Construye la vista de un ViewModel por adelantado (sin mostrarla) para que la primera visita
    /// no pague el coste de crearla. Se llama en tiempo inactivo tras el arranque.
    /// </summary>
    public void Prewarm(object viewModel)
    {
        if (viewModel == null || _views.ContainsKey(viewModel)) return;
        if (TryFindResource(new DataTemplateKey(viewModel.GetType())) is DataTemplate template &&
            template.LoadContent() is FrameworkElement created)
        {
            created.DataContext = viewModel;
            // Medir fuerza a WPF a aplicar las plantillas y generar las filas ahora, no en la primera visita.
            created.Measure(new System.Windows.Size(900, double.PositiveInfinity));
            _views[viewModel] = created;
        }
    }

    private void ShowFor(object? viewModel)
    {
        if (viewModel == null) { Content = null; return; }

        if (!_views.TryGetValue(viewModel, out var view))
        {
            var template = TryFindResource(new DataTemplateKey(viewModel.GetType())) as DataTemplate;
            if (template?.LoadContent() is not FrameworkElement created)
            {
                Content = viewModel; // sin plantilla: comportamiento normal de ContentControl
                return;
            }
            created.DataContext = viewModel;
            _views[viewModel] = view = created;
        }
        Content = view;
    }
}
