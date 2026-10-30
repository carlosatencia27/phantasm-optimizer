using PC_Optimizer.Models;
using PC_Optimizer.Services;

namespace PC_Optimizer.Core;

public class MenuShowDelaySetting : ISetting
{
    public const string ValueName = "MenuShowDelay";
    public const string OptimizedValue = "0";
    public const string DefaultValue = "400";

    private readonly IGameRegistryService _registryService;

    public MenuShowDelaySetting(IGameRegistryService registryService)
    {
        _registryService = registryService;
        Key = "System.MenuShowDelay";
        Name = "Eliminar el retardo de los menús (MenuShowDelay)";
        Description = "Activado: elimina el retardo de 400 ms al desplegar menús y submenús (0 ms) y aplica el cambio con SystemParametersInfo. Al revertir, restaura el valor original.";
        RealisticEffect = "Elimina la pausa de 400 ms al abrir submenús. Es un ajuste puramente estético y de respuesta visual de la interfaz; no aumenta los FPS ni el rendimiento de juegos ni aplicaciones. Puede requerir reiniciar el Explorador de Windows o cerrar sesión para surtir efecto visual completo.";
        RiskLevel = SettingRiskLevel.Safe;
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
    public bool RequiresAdmin => false;
    public string VerificationStatus => "Documentado por Microsoft";
    public string? DocumentationUrl => "https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfow";

    public object? OriginalValue { get; private set; }
    public bool HasOriginal { get; private set; }

    /// <summary>Activado = retardo de menús a 0 ms.</summary>
    public object? ApplyValue => true;
    /// <summary>Sin copia original, desactivar el ajuste devuelve el retardo de Windows (400 ms).</summary>
    public object? RevertValue => false;
    private bool _originalValueExisted;

    /// <summary>Retardo actual en milisegundos, leído del sistema (400 si no hay valor).</summary>
    public int CurrentDelayMilliseconds
    {
        get
        {
            var val = _registryService.GetValue(ValueName);
            return val != null && int.TryParse(val.ToString(), out var ms) && ms >= 0 ? ms : 400;
        }
    }

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
        if (val is string str)
            return str == OptimizedValue || str == "0";
        return false;
    }

    /// <summary>Estado con lenguaje simple, sin nombres de claves técnicas.</summary>
    public string GetFriendlyState()
    {
        var val = _registryService.GetValue(ValueName);
        if (val is string str)
        {
            if (str == OptimizedValue || str == "0") return "0 ms (sin retardo)";
            return $"{str} ms (valor de Windows)";
        }
        return "400 ms (valor de Windows)";
    }

    public void Apply(object? newValue, bool dryRun)
    {
        if (!dryRun)
        {
            OriginalValue = _registryService.GetValue(ValueName);
            _originalValueExisted = OriginalValue != null;
            bool enable = newValue is bool b ? b : true;
            if (!_registryService.SetValue(ValueName, enable ? OptimizedValue : DefaultValue)) throw new InvalidOperationException("No se pudo cambiar el retardo de menús.");
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
            if (!restored) throw new InvalidOperationException("No se pudo restaurar el retardo de menús.");
            OriginalValue = null;
            HasOriginal = false;
            _originalValueExisted = false;
        }
    }
}
