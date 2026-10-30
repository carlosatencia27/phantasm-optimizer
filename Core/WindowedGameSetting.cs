using PC_Optimizer.Models;
using PC_Optimizer.Services;

namespace PC_Optimizer.Core;

public class WindowedGameSetting : ISetting
{
    private const string ValueName = "DirectXUserGlobalSettings";
    private const string SwapEffectUpgradeEnableKey = "SwapEffectUpgradeEnable";
    private readonly IGameRegistryService _registry;

    public WindowedGameSetting(IGameRegistryService registry)
    {
        _registry = registry;
        Key = "Game.WindowedOptimization";
        Name = "Activar las optimizaciones para juegos en ventana";
        Description = "Activado: aplica las optimizaciones de Windows 11 para juegos DirectX 10 y DirectX 11 en ventana o sin bordes (modelo de presentación flip). Edita solo el elemento SwapEffectUpgradeEnable de DirectXUserGlobalSettings y conserva el resto. Requiere reiniciar el juego para aplicar.";
        RealisticEffect = "Cambia cómo Windows presenta los juegos DirectX 10 y 11 en ventana o sin bordes. El efecto depende del juego y del equipo, la app no lo puede medir y puede no notarse. No garantiza más FPS y no afecta a juegos en pantalla completa exclusiva.";
        RiskLevel = SettingRiskLevel.Safe;
        IsWindowsVersionSupported = IsWindows11Build22557OrLater();
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

    /// <summary>Activado = SwapEffectUpgradeEnable = 1.</summary>
    public object? ApplyValue => true;
    /// <summary>Sin copia original, desactivar el ajuste escribe SwapEffectUpgradeEnable = 0.</summary>
    public object? RevertValue => false;

    /// <inheritdoc />
    public void RestoreBackup(object? originalValue, bool originalExisted)
    {
        if (HasOriginal) return;
        OriginalValue = originalValue;
        HasOriginal = true;
    }

    // Valor original del elemento SwapEffectUpgradeEnable ("0", "1" o null si no existía).
    private string? OriginalSwapEffect { get; set; }

    public object? ReadCurrentState()
    {
        var val = _registry.GetValue(ValueName) as string;
        return GetSwapEffectValue(val);
    }

    /// <summary>El estado leído es un texto («0»/«1»): está aplicado cuando el valor es «1».</summary>
    public bool IsAppliedInSystem
    {
        get
        {
            try { return string.Equals(ReadCurrentState() as string, "1", StringComparison.Ordinal); }
            catch { return false; }
        }
    }

    private static string? GetSwapEffectValue(string? settings)
    {
        if (string.IsNullOrEmpty(settings)) return null;
        foreach (var part in settings.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && string.Equals(kv[0].Trim(), SwapEffectUpgradeEnableKey, StringComparison.OrdinalIgnoreCase))
                return kv[1].Trim();
        }
        return null;
    }

    private static string SetSwapEffectValue(string? settings, string? newValue)
    {
        var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(settings))
        {
            foreach (var part in settings.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split('=', 2);
                if (kv.Length == 2)
                    entries[kv[0].Trim()] = kv[1].Trim();
            }
        }

        if (newValue == null)
            entries.Remove(SwapEffectUpgradeEnableKey);
        else
            entries[SwapEffectUpgradeEnableKey] = newValue;

        return string.Join(";", entries.Select(kv => $"{kv.Key}={kv.Value}")) + ";";
    }

    public void Apply(object? newValue, bool dryRun)
    {
        if (!dryRun)
        {
            var current = _registry.GetValue(ValueName) as string;
            OriginalValue = current;
            OriginalSwapEffect = GetSwapEffectValue(current);

            bool enable = newValue is bool b && b;
            if (!_registry.SetValue(ValueName, SetSwapEffectValue(current, enable ? "1" : "0"))) throw new InvalidOperationException("No se pudo cambiar las optimizaciones de ventana.");
            HasOriginal = true;
        }
    }

    public void Revert(bool dryRun)
    {
        if (!dryRun && HasOriginal)
        {
            var current = _registry.GetValue(ValueName) as string;
            var updated = SetSwapEffectValue(current, OriginalSwapEffect);
            if (string.IsNullOrEmpty(updated.TrimEnd(';')))
            {
                if (!_registry.DeleteValue(ValueName))
                    throw new InvalidOperationException("No se pudo restaurar las optimizaciones de ventana.");
            }
            else
            {
                if (!_registry.SetValue(ValueName, updated))
                    throw new InvalidOperationException("No se pudo restaurar las optimizaciones de ventana.");
            }
            OriginalValue = null;
            HasOriginal = false;
            OriginalSwapEffect = null;
        }
    }

    private static bool IsWindows11Build22557OrLater()
    {
        try
        {
            return Environment.OSVersion.Version.Build >= 22557;
        }
        catch { return false; }
    }
}
