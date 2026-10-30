using System;
using System.Collections.Generic;
using System.Linq;
using PC_Optimizer.Models;
using PC_Optimizer.Services;

namespace PC_Optimizer.Core;

public class HagsSetting : ISetting
{
    private const int HagsEnabled = 2;
    private const int HagsDisabled = 1;

    private readonly IHagsService _hagsService;

    public HagsSetting(IHagsService hagsService, IGpuInfoService? gpuInfo = null)
    {
        _hagsService = hagsService;
        _gpu = gpuInfo;
        Key = "Game.Hags";
        Name = "Activar la programación de GPU por hardware (HAGS)";
        _baseDescription = "Activado: enciende HAGS, que cambia quién programa el trabajo de la GPU: pasa de Windows a la propia tarjeta gráfica. Requiere reiniciar el equipo para aplicar. El efecto depende de la GPU, el controlador y el juego, y no garantiza más FPS.";
        RealisticEffect = "El efecto depende de la GPU, el controlador y el juego: en muchos equipos no cambia nada y en otros puede causar tirones o inestabilidad. La app no puede demostrarlo ni medirlo. No garantiza más FPS. Se puede revertir.";
        RiskLevel = SettingRiskLevel.Moderate;
        IsWindowsVersionSupported = _hagsService.IsSupported();
    }

    public string Key { get; }
    public string Name { get; }
    private readonly IGpuInfoService? _gpu;
    private readonly string _baseDescription;
    private IReadOnlyList<GpuInfo>? _gpuList;

    private IReadOnlyList<GpuInfo> Gpus
    {
        get
        {
            if (_gpuList == null)
            {
                try { _gpuList = _gpu?.GetGpus() ?? Array.Empty<GpuInfo>(); }
                catch { _gpuList = Array.Empty<GpuInfo>(); }
            }
            return _gpuList;
        }
    }

    /// <summary>Texto base más las GPU detectadas (solo lectura) y el aviso si hay más de una.</summary>
    public string Description
    {
        get
        {
            if (_gpu == null) return _baseDescription;
            var gpus = Gpus;
            if (gpus.Count == 0) return _baseDescription + " GPU detectadas: no se pudieron leer.";
            var text = _baseDescription + " GPU detectadas (solo lectura): " + string.Join("; ", gpus.Select(g => g.Name)) + ".";
            if (gpus.Count > 1)
                text += " ⚠ Este equipo tiene más de una GPU (típico de portátiles con gráficos integrados y dedicados): HAGS es un ajuste " +
                        "global de Windows y su efecto depende del controlador de cada una.";
            return text;
        }
    }

    /// <summary>Con más de una GPU se pide confirmación antes de aplicar.</summary>
    public string? PreApplyWarning => Gpus.Count > 1
        ? "Este equipo tiene más de una GPU (" + string.Join("; ", Gpus.Select(g => g.Name)) + "). HAGS es un ajuste global de Windows y en portátiles " +
          "con gráficos integrados y dedicados su efecto depende del controlador de cada una (puede no cambiar nada o causar tirones). " +
          "Requiere reiniciar."
        : null;
    public string RealisticEffect { get; }
    public SettingRiskLevel RiskLevel { get; }
    public bool IsWindowsVersionSupported { get; }
    public bool IsReversible => true;
    public bool RequiresConfirmation => true;
    public bool RequiresRestart => true;
    public bool RequiresAdmin => true;
    public string VerificationStatus => "Observado en Windows, no documentado oficialmente";
    public string? DocumentationUrl => null;

    public object? OriginalValue { get; private set; }
    public bool HasOriginal { get; private set; }

    /// <summary>Activado = HAGS activado (HwSchMode = 2).</summary>
    public object? ApplyValue => true;
    /// <summary>Sin copia original, desactivar el ajuste escribe HwSchMode = 1 (HAGS desactivado).</summary>
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
        if (!_hagsService.IsSupported())
            return "No disponible";

        return _hagsService.IsEnabled();
    }

    public void Apply(object? newValue, bool dryRun)
    {
        if (!dryRun)
        {
            OriginalValue = _hagsService.GetRawMode();

            bool enable = newValue is bool b && b;
            if (!_hagsService.SetRawMode(enable ? HagsEnabled : HagsDisabled, out var error))
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "No se pudo cambiar HAGS." : error);
            HasOriginal = true;
        }
    }

    public void Revert(bool dryRun)
    {
        if (!dryRun && HasOriginal)
        {
            bool restored = OriginalValue is int original
                ? _hagsService.SetRawMode(original, out var error)
                : _hagsService.DeleteRawMode(out error);
            if (!restored)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "No se pudo restaurar HAGS." : error);
            OriginalValue = null;
            HasOriginal = false;
        }
    }
}
