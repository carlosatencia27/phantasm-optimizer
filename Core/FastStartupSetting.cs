using PC_Optimizer.Models;
using PC_Optimizer.Services;

namespace PC_Optimizer.Core;

public class FastStartupSetting : ISetting
{
    public const string ValueName = "HiberbootEnabled";
    // 1 = Inicio rápido habilitado (default de Windows), 0 = Inicio rápido deshabilitado (apagado completo)
    public const int EnabledValue = 1;
    public const int DisabledValue = 0;

    private readonly IGameRegistryService _registryService;

    public FastStartupSetting(IGameRegistryService registryService)
    {
        _registryService = registryService;
        Key = "System.FastStartup";
        Name = "Desactivar el inicio rápido de Windows (Fast Startup)";
        Description = "Activado: desactiva el inicio rápido, así que Windows hace un apagado completo y el siguiente encendido es más lento pero más limpio (sin la imagen parcial del kernel). Al revertir, vuelve a activarlo.";
        RealisticEffect = "Desactivar el inicio rápido no aumenta los FPS ni el rendimiento del sistema. Ayuda a solucionar fallos con controladores que quedan bloqueados al apagar, discos en arranque dual o dispositivos que no responden tras reiniciar. El tiempo de encendido desde apagado completo puede ser ligeramente más lento.";
        RiskLevel = SettingRiskLevel.Moderate;
        IsWindowsVersionSupported = true;
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
    public bool RequiresAdmin => true;
    public string VerificationStatus => "Observado en Windows, no documentado oficialmente";
    // La página de "actualizaciones que no se instalan con el inicio rápido" habla de actualizaciones,
    // no documenta HiberbootEnabled: por eso no se enlaza ninguna fuente.
    public string? DocumentationUrl => null;

    public object? OriginalValue { get; private set; }
    public bool HasOriginal { get; private set; }
    private bool _originalValueExisted;

    /// <summary>Activado = inicio rápido desactivado (HiberbootEnabled = 0).</summary>
    public object? ApplyValue => true;
    /// <summary>Sin copia original, desactivar el ajuste reactiva el inicio rápido (HiberbootEnabled = 1).</summary>
    public object? RevertValue => false;

    /// <inheritdoc />
    public void RestoreBackup(object? originalValue, bool originalExisted)
    {
        if (HasOriginal) return;
        OriginalValue = originalValue;
        _originalValueExisted = originalExisted;
        HasOriginal = true;
    }

    public object? ReadCurrentState()
    {
        var val = _registryService.GetValue(ValueName);
        if (val is int i)
            return i == DisabledValue; // true si está desactivado el inicio rápido (modo apagado completo)
        return false;
    }

    /// <summary>Estado con lenguaje simple, sin nombres de claves técnicas.</summary>
    public string GetFriendlyState()
    {
        var val = _registryService.GetValue(ValueName);
        if (val is int i)
            return i == DisabledValue
                ? "Desactivado (apagado completo)"
                : "Activado (Windows guarda parte del kernel al apagar)";
        return "Predeterminado de Windows (activado)";
    }

    public void Apply(object? newValue, bool dryRun)
    {
        if (!dryRun)
        {
            OriginalValue = _registryService.GetValue(ValueName);
            _originalValueExisted = OriginalValue != null;

            // Si newValue es true, se desactiva el inicio rápido (HiberbootEnabled = 0)
            bool disableFastStartup = newValue is bool b ? b : true;
            if (!_registryService.SetValue(ValueName, disableFastStartup ? DisabledValue : EnabledValue)) throw new InvalidOperationException("No se pudo cambiar el Inicio rápido.");
            HasOriginal = true;
        }
    }

    public void Revert(bool dryRun)
    {
        if (!dryRun && HasOriginal)
        {
            bool restored = _originalValueExisted
                ? _registryService.SetValue(ValueName, OriginalValue)
                : _registryService.DeleteValue(ValueName);
            if (!restored) throw new InvalidOperationException("No se pudo restaurar el Inicio rápido.");
            OriginalValue = null;
            HasOriginal = false;
            _originalValueExisted = false;
        }
    }
}
