using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using PC_Optimizer.Core;
using PC_Optimizer.Models;

namespace PC_Optimizer.ViewModels;

public class ProfileViewModel : INotifyPropertyChanged
{
    private readonly ITuningEngine _tuningEngine;
    private bool _dryRunMode;
    private string _statusMessage = string.Empty;

    public bool DryRunMode
    {
        get => _dryRunMode;
        set { _dryRunMode = value; OnPropertyChanged(); }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    // ── Presentación: qué ajustes activa cada perfil (nombres reales del motor) ──
    public IReadOnlyList<string> LowResourcesSettings => GetProfileSettings("Bajos recursos").Select(s => s.Name).ToList();
    public IReadOnlyList<string> GamingSettings => GetProfileSettings("Gaming").Select(s => s.Name).ToList();

    public ICommand ApplyLowResourcesCommand { get; }
    public ICommand ApplyGamingCommand { get; }
    public ICommand RevertProfileCommand { get; }

    public ProfileViewModel(ITuningEngine tuningEngine, bool dryRunMode = false)
    {
        _tuningEngine = tuningEngine;
        _dryRunMode = dryRunMode;

        ApplyLowResourcesCommand = new RelayCommand(_ => ApplyProfile("Bajos recursos"));
        ApplyGamingCommand = new RelayCommand(_ => ApplyProfile("Gaming"));
        RevertProfileCommand = new RelayCommand(_ => RevertProfile());
    }

    private void ApplyProfile(string profileName)
    {
        var settingsToApply = GetProfileSettings(profileName);

        if (settingsToApply.Count == 0)
        {
            StatusMessage = "Configuración no reconocida o no hay ajustes disponibles.";
            return;
        }

        if (settingsToApply.Any(s => s.RequiresAdmin && !ConfirmDialog.IsRunningAsAdmin()))
        {
            StatusMessage = "Se necesita administrador. Use el botón ‘Reiniciar como administrador’.";
            foreach (var setting in settingsToApply.Where(s => s.RequiresAdmin))
                _tuningEngine.RecordBlockedAction(setting.Key, setting.Name, _dryRunMode, StatusMessage);
            return;
        }

        if (settingsToApply.Any(s => s.RequiresConfirmation || s.RequiresRestart || s.RequiresAdmin)
            && !ConfirmDialog.RequestProfile(settingsToApply))
            return;

        var lines = new List<string>();
        var failures = new List<string>();
        var appliedCount = 0;
        var alreadyCount = 0;

        foreach (var setting in settingsToApply)
        {
            if (setting.RiskLevel == SettingRiskLevel.Advanced && !IsAdvancedAllowed())
            {
                failures.Add($"{setting.Name}: ajuste avanzado, requiere confirmación explícita.");
                continue;
            }

            if (setting.ApplyValue == null)
            {
                failures.Add($"{setting.Name}: no disponible en este equipo.");
                continue;
            }

            // ¿El sistema ya está en el estado que pide el ajuste? Se lee del sistema, no de que
            // exista una copia guardada (un ajuste que Windows ya trae activado no es un error).
            if (setting.IsAppliedInSystem)
            {
                alreadyCount++;
                lines.Add(setting.HasOriginal
                    ? $"• {setting.Name}: ya estaba aplicado por la app."
                    : $"• {setting.Name}: ya estaba así en Windows (no se cambió nada).");
                continue;
            }

            var result = _tuningEngine.ApplySetting(setting.Key, setting.ApplyValue, _dryRunMode);
            if (result.Success)
            {
                appliedCount++;
                lines.Add($"• {setting.Name}: {(_dryRunMode ? "aplicado en simulación" : "aplicado")}.");
            }
            else
            {
                failures.Add($"{setting.Name}: {result.Message}");
            }
        }

        var header = _dryRunMode
            ? $"Simulación del perfil «{profileName}»: {appliedCount} ajuste(s) se aplicarían, {alreadyCount} ya estaban así. No se cambió nada."
            : $"Configuración «{profileName}»: {appliedCount} ajuste(s) aplicado(s), {alreadyCount} ya estaban así.";
        var message = header + Environment.NewLine + string.Join(Environment.NewLine, lines);
        if (failures.Count > 0)
            message += Environment.NewLine + "Fallaron (los demás sí se aplicaron): " + string.Join("; ", failures);

        StatusMessage = message.TrimEnd();
    }

    private void RevertProfile()
    {
        var result = _tuningEngine.RevertAll(dryRun: _dryRunMode);
        StatusMessage = _dryRunMode
            ? $"Simulación: reversión total de perfiles: {result.Message}"
            : $"Reversión total: {result.Message}";
    }

    private List<ISetting> GetProfileSettings(string profileName)
    {
        var result = new List<ISetting>();

        if (profileName == "Bajos recursos")
        {
            // Desactiva la captura de Game Bar y las transparencias (ambos ajustes Seguros y reversibles).
            AddIfExists(result, "Game.GameBar");
            AddIfExists(result, "System.Transparency");
            // AddIfExists(result, "Game.Hags"); // HAGS no se incluye en perfiles: depende del hardware (Etapa 4C, punto 5)
        }
        else if (profileName == "Gaming")
        {
            AddIfExists(result, "Game.GameMode");
            AddIfExists(result, "Game.WindowedOptimization");
        }

        return result;
    }

    private void AddIfExists(List<ISetting> list, string key)
    {
        var setting = _tuningEngine.GetSetting(key);
        if (setting != null)
            list.Add(setting);
    }

    private static bool IsAdvancedAllowed() => false;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
