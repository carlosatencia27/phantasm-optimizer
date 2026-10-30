using PC_Optimizer.Models;
using PC_Optimizer.Services;

namespace PC_Optimizer.Core;

public class GameBarSetting : ISetting
{
    private const string ValueName = "AppCaptureEnabled";
    private readonly IGameRegistryService _registry;

    public GameBarSetting(IGameRegistryService registry)
    {
        _registry = registry;
        Key = "Game.GameBar";
        Name = "Desactivar la captura y transmisión con Game Bar";
        Description = "Activado: desactiva la captura y transmisión de Game Bar (AppCaptureEnabled = 0). Al revertir, restaura el valor original. No es la directiva AllowGameDVR.";
        RealisticEffect = "Desactivarla puede liberar mínimos recursos en segundo plano. El impacto en FPS es generalmente imperceptible y no está garantizado.";
        RiskLevel = SettingRiskLevel.Safe;
        IsWindowsVersionSupported = IsWindows10OrLater();
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
    public string VerificationStatus => "Observado en Windows, no documentado oficialmente";
    public string? DocumentationUrl => null;

    public object? OriginalValue { get; private set; }
    public bool HasOriginal { get; private set; }

    /// <summary>Activado = captura de Game Bar desactivada (AppCaptureEnabled = 0).</summary>
    public object? ApplyValue => true;
    /// <summary>Sin copia original, desactivar el ajuste restaura la captura activada (predeterminado de Windows).</summary>
    public object? RevertValue => false;

    /// <inheritdoc />
    public void RestoreBackup(object? originalValue, bool originalExisted)
    {
        if (HasOriginal) return;
        OriginalValue = originalValue;
        HasOriginal = true;
    }

    public object? ReadCurrentState()
    {
        // El interruptor representa el ajuste aplicado: la captura de Game Bar está desactivada.
        var val = _registry.GetValue(ValueName);
        return val is int i && i == 0;
    }

    /// <summary>Estado con lenguaje simple, sin nombres de claves técnicas.</summary>
    public string GetFriendlyState()
    {
        var val = _registry.GetValue(ValueName);
        if (val is int i)
            return i == 0 ? "Desactivada (valor 0)" : "Activada (valor 1)";
        return "Predeterminado de Windows (activada)";
    }

    public void Apply(object? newValue, bool dryRun)
    {
        if (!dryRun)
        {
            OriginalValue = _registry.GetValue(ValueName);
            // Activado = desactivar la captura: AppCaptureEnabled = 0. Al desactivar el ajuste se escribe 1.
            bool disableCapture = newValue is bool b ? b : true;
            int enabled = disableCapture ? 0 : 1;
            if (!_registry.SetValue(ValueName, enabled)) throw new InvalidOperationException("No se pudo cambiar Game Bar.");
            HasOriginal = true;
        }
    }

    public void Revert(bool dryRun)
    {
        if (!dryRun && HasOriginal)
        {
            if (OriginalValue == null ? !_registry.DeleteValue(ValueName) : !_registry.SetValue(ValueName, OriginalValue))
                throw new InvalidOperationException("No se pudo restaurar Game Bar.");
            OriginalValue = null;
            HasOriginal = false;
        }
    }

    private static bool IsWindows10OrLater()
    {
        try
        {
            var version = Environment.OSVersion.Version;
            return version.Major > 10 || (version.Major == 10 && version.Minor >= 0);
        }
        catch { return false; }
    }
}
