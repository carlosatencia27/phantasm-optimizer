using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using PC_Optimizer.Models;
using PC_Optimizer.Services;

namespace PC_Optimizer.Core;

public class TuningEngine : ITuningEngine
{
    private readonly IRestorePointService _restorePointService;
    private readonly IActivityLogger _activityLogger;
    private readonly IHistoryStore? _historyStore;
    private readonly ISystemNotifier _systemNotifier;
    private readonly PC_Optimizer.Services.Licensing.ILicenseGate _licenseGate;
    private readonly List<ISetting> _settings = new();
    private readonly List<BackupEntry> _backups = new();
    private readonly List<ChangeLogEntry> _changeLog = new();
    private readonly object _lock = new();

    public TuningEngine(IRestorePointService restorePointService, IActivityLogger? activityLogger = null,
        IHistoryStore? historyStore = null, ISystemNotifier? systemNotifier = null,
        PC_Optimizer.Services.Licensing.ILicenseGate? licenseGate = null)
    {
        _licenseGate = licenseGate ?? PC_Optimizer.Services.Licensing.AllowAllGate.Instance;
        _restorePointService = restorePointService;
        _activityLogger = activityLogger ?? new NullActivityLogger();
        _historyStore = historyStore;
        _systemNotifier = systemNotifier ?? new NullSystemNotifier();
        LoadPersistedState();
    }

    public IReadOnlyList<ISetting> Settings => _settings.AsReadOnly();

    public ISetting? GetSetting(string key) => _settings.FirstOrDefault(s => s.Key == key);

    public void RegisterSetting(ISetting setting)
    {
        _settings.Add(setting);
        RestorePersistedBackup(setting);
    }

    public PC_Optimizer.Services.Licensing.ILicenseGate License => _licenseGate;

    /// <summary>Aplica un valor nuevo. Los cambios reales exigen licencia válida; la simulación no.</summary>
    public SettingResult ApplySetting(string key, object? newValue, bool dryRun = false) => ApplyCore(key, newValue, dryRun, requiresLicense: true);

    /// <summary>Escribe el valor predeterminado de Windows al revertir un ajuste sin copia original. Es una reversión: nunca exige licencia.</summary>
    public SettingResult RestoreDefaultValue(string key, object? defaultValue, bool dryRun = false)
    {
        // Sin licencia solo puede escribirse el valor predeterminado que declara el propio ajuste, y solo
        // cuando no hay copia original: esta vía no sirve para aplicar valores arbitrarios.
        var setting = GetSetting(key);
        if (setting == null || setting.HasOriginal || setting.RevertValue == null || !Equals(setting.RevertValue, defaultValue))
            return Complete(key, setting?.Name ?? key, "ApplyFailed", setting?.OriginalValue, defaultValue, dryRun, false,
                "No hay un valor predeterminado seguro que restaurar para este ajuste.");
        return ApplyCore(key, defaultValue, dryRun, requiresLicense: false);
    }

