using PC_Optimizer.Models;
using PC_Optimizer.Services;

namespace PC_Optimizer.Core;

public class TransparencySetting : ISetting
{
    private const string ValueName = "EnableTransparency";
    private readonly IGameRegistryService _registryService;

    public TransparencySetting(IGameRegistryService registryService)
    {
        _registryService = registryService;
        Key = "System.Transparency";
        Name = "Desactivar efectos de transparencia";
        Description = "Activado: la barra de tareas, el menú Inicio y las ventanas dejan de ser translúcidas y se ven opacas. Alivia algo de GPU en equipos modestos; no garantiza más FPS.";
        RealisticEffect = "Desactivar las transparencias reduce la carga de composición gráfica en el Administrador de ventanas (DWM). Es un cambio visual; en tarjetas gráficas dedicadas modernas no produce una mejora medible de FPS. El beneficio en rendimiento solo se percibe en procesadores con gráficos integrados lentos o memoria de video compartida reducida.";
        RiskLevel = SettingRiskLevel.Safe;
        IsWindowsVersionSupported = true;
        VerificationStatus = "Observado en Windows, no documentado oficialmente";
        DocumentationUrl = null;
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
    public string VerificationStatus { get; }
    public string? DocumentationUrl { get; }

    public object? OriginalValue { get; private set; }
    public bool HasOriginal { get; private set; }

    /// <summary>Activado = efectos de transparencia desactivados (EnableTransparency = 0).</summary>
    public object? ApplyValue => true;
    /// <summary>Sin copia original, desactivar el ajuste vuelve a activar las transparencias (EnableTransparency = 1).</summary>
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
        var val = _registryService.GetValue(ValueName);
        return val is int i && i == 0;
    }

    public void Apply(object? newValue, bool dryRun)
    {
        if (!dryRun)
        {
            OriginalValue = _registryService.GetValue(ValueName);
            // Desactivar transparencias (EnableTransparency = 0)
            bool disable = newValue is bool b ? b : true;
            if (!_registryService.SetValue(ValueName, disable ? 0 : 1)) throw new InvalidOperationException("No se pudo cambiar las transparencias.");
            HasOriginal = true;
        }
    }

    public void Revert(bool dryRun)
    {
        if (!dryRun && HasOriginal)
        {
            if (OriginalValue == null ? !_registryService.DeleteValue(ValueName) : !_registryService.SetValue(ValueName, OriginalValue))
                throw new InvalidOperationException("No se pudo restaurar las transparencias.");
            OriginalValue = null;
            HasOriginal = false;
        }
    }
}
