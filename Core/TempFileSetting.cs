using PC_Optimizer.Models;
using PC_Optimizer.Services;

namespace PC_Optimizer.Core;

public class TempFileSetting : ICleanupAction
{
    private readonly IDiskCleanupService _cleanupService;
    private readonly string _folderPath;

    public TempFileSetting(IDiskCleanupService cleanupService, string folderPath,
        string name, string description, string realisticEffect,
        SettingRiskLevel riskLevel, bool isWindowsVersionSupported = true, bool requiresAdmin = false,
        string? verificationStatus = null, string? documentationUrl = null,
        string actionLabel = "Limpiar ahora", string? targetDescription = null, bool hideWhenEmpty = false)
    {
        _cleanupService = cleanupService;
        _folderPath = folderPath;
        Key = $"Cleanup.Temp.{folderPath.GetHashCode():X8}";
        Name = name;
        Description = description;
        RealisticEffect = realisticEffect;
        RiskLevel = riskLevel;
        IsWindowsVersionSupported = isWindowsVersionSupported;
        RequiresAdminValue = requiresAdmin;
        VerificationStatus = verificationStatus ?? "No aplica (acción de limpieza de la app)";
        DocumentationUrl = documentationUrl;
        ActionLabel = actionLabel;
        TargetDescription = targetDescription ?? $"la carpeta {folderPath}";
        HideWhenEmpty = hideWhenEmpty;
    }

    public string Key { get; }
    public string Name { get; }
    public string Description { get; }
    public string RealisticEffect { get; }
    public SettingRiskLevel RiskLevel { get; }
    public bool IsWindowsVersionSupported { get; }
    public bool IsReversible => false; // Los archivos borrados no se pueden recuperar
    public bool RequiresConfirmation => true; // Requiere confirmación explícita antes de borrar
    public bool RequiresRestart => false;
    private readonly bool RequiresAdminValue;
    public bool RequiresAdmin => RequiresAdminValue;
    public string VerificationStatus { get; }
    public string? DocumentationUrl { get; }

    public string ActionLabel { get; }
    public string TargetDescription { get; }
    public bool HideWhenEmpty { get; }

    public object? OriginalValue { get; private set; }
    public bool HasOriginal { get; private set; }

    /// <summary>Acción de limpieza no reversible: no se envía ningún valor para aplicar ni revertir.</summary>
    public object? ApplyValue => null;
    public object? RevertValue => null;

    /// <summary>Acción de limpieza no reversible: no guarda copia del valor original.</summary>
    public void RestoreBackup(object? originalValue, bool originalExisted) { }

    public object? ReadCurrentState() => _cleanupService.GetTempFolderSize(_folderPath);

    /// <summary>
    /// Devuelve la cantidad de archivos y el espacio en bytes que se liberarían al limpiar.
    /// </summary>
    public (int FileCount, long BytesToFree) GetPreview()
    {
        var result = _cleanupService.GetTempFolderPreview(_folderPath);
        return (result.FilesDeleted, result.BytesFreed);
    }

    public CleanResult Clean(bool dryRun) => dryRun ? new CleanResult() : _cleanupService.CleanTempFolder(_folderPath);

    public CleanupAvailability Probe(bool isAdmin)
    {
        var (fileCount, bytes) = GetPreview();
        return new CleanupAvailability(fileCount, bytes, null, CanClean: true, ShowElevate: false);
    }

    public CleanupRunResult CleanAndVerify(bool isAdmin)
    {
        var result = Clean(dryRun: false);
        var after = Probe(isAdmin);

        var note = $"Limpieza no reversible. Borrados: {result.FilesDeleted}; omitidos o inaccesibles: {result.SkippedFiles}; "
            + $"bytes liberados: {result.BytesFreed}; archivos que quedan: {after.FileCount}.";
        var message = $"Limpieza no reversible completada: {result.FilesDeleted} archivo(s), {ByteSize.Format(result.BytesFreed)}. "
            + $"{result.SkippedFiles} omitido(s) por estar en uso o sin acceso. "
            + $"Verificación posterior: quedan {after.FileCount} archivo(s) ({ByteSize.Format(after.BytesToFree)}).";
        return new CleanupRunResult(true, message, note, after);
    }

    public void Apply(object? newValue, bool dryRun)
    {
        // No se ejecuta como ISetting: la limpieza se invoca como acción explícita desde CleanupViewModel.
    }

    public void Revert(bool dryRun)
    {
        // La limpieza de archivos no se registra en TuningEngine y no admite reversión.
    }
}
