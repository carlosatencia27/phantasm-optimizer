using PC_Optimizer.Models;
using PC_Optimizer.Services;

namespace PC_Optimizer.Core;

/// <summary>
/// Acción de Limpieza para la caché de Optimización de entrega. La vista previa es solo lectura y
/// el borrado lo hace el cmdlet oficial <c>Delete-DeliveryOptimizationCache -Force</c>.
/// </summary>
public sealed class DeliveryOptimizationCleanupSetting : ICleanupAction
{
    public const string KeyValue = "Cleanup.DeliveryOptimization";
    public const string DocumentationLink =
        "https://learn.microsoft.com/en-us/powershell/module/deliveryoptimization/delete-deliveryoptimizationcache";

    private readonly IDeliveryOptimizationService _service;
    private DeliveryOptimizationStatus? _lastStatus;

    public DeliveryOptimizationCleanupSetting(IDeliveryOptimizationService service) => _service = service;

    public string Key => KeyValue;
    public string Name => "Vaciar caché de Optimización de entrega";
    public string Description =>
        "Elimina los archivos que Windows guardó para compartir y descargar actualizaciones más rápido. "
        + "Windows vuelve a crearla cuando la necesita. No se puede deshacer.";
    public string RealisticEffect =>
        "Puede liberar espacio en disco. No acelera el PC ni las descargas: vuelve a descargar lo que necesite.";
    public SettingRiskLevel RiskLevel => SettingRiskLevel.Safe;
    public bool IsWindowsVersionSupported => true;
    public bool IsReversible => false;
    public bool RequiresConfirmation => true;
    public bool RequiresRestart => false;
    public bool RequiresAdmin => true;

    /// <summary>El cmdlet oficial está documentado por Microsoft en Learn (ver enlace).</summary>
    public string VerificationStatus => "Documentado por Microsoft";
    public string? DocumentationUrl => DocumentationLink;

    public string ActionLabel => "Vaciar caché";
    public string TargetDescription => "la caché de Optimización de entrega de Windows";
    public bool HideWhenEmpty => true;

    public object? OriginalValue => null;
    public bool HasOriginal => false;
    public object? ApplyValue => null;
    public object? RevertValue => null;

    public void RestoreBackup(object? originalValue, bool originalExisted) { }

    public object? ReadCurrentState() => _lastStatus?.Bytes;

    public CleanupAvailability Probe(bool isAdmin)
    {
        DeliveryOptimizationStatus status;
        try { status = _service.GetStatus(isAdmin); }
        catch (System.Exception ex)
        {
            return new CleanupAvailability(0, 0,
                $"No se pudo leer la caché de Optimización de entrega: {PC_Optimizer.Services.UserErrorText.Of(ex)}", CanClean: false, ShowElevate: false);
        }

        _lastStatus = status;

        // El cmdlet es obligatorio: si no existe, se explica y no se ofrece la acción.
        bool canClean = status.CmdletAvailable && isAdmin;
        bool showElevate = status.CmdletAvailable && !isAdmin;
        return new CleanupAvailability(status.FileCount, status.Bytes, status.ProblemText, canClean, showElevate);
    }

    public CleanupRunResult CleanAndVerify(bool isAdmin)
    {
        var outcome = _service.Clean(isAdmin);
        var after = Probe(isAdmin);

        var note = $"Limpieza no reversible de la caché de Optimización de entrega. "
            + $"Archivos antes: {outcome.FileCountBefore}; bytes liberados medidos: {outcome.BytesFreedMeasured}; "
            + $"archivos que quedan: {after.FileCount}.";
        return new CleanupRunResult(outcome.Succeeded, outcome.Message, note, after);
    }

    public void Apply(object? newValue, bool dryRun)
    {
        // No se ejecuta como ISetting: la limpieza se invoca como acción explícita desde CleanupViewModel.
    }

    public void Revert(bool dryRun)
    {
        // No se puede deshacer: el historial lo marca como no reversible.
    }
}
