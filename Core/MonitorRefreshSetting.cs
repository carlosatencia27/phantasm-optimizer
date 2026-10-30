using System.Globalization;
using PC_Optimizer.Models;
using PC_Optimizer.Services;

namespace PC_Optimizer.Core;

public class MonitorRefreshSetting : ISetting
{
    private readonly IMonitorService _monitorService;

    public MonitorRefreshSetting(IMonitorService monitorService)
    {
        _monitorService = monitorService;
        Key = "Monitor.RefreshRate";
        Name = "Frecuencia de refresco del monitor (solo lectura)";
        Description = "Revisa la frecuencia actual del monitor y la compara con la máxima que soporta. Solo lectura: no cambia nada por sí sola.";
        RealisticEffect = "Informativo solamente. Ayuda a identificar si el monitor está funcionando por debajo de su capacidad máxima.";
        RiskLevel = SettingRiskLevel.Safe;
        IsWindowsVersionSupported = true;
    }

    public string Key { get; }
    public string Name { get; }
    public string Description { get; }
    public string RealisticEffect { get; }
    public SettingRiskLevel RiskLevel { get; }
    public bool IsWindowsVersionSupported { get; }
    public bool IsReversible => true; // No cambia nada, siempre reversible
    public bool RequiresConfirmation => false;
    public bool RequiresRestart => false;
    public bool RequiresAdmin => false;
    public string VerificationStatus => "Documentado por Microsoft";
    public string? DocumentationUrl => "https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumdisplaysettingsw";

    public object? OriginalValue { get; private set; }
    public bool HasOriginal { get; private set; }

    /// <summary>Solo lectura informativa: la app nunca envía un valor para aplicar.</summary>
    public object? ApplyValue => null;
    public object? RevertValue => null;

    /// <summary>Solo lectura informativa: no aplica cambios, así que no hay nada que restaurar.</summary>
    public void RestoreBackup(object? originalValue, bool originalExisted) { }

    public object? ReadCurrentState()
    {
        var current = _monitorService.GetCurrentRefreshRate();
        var max = _monitorService.GetMaxRefreshRate();

        if (current == null)
            return "No disponible";

        if (max == null)
            return $"{current.Value.ToString("0.0", CultureInfo.InvariantCulture)} Hz (máxima no disponible)";

        return $"{current.Value.ToString("0.0", CultureInfo.InvariantCulture)} Hz (máx. {max.Value.ToString("0.0", CultureInfo.InvariantCulture)} Hz)";
    }

    public string? CurrentRefreshRateText => _monitorService.GetCurrentRefreshRate()?.ToString("0.0", CultureInfo.InvariantCulture) + " Hz";
    public string? MaxRefreshRateText
    {
        get
        {
            var max = _monitorService.GetMaxRefreshRate();
            return max == null ? "máxima no disponible" : max.Value.ToString("0.0", CultureInfo.InvariantCulture) + " Hz";
        }
    }
    public string StatusDescription => _monitorService.GetStatusDescription();

    public void Apply(object? newValue, bool dryRun)
    {
        // Solo lectura: no aplica cambios
    }

    public void Revert(bool dryRun)
    {
        // Solo lectura: no hay nada que revertir
        OriginalValue = null;
        HasOriginal = false;
    }
}
