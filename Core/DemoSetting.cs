using System;
using PC_Optimizer.Models;

namespace PC_Optimizer.Core;

/// <summary>
/// Ajuste de ejemplo que usa el registro simulado. Solo para demostrar el motor en Etapa 1.
/// No modifica el registro real de Windows.
/// </summary>
public class DemoSetting : ISetting
{
    private readonly RegistryMock _registry;
    private readonly string _registryKey;

    public DemoSetting(RegistryMock registry, string registryKey, string name, string description, string realisticEffect,
        SettingRiskLevel riskLevel, bool isWindowsVersionSupported = true)
    {
        _registry = registry;
        _registryKey = registryKey;
        Key = registryKey;
        Name = name;
        Description = description;
        RealisticEffect = realisticEffect;
        RiskLevel = riskLevel;
        IsWindowsVersionSupported = isWindowsVersionSupported;
    }

    public string Key { get; }
    public string Name { get; }
    public string Description { get; }
    public string RealisticEffect { get; }
    public SettingRiskLevel RiskLevel { get; }
    public bool IsWindowsVersionSupported { get; }
    public bool IsReversible => true;
    public bool RequiresConfirmation => false;
    public bool RequiresRestart => false;
    public bool RequiresAdmin => false;
    public string VerificationStatus => "No verificado";
    public string? DocumentationUrl => null;

    public object? OriginalValue { get; private set; }
    public bool HasOriginal { get; private set; }

    /// <summary>Ajuste de ejemplo: el valor lo elige la prueba.</summary>
    public object? ApplyValue => true;
    public object? RevertValue => false;

    /// <inheritdoc />
    public void RestoreBackup(object? originalValue, bool originalExisted)
    {
        if (HasOriginal) return;
        OriginalValue = originalValue;
        HasOriginal = true;
    }

    public object? ReadCurrentState() => _registry.GetValue(_registryKey);

    public void Apply(object? newValue, bool dryRun)
    {
        if (!dryRun)
        {
            OriginalValue = ReadCurrentState();
            _registry.SetValue(_registryKey, newValue);
            HasOriginal = true;
        }
    }

    public void Revert(bool dryRun)
    {
        if (!dryRun && HasOriginal)
        {
            _registry.SetValue(_registryKey, OriginalValue);
        }
    }
}