    private SettingResult ApplyCore(string key, object? newValue, bool dryRun, bool requiresLicense)
    {
        lock (_lock)
        {
            var setting = GetSetting(key);
            if (setting == null)
                return Complete(key, key, "ApplyFailed", null, newValue, dryRun, false, "Ajuste no encontrado.");

            if (!dryRun && requiresLicense && !_licenseGate.CanModifySystem)
                return Complete(setting.Key, setting.Name, "ApplyFailed", setting.OriginalValue, newValue, false, false, _licenseGate.BlockedMessage);

            if (setting.RiskLevel == SettingRiskLevel.Advanced && !IsAdvancedAllowed())
                return Complete(setting.Key, setting.Name, "ApplyFailed", setting.OriginalValue, newValue, dryRun, false, "Ajuste avanzado requiere confirmación.");

            if (!setting.IsWindowsVersionSupported)
                return Complete(setting.Key, setting.Name, "ApplyFailed", setting.OriginalValue, newValue, dryRun, false, "Este ajuste no es compatible con tu versión de Windows.");

            if (!dryRun)
            {
                bool needsBackup = !setting.HasOriginal;
                // Punto de restauración antes de aplicar cambios reales
                _restorePointService.CreateRestorePoint($"Phantasm Optimizer: aplicar {setting.Name}", out string rpMsg);

                try
                {
                    setting.Apply(newValue, dryRun: false);
                }
                catch (Exception ex)
                {
                    TryUndoAfterFailedApply(setting);
                    var failureMessage = $"Error al aplicar: {PC_Optimizer.Services.UserErrorText.Of(ex)}";
                    if (setting is DnsSetting dnsDiagnostic) failureMessage += " " + dnsDiagnostic.GetOperationDetails(newValue, false);
                    return Complete(setting.Key, setting.Name, "ApplyFailed", setting.OriginalValue, newValue, false, false, failureMessage);
                }

                if (!setting.HasOriginal)
                    return Complete(setting.Key, setting.Name, "ApplyFailed", setting.OriginalValue, newValue, false, false, "El ajuste no confirmó que se aplicó.");

                if (setting is IApplyVerification verification && !verification.VerifyApplied(newValue, out var verificationError))
                {
                    TryUndoAfterFailedApply(setting);
                    return Complete(setting.Key, setting.Name, "ApplyFailed", setting.OriginalValue, newValue, false, false, verificationError);
                }

                // El registro quedó escrito; ahora se pide a Windows que lo aplique con su
                // mecanismo estándar (en segundo plano y con tiempo máximo).
                NotifySystemChange(setting);

                if (needsBackup)
                {
                    var original = setting.OriginalValue;
                    _backups.Add(new BackupEntry
                    {
                        SettingKey = setting.Key,
                        SettingName = setting.Name,
                        OriginalValue = original?.ToString() ?? string.Empty,
                        CurrentValue = newValue?.ToString() ?? string.Empty,
                        // Se guarda el valor original con su tipo para poder revertir tras reiniciar.
                        OriginalValueType = original?.GetType().AssemblyQualifiedName,
                        OriginalValueJson = original == null ? null : JsonSerializer.Serialize(original, original.GetType()),
                        OriginalValueExisted = original != null
                    });
                }
            }

            var operationMessage = dryRun ? "Simulación completada. No se aplicaron cambios." : "Ajuste aplicado correctamente.";
            if (setting is DnsSetting dns)
            {
                operationMessage += " " + dns.GetOperationDetails(newValue, dryRun);
                if (!string.IsNullOrWhiteSpace(dns.CacheFlushWarning))
                    operationMessage += " " + dns.CacheFlushWarning + ".";
            }
            return Complete(setting.Key, setting.Name, dryRun ? "DryRun" : "Apply", setting.OriginalValue, newValue,
                dryRun, true, operationMessage);
        }
    }

    public SettingResult RevertSetting(string key, bool dryRun = false)
    {
        lock (_lock)
        {
            var setting = GetSetting(key);
            if (setting == null)
                return Complete(key, key, "RevertFailed", null, null, dryRun, false, "Ajuste no encontrado.");

            if (!setting.HasOriginal)
                return Complete(setting.Key, setting.Name, "RevertFailed", null, null, dryRun, false, "No hay valor original guardado para revertir.");

            if (!dryRun)
            {
                try
                {
                    setting.Revert(dryRun: false);
                }
                catch (Exception ex)
                {
                    return Complete(setting.Key, setting.Name, "RevertFailed", setting.ReadCurrentState(), setting.OriginalValue, false, false, $"Error al revertir: {PC_Optimizer.Services.UserErrorText.Of(ex)}");
                }

                // Al revertir también se avisa a Windows para que vuelva a leer el valor.
                NotifySystemChange(setting);
            }

            var backup = _backups.FirstOrDefault(b => b.SettingKey == key);
            if (backup != null)
            {
                backup.IsReverted = true;
                backup.RevertedAt = DateTime.Now;
            }

            var revertMessage = dryRun ? "Simulación de reversión completada." : "Ajuste revertido correctamente.";
            if (!dryRun && setting is DnsSetting revertedDns && !string.IsNullOrWhiteSpace(revertedDns.CacheFlushWarning))
                revertMessage += " " + revertedDns.CacheFlushWarning + ".";

            return Complete(setting.Key, setting.Name, dryRun ? "DryRun" : "Revert", setting.ReadCurrentState(), backup?.OriginalValue,
                dryRun, true, revertMessage);
        }
    }

    public SettingResult RevertAll(bool dryRun = false)
    {
        var lastResult = new SettingResult { Success = true, Message = dryRun ? "Simulación de reversión total completada." : "Todos los ajustes revertidos.", DryRun = dryRun };
        foreach (var setting in _settings.Where(s => s.HasOriginal).ToList())
        {
            var result = RevertSetting(setting.Key, dryRun);
            if (!result.Success) lastResult = result;
        }
        return lastResult;
    }

    public IReadOnlyList<ChangeLogEntry> GetChangeLog() => _changeLog.AsReadOnly();

