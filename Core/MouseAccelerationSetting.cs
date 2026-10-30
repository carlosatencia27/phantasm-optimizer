using System;
using PC_Optimizer.Models;
using PC_Optimizer.Services;

namespace PC_Optimizer.Core;

public class MouseAccelerationSetting : ISetting
{
    private readonly IMouseService _mouseService;

    public MouseAccelerationSetting(IMouseService mouseService)
    {
        _mouseService = mouseService;
        Key = "System.MouseAcceleration";
        Name = "Desactivar la aceleración del ratón (Mejorar precisión del puntero)";
        Description = "Activado: desactiva la aceleración no lineal del cursor (MouseSpeed y umbrales a 0) y avisa a Windows al instante con SystemParametersInfo. Al revertir, restaura los valores originales.";
        RealisticEffect = "Desactiva la curva de aceleración no lineal de Windows ('Mejorar la precisión del puntero') para que el movimiento sea estrictamente 1:1. Es una preferencia de control y precisión motriz; no es una mejora de rendimiento del hardware ni reduce el retraso de entrada (input lag).";
        RiskLevel = SettingRiskLevel.Safe;
        IsWindowsVersionSupported = true;
        VerificationStatus = "Documentado por Microsoft";
        DocumentationUrl = "https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfow";
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

    /// <summary>Activado = aceleración del ratón desactivada (movimiento 1:1).</summary>
    public object? ApplyValue => true;
    /// <summary>Sin copia original, desactivar el ajuste restaura los valores predeterminados de Windows.</summary>
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
        var (speed, t1, t2) = _mouseService.GetMouseSettings();
        // El interruptor representa el ajuste aplicado: aceleración desactivada.
        return speed == "0" && t1 == "0" && t2 == "0";
    }

    public void Apply(object? newValue, bool dryRun)
    {
        if (!dryRun)
        {
            var (origSpeed, origT1, origT2) = _mouseService.GetMouseSettings();
            OriginalValue = new MouseSnapshot(origSpeed, origT1, origT2);
            // Activado = desactivar la aceleración (1:1 lineal). Al desactivar el ajuste se
            // escriben los valores predeterminados de Windows.
            bool disable = newValue is bool b ? b : true;
            bool ok = disable
                ? _mouseService.SetMouseSettings("0", "0", "0")
                : _mouseService.SetMouseSettings(DefaultSpeed, DefaultThreshold1, DefaultThreshold2);
            if (!ok) throw new InvalidOperationException("No se pudo cambiar la aceleración del ratón.");
            HasOriginal = true;
        }
    }

    public void Revert(bool dryRun)
    {
        if (!dryRun && HasOriginal)
        {
            var orig = OriginalValue as MouseSnapshot;
            if (orig != null && !_mouseService.SetMouseSettings(orig.Speed, orig.Threshold1, orig.Threshold2))
                throw new InvalidOperationException("No se pudo restaurar la aceleración del ratón.");
            OriginalValue = null;
            HasOriginal = false;
        }
    }

    /// <summary>Copia del estado original del ratón. Pública para poder guardarla y restaurarla en JSON.</summary>
    public sealed record MouseSnapshot(string? Speed, string? Threshold1, string? Threshold2);

    // Valores predeterminados de Windows (aceleración activada).
    public const string DefaultSpeed = "1";
    public const string DefaultThreshold1 = "6";
    public const string DefaultThreshold2 = "10";
}
