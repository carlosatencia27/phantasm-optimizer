using PC_Optimizer.Services;

namespace PC_Optimizer.Core;

/// <summary>
/// Vista previa de una acción de limpieza. ProblemText != null → la acción no se puede ofrecer
/// ahora y el motivo se muestra al usuario en lenguaje claro.
/// </summary>
public sealed record CleanupAvailability(
    int FileCount,
    long BytesToFree,
    string? ProblemText,
    bool CanClean,
    bool ShowElevate)
{
    public bool HasProblem => !string.IsNullOrWhiteSpace(ProblemText);
}

/// <summary>Resultado de ejecutar una limpieza: mensaje para el usuario, nota para el historial y estado posterior.</summary>
public sealed record CleanupRunResult(
    bool Succeeded,
    string Message,
    string HistoryNote,
    CleanupAvailability After);

/// <summary>
/// Acción de limpieza no reversible. Se muestra primero qué se va a borrar, nunca se toca nada
/// fuera de su carpeta objetivo, respeta el modo simulación y se verifica después del borrado.
/// </summary>
public interface ICleanupAction : ISetting
{
    /// <summary>Vista previa (solo lectura) y si la acción se puede ofrecer.</summary>
    CleanupAvailability Probe(bool isAdmin);

    /// <summary>Ejecuta la limpieza real y vuelve a medir para verificar el espacio liberado.</summary>
    CleanupRunResult CleanAndVerify(bool isAdmin);

    /// <summary>Texto del botón: «Limpiar ahora», «Vaciar caché», «Borrar informes»…</summary>
    string ActionLabel { get; }

    /// <summary>Qué se borra exactamente, para la confirmación y el tooltip.</summary>
    string TargetDescription { get; }

    /// <summary>Oculta la acción cuando no hay nada que limpiar (y no hay un motivo que explicar).</summary>
    bool HideWhenEmpty { get; }
}
