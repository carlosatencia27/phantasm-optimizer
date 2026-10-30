using System;
using PC_Optimizer.Models;
using PC_Optimizer.Services;

namespace PC_Optimizer.Core;

public class HibernateSetting : ISetting
{
    private readonly IHibernateService _hibernateService;
    private readonly IDeviceInfoService? _device;

    public HibernateSetting(IHibernateService hibernateService, IDeviceInfoService? deviceInfo = null)
    {
        _hibernateService = hibernateService;
        _device = deviceInfo;
        Key = "Power.Hibernate";
        Name = "Desactivar la hibernación de Windows (hiberfil.sys)";
        Description = $"Activado: desactiva la hibernación y elimina C:\\hiberfil.sys (tamaño actual: {FormatSize(hibernateService.GetHiberfileSizeBytes())}). Al revertir, la vuelve a activar. Necesita administrador.";
        RealisticEffect = "Desactivarla elimina hiberfil.sys y libera su tamaño real. Deja sin efecto el Inicio rápido y en portátiles se pierde la hibernación de seguridad con batería baja.";
        RiskLevel = SettingRiskLevel.Moderate;
        IsWindowsVersionSupported = true;
        VerificationStatus = "Documentado por Microsoft";
        DocumentationUrl = "https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/powercfg-command-line-options";
    }

    public string Key { get; }
    public string Name { get; }
    public string Description { get; }
    public string RealisticEffect { get; }

    /// <summary>Confirmación reforzada si el equipo tiene batería: se pierde la hibernación de seguridad.</summary>
    public string? PreApplyWarning
    {
        get
        {
            DeviceInfo? info = null;
            try { info = _device?.Get(); } catch { }
            if (info is not { HasBattery: true }) return null;
            return "Tu equipo tiene batería. Desactivar la hibernación quita la hibernación de seguridad que Windows suele usar cuando la batería se agota " +
                   "(podrías perder el trabajo no guardado) y además desactiva el inicio rápido de Windows.";
        }
    }
    public SettingRiskLevel RiskLevel { get; }
    public bool IsWindowsVersionSupported { get; }
    public bool IsReversible => true;
    public bool RequiresConfirmation => false;
    public bool RequiresRestart => false;
    public bool RequiresAdmin => true;
    public string VerificationStatus { get; }
    public string? DocumentationUrl { get; }

    public object? OriginalValue { get; private set; }
    public bool HasOriginal { get; private set; }

    /// <summary>Activado = hibernación desactivada (se elimina hiberfil.sys).</summary>
    public object? ApplyValue => true;
    /// <summary>Sin copia original, desactivar el ajuste vuelve a habilitar la hibernación.</summary>
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
        return !_hibernateService.IsHibernateEnabled();
    }

    public void Apply(object? newValue, bool dryRun)
    {
        if (!dryRun)
        {
            OriginalValue = _hibernateService.IsHibernateEnabled();

            // Desactivar hibernación si newValue es true o false según convenga:
            // Siguiendo el estándar de interruptores de optimización: activado = optimizado (hibernación desactivada para liberar espacio)
            bool disableHibernate = newValue is bool b ? b : true;
            if (!_hibernateService.SetHibernate(!disableHibernate)) throw new InvalidOperationException("No se pudo cambiar la hibernación.");
            HasOriginal = true;
        }
    }

    public void Revert(bool dryRun)
    {
        if (!dryRun && HasOriginal)
        {
            bool wasEnabled = OriginalValue is bool b ? b : true;
            if (!_hibernateService.SetHibernate(wasEnabled)) throw new InvalidOperationException("No se pudo restaurar la hibernación.");
            OriginalValue = null;
            HasOriginal = false;
        }
    }

    private static string FormatSize(long bytes) => bytes > 0
        ? $"{bytes / (1024d * 1024d * 1024d):0.00} GB"
        : "no disponible";
}
