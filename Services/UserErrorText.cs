using System;

namespace PC_Optimizer.Services;

/// <summary>
/// Texto de error que se muestra al usuario: nunca el mensaje interno de la excepción (puede traer
/// rutas, nombres de clase o datos personales), solo una frase genérica y el nombre del tipo de error.
/// El detalle seguro (tipo y código HRESULT, sin mensaje) se guarda en el registro local.
/// </summary>
public static class UserErrorText
{
    /// <summary>Dónde se guarda el detalle. La aplicación lo apunta al registro local al arrancar.</summary>
    public static IActivityLogger Logger { get; set; } = new NullActivityLogger();

    public static string Of(Exception ex)
    {
        var type = ex?.GetType().Name ?? "Exception";
        try
        {
            Logger.Write($"Error interno: {ex?.GetType().FullName ?? type} · HRESULT 0x{(ex?.HResult ?? 0):X8}");
        }
        catch
        {
            // Registrar nunca debe provocar otro error.
        }

        return $"no se puede determinar la causa ({type})";
    }
}
