using System;
using System.Diagnostics;

namespace PC_Optimizer.Services;

/// <summary>
/// Abre enlaces de documentación. Se separa del visor para poder probar el validador sin
/// abrir el navegador (las pruebas nunca tocan el sistema real).
/// </summary>
public interface IUrlLauncher
{
    /// <summary>Valida y abre la URL. Devuelve false y un motivo si no se pudo abrir.</summary>
    bool TryOpen(string? url, out string? errorMessage);
}

/// <summary>
/// Reglas de las direcciones que la app puede abrir: solo HTTPS y solo dominios de
/// documentación oficial usados hoy en las fuentes de los ajustes.
/// </summary>
public static class SourceUrlPolicy
{
    /// <summary>Dominios permitidos (sin subdominios, comparación exacta).</summary>
    public static readonly string[] AllowedHosts = { "learn.microsoft.com" };

    public static bool IsAllowed(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return false;
        if (string.IsNullOrWhiteSpace(uri.Host)) return false;

        foreach (var host in AllowedHosts)
            if (string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }
}

/// <summary>Abre la URL con el navegador predeterminado del usuario.</summary>
public class UrlLauncher : IUrlLauncher
{
    public bool TryOpen(string? url, out string? errorMessage)
    {
        errorMessage = null;
        if (!SourceUrlPolicy.IsAllowed(url))
        {
            errorMessage = "La dirección no es una fuente oficial permitida (https de learn.microsoft.com).";
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url!) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = PC_Optimizer.Services.UserErrorText.Of(ex);
            return false;
        }
    }
}
