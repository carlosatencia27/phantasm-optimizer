using System;
using System.Collections.Generic;
using System.Linq;
using PC_Optimizer.Models;
using PC_Optimizer.Services;

namespace PC_Optimizer.Core;

public class PowerPlanSetting : ISetting
{
    /// <summary>GUID del plan «Alto rendimiento» de Windows.</summary>
    public const string HighPerformanceGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    /// <summary>GUID del plan «Máximo rendimiento» (Ultimate Performance) de Windows.</summary>
    public const string UltimatePerformanceGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";

    private readonly IPowerPlanService _powerPlanService;
    private readonly IDeviceInfoService? _device;
    private IReadOnlyList<PowerPlan>? _plansCache;

    public PowerPlanSetting(IPowerPlanService powerPlanService, IDeviceInfoService? deviceInfo = null)
    {
        _powerPlanService = powerPlanService;
        _device = deviceInfo;
        Key = "Power.ActivePlan";
        Name = "Cambiar el plan de energía a alto rendimiento";
        RiskLevel = SettingRiskLevel.Moderate;
        IsWindowsVersionSupported = true;
    }

    public string Key { get; }
    public string Name { get; }
    public string Description => IsAvailable
        ? $"Cambia el plan activo al plan «{TargetPlanName}», que ya existe en este equipo, y guarda el plan actual para poder revertirlo. ⚠ Portátiles y batería: un plan de máximo rendimiento aumenta el consumo, la temperatura y el ruido del ventilador, y reduce la autonomía. Úsalo solo conectado a la corriente."
        : $"{UnavailableMessage} No se cambiará nada.";
    /// <summary>Con batería (sin corriente) se pide confirmación antes de pasar a un plan de máximo rendimiento.</summary>
    public string? PreApplyWarning
    {
        get
        {
            DeviceInfo? info = null;
            try { info = _device?.Get(); } catch { }
            if (info is not { HasBattery: true, OnAcPower: false }) return null;
            var level = info.BatteryPercent.HasValue ? $" ({info.BatteryPercent} %)" : string.Empty;
            return $"Tu equipo está funcionando con batería{level}. Un plan de máximo rendimiento gasta más batería, calienta más y hace más ruido, " +
                   "y la autonomía baja. Conviene aplicarlo solo con el cargador conectado.";
        }
    }

    public string RealisticEffect => "Un plan de máximo rendimiento reduce el ahorro de energía de la CPU y del disco, a cambio de más consumo, temperatura y ruido. Puede no cambiar los FPS: el efecto depende del equipo y de la carga, y la app no lo puede medir. La app no crea, importa ni modifica planes de energía.";
    public SettingRiskLevel RiskLevel { get; }
    public bool IsWindowsVersionSupported { get; }
    public bool IsReversible => true;
    public bool RequiresConfirmation => false;
    public bool RequiresRestart => false;
    public bool RequiresAdmin => false;
    public string VerificationStatus => "Documentado por Microsoft";
    public string? DocumentationUrl => "https://learn.microsoft.com/en-us/windows/win32/api/powersetting/nf-powersetting-powersetactivescheme";

    public IPowerPlanService PowerPlanService => _powerPlanService;

    public object? OriginalValue { get; private set; }
    public bool HasOriginal { get; private set; }

    /// <summary>Activado = cambiar al plan de máximo rendimiento disponible (solo los dos GUID admitidos).</summary>
    public object? ApplyValue => TargetPlanGuid;

    /// <summary>Sin copia original, el valor de reversión es el propio GUID guardado al aplicar.</summary>
    public object? RevertValue => OriginalValue;

    // ── Planes admitidos ──────────────────────────────────────────────────────
    /// <summary>Plan al que cambia el interruptor: «Alto rendimiento» o «Máximo rendimiento», solo si ya existe.</summary>
    public PowerPlan? TargetPlan
    {
        get
        {
            var plans = AvailablePlans();
            return plans.FirstOrDefault(p => p.Guid.Equals(HighPerformanceGuid, StringComparison.OrdinalIgnoreCase))
                ?? plans.FirstOrDefault(p => p.Guid.Equals(UltimatePerformanceGuid, StringComparison.OrdinalIgnoreCase));
        }
    }

