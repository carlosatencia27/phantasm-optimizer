using System.Collections.Generic;

namespace PC_Optimizer.Core;

using PC_Optimizer.Models;

public interface ITuningEngine
{
    IReadOnlyList<ISetting> Settings { get; }
    ISetting? GetSetting(string key);
    void RegisterSetting(ISetting setting);
    SettingResult ApplySetting(string key, object? newValue, bool dryRun = false);
    /// <summary>Reversión al valor predeterminado cuando no hay copia original; no exige licencia.</summary>
    SettingResult RestoreDefaultValue(string key, object? defaultValue, bool dryRun = false);
    /// <summary>Puerta de licencia: bloquea solo cambios nuevos, nunca la reversión.</summary>
    PC_Optimizer.Services.Licensing.ILicenseGate License { get; }
    SettingResult RevertSetting(string key, bool dryRun = false);
    SettingResult RevertAll(bool dryRun = false);
    IReadOnlyList<ChangeLogEntry> GetChangeLog();
    void RecordNonReversibleAction(string key, string name, bool dryRun, string message);
    void RecordBlockedAction(string key, string name, bool dryRun, string message);
    IReadOnlyList<BackupEntry> GetBackups();
}

/// <summary>Permite que un ajuste valide su estado después de una escritura real.</summary>
public interface IApplyVerification
{
    bool VerifyApplied(object? requestedValue, out string failureReason);
}

public class SettingResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool DryRun { get; set; }
    public string? RestorePointCreated { get; set; }
}
