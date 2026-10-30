using System.Windows.Navigation;
using PC_Optimizer.Services;

namespace PC_Optimizer.Views;

public partial class CleanupView : BasePage
{
    private readonly IUrlLauncher _urlLauncher = new UrlLauncher();

    public CleanupView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// WPF no abre solo la URL de un Hyperlink: se valida la dirección y se abre con el navegador
    /// del usuario. Si algo falla, se avisa con la URL. Solo se muestra cuando hay fuente.
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