    public string? TargetPlanGuid => TargetPlan?.Guid;
    public string TargetPlanName => TargetPlan?.Name ?? "ninguno disponible";

    /// <summary>¿Existe en este equipo un plan al que se pueda cambiar? Si no, el interruptor queda deshabilitado.</summary>
    public bool IsAvailable => TargetPlan != null;

    public string UnavailableMessage =>
        "No disponible en este equipo: Windows no tiene el plan Alto rendimiento.";

    /// <summary>Vuelve a leer la lista de planes (se llama al analizar de nuevo y tras aplicar o revertir).</summary>
    public void InvalidatePlanCache() => _plansCache = null;

    private IReadOnlyList<PowerPlan> AvailablePlans()
    {
        try { return _plansCache ??= _powerPlanService.GetAvailablePlans(); }
        catch { return Array.Empty<PowerPlan>(); }
    }

    /// <inheritdoc />
    public void RestoreBackup(object? originalValue, bool originalExisted)
    {
        if (HasOriginal) return;
        OriginalValue = originalValue;
        HasOriginal = true;
    }

    public object? ReadCurrentState()
    {
        InvalidatePlanCache();
        // Devuelve el GUID del plan activo para que OriginalValue permita revertir de forma exacta.
        return _powerPlanService.GetActivePlanGuid();
    }

    /// <summary>Está aplicado cuando el plan activo ya es el plan objetivo del interruptor.</summary>
    public bool IsAppliedInSystem
    {
        get
        {
            try
            {
                var target = TargetPlanGuid;
                return !string.IsNullOrWhiteSpace(target) &&
                       string.Equals(ReadCurrentState() as string, target, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }

    /// <summary>
    /// Devuelve el nombre legible del plan activo para mostrar en la interfaz de usuario.
    /// </summary>
    public string GetFriendlyName()
    {
        var name = _powerPlanService.GetActivePlanName();
        if (!string.IsNullOrEmpty(name))
            return name;
        return _powerPlanService.GetActivePlanGuid() ?? "Desconocido";
    }

    public void Apply(object? newValue, bool dryRun)
    {
        if (dryRun) return;

        // Solo se admiten «Alto rendimiento» y «Máximo rendimiento», y solo si ya existen en el equipo.
        var target = ResolveAdmittedPlan(newValue);
        if (target == null) throw new InvalidOperationException(UnavailableMessage);

        OriginalValue = _powerPlanService.GetActivePlanGuid();
        if (OriginalValue is not string originalGuid || string.IsNullOrWhiteSpace(originalGuid))
            throw new InvalidOperationException("No se pudo leer el plan de energía actual.");

        if (!_powerPlanService.SetActivePlan(target.Guid)) throw new InvalidOperationException("No se pudo cambiar el plan de energía.");
        HasOriginal = true;
        InvalidatePlanCache();
    }

    /// <summary>
    /// Resuelve el plan solicitado comprobando que su GUID es uno de los dos admitidos y que
    /// existe entre los planes del equipo. Cualquier otro plan (ahorro, equilibrado, personalizado)
    /// se rechaza: el interruptor nunca cambia a «el primero disponible».
    /// </summary>
    private PowerPlan? ResolveAdmittedPlan(object? newValue)
    {
        var guid = newValue switch
        {
            PowerPlan plan => plan.Guid,
            string s when !string.IsNullOrWhiteSpace(s) => s,
            _ => TargetPlanGuid
        };
        if (string.IsNullOrWhiteSpace(guid)) return null;

        return AvailablePlans().FirstOrDefault(p =>
            p.Guid.Equals(guid, StringComparison.OrdinalIgnoreCase) &&
            (p.Guid.Equals(HighPerformanceGuid, StringComparison.OrdinalIgnoreCase) ||
             p.Guid.Equals(UltimatePerformanceGuid, StringComparison.OrdinalIgnoreCase)));
    }

    public void Revert(bool dryRun)
    {
        if (!dryRun && HasOriginal && OriginalValue is string guid)
        {
            if (!_powerPlanService.SetActivePlan(guid)) throw new InvalidOperationException("No se pudo restaurar el plan de energía.");
            OriginalValue = null;
            HasOriginal = false;
            InvalidatePlanCache();
        }
    }
}
