using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using PC_Optimizer.Services;

namespace PC_Optimizer.Views;

public partial class PanelView : BasePage
{
    private readonly IUrlLauncher _urlLauncher = new UrlLauncher();

    public PanelView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// WPF no abre solo la URL de un Hyperlink: hay que atender RequestNavigate. Se valida la
    /// dirección, se abre con el navegador del usuario y, si algo falla, se avisa con la URL.
    /// </summary>
    private void OnSourceLinkRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        e.Handled = true;

        var url = e.Uri?.OriginalString;
        if (!_urlLauncher.TryOpen(url, out var errorMessage))
        {
            System.Windows.MessageBox.Show(
                $"No se pudo abrir el enlace:\n{url}\n\n{errorMessage}",
                "Phantasm Optimizer",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
    }
}
