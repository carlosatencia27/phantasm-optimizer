namespace PC_Optimizer.Core;

/// <summary>
/// Detecta los casos en los que el sistema ya no está en el valor que aplicó la app, por ejemplo
/// cuando el ajuste se deshizo a mano en Windows. Revertir en ese momento pisaría un cambio manual
/// del usuario, así que la app avisa y pide confirmación antes de restaurar el valor guardado.
/// </summary>
public static class ManualChangeCheck
{
    /// <summary>Aviso mostrado antes de restaurar el valor guardado.</summary>
    public const string WarningText = "El valor actual ya no es el que aplicó la app.";

    /// <summary>
    /// true solo si la app guardó una copia original y el valor leído del sistema ya no coincide con
    /// el valor de «aplicar» (es decir, el ajuste se cambió fuera de la app).
    /// </summary>
    public static bool WasChangedOutsideTheApp(ISetting? setting)
    {
        if (setting == null || !setting.HasOriginal) return false;
        try { return !setting.IsAppliedInSystem; }
        catch { return false; }
    }
}