    public void RecordNonReversibleAction(string key, string name, bool dryRun, string message)
    {
        lock (_lock)
        {
            _changeLog.Add(new ChangeLogEntry
            {
                SettingKey = key,
                SettingName = name,
                Action = dryRun ? "DryRun" : "Cleanup",
                DryRun = dryRun,
                Notes = $"Acción no reversible. {message}"
            });
            _activityLogger.Write($"{key}: {(dryRun ? "simulación" : "acción no reversible")}. {message}");
            PersistState();
        }
    }

    public void RecordBlockedAction(string key, string name, bool dryRun, string message)
    {
        lock (_lock)
            Complete(key, name, "Blocked", null, null, dryRun, false, message);
    }

    public IReadOnlyList<BackupEntry> GetBackups() => _backups.AsReadOnly();

    private static bool IsAdvancedAllowed() => false; // Los avanzados requieren confirmación explícita en UI

    private SettingResult Complete(string key, string name, string action, object? oldValue, object? newValue,
        bool dryRun, bool success, string message)
    {
        _changeLog.Add(new ChangeLogEntry
        {
            SettingKey = key,
            SettingName = name,
            Action = action,
            OldValue = oldValue?.ToString() ?? string.Empty,
            NewValue = newValue?.ToString() ?? string.Empty,
            DryRun = dryRun,
            Notes = message
        });
        _activityLogger.Write($"{action} {key}; simulación={dryRun}; éxito={success}; {message}");
        PersistState();
        return new SettingResult { Success = success, Message = message, DryRun = dryRun };
    }

    // ── Persistencia del historial y los respaldos (Principios 2 y 7) ─────────
    /// <summary>
    /// Recupera de disco el historial y las copias del valor original. Los ajustes se
    /// registran después, así que cada uno recibe su copia en RegisterSetting.
    /// </summary>
    private void LoadPersistedState()
    {
        if (_historyStore == null) return;
        try
        {
            _backups.AddRange(_historyStore.LoadBackups().Where(b => !b.IsReverted));
            _changeLog.AddRange(_historyStore.LoadChangeLog());
        }
        catch
        {
            // El almacén ya tolera archivos ausentes o dañados; esta red evita que un
            // fallo inesperado impida arrancar la app.
        }
    }

    /// <summary>Repone en el ajuste la copia del valor original guardada en disco (si la hay).</summary>
    private void RestorePersistedBackup(ISetting setting)
    {
        if (_historyStore == null || setting.HasOriginal || !setting.IsReversible) return;

        var backup = _backups.LastOrDefault(b => b.SettingKey == setting.Key && !b.IsReverted);
        if (backup == null) return;

        try
        {
            setting.RestoreBackup(DeserializeOriginal(backup), backup.OriginalValueExisted);
        }
        catch
        {
            // Si el valor guardado no se puede reconstruir, el ajuste se queda sin copia original.
        }
    }

    private static object? DeserializeOriginal(BackupEntry backup)
    {
        if (string.IsNullOrWhiteSpace(backup.OriginalValueJson) ||
            string.IsNullOrWhiteSpace(backup.OriginalValueType))
            return null;

        var type = Type.GetType(backup.OriginalValueType, throwOnError: false);
        return type == null ? null : JsonSerializer.Deserialize(backup.OriginalValueJson, type);
    }

    /// <summary>Escribe historial y respaldos en disco. Nunca interrumpe la operación en curso.</summary>
    private void PersistState()
    {
        try
        {
            _historyStore?.Save(_backups, _changeLog);
        }
        catch
        {
            // No poder guardar el historial no debe romper una operación ya realizada.
        }
    }

    /// <summary>
    /// Pide a Windows que aplique el cambio con su mecanismo estándar. Solo los ajustes cuya
    /// propagación depende de una notificación la usan; el resto se aplica en su momento
    /// documentado (reinicio, próximo arranque, siguiente juego...).
    /// </summary>
    private void NotifySystemChange(ISetting setting)
    {
        try
        {
            switch (setting)
            {
                case MenuShowDelaySetting menu:
                    _systemNotifier.NotifyMenuShowDelay(menu.CurrentDelayMilliseconds);
                    break;
                case TransparencySetting:
                    _systemNotifier.NotifyTransparencyChanged();
                    break;
            }
        }
        catch
        {
            // Nunca se convierte en un fallo del ajuste: el valor ya está escrito y verificado.
        }
    }

    private static void TryUndoAfterFailedApply(ISetting setting)
    {
        if (!setting.HasOriginal) return;
        try { setting.Revert(dryRun: false); }
        catch { /* el resultado principal conserva la razón de fallo de la operación */ }
    }
}
