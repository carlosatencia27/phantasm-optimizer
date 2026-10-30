using System.Windows;

namespace PC_Optimizer.Services;

/// <summary>
/// ¿Se permiten animaciones? Si Windows tiene desactivadas las animaciones
/// (SystemParameters.ClientAreaAnimation), todas las animaciones de la app se omiten.
/// </summary>
public static class Motion
{
    public static bool Enabled
    {
        get
        {
            try { return SystemParameters.ClientAreaAnimation; }
            catch { return true; }
        }
    }

    /// <summary>
    /// Aplica el ajuste de movimiento a los recursos de la app: con movimiento reducido, las duraciones
    /// de brillo y avisos pasan a 0 (los estados cambian al instante). Se llama una vez al arrancar.
    /// </summary>
    public static void ApplyTo(ResourceDictionary resources)
    {
        if (Enabled) return;
        var zero = new Duration(System.TimeSpan.Zero);
        resources["DurationGlow"] = zero;
        resources["DurationToast"] = zero;
        resources["DurationFast"] = zero;
        resources["DurationNormal"] = zero;
    }
}
