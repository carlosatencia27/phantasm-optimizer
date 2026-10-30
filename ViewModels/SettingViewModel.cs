using System.ComponentModel;
using PC_Optimizer.Core;
using PC_Optimizer.Models;

namespace PC_Optimizer.ViewModels;

public class SettingViewModel : INotifyPropertyChanged
{
    private readonly ITuningEngine _tuningEngine;
    private readonly ISetting _setting;
    private readonly bool _dryRun;
    private bool _isOn;
    private bool _isApplying;

    public SettingViewModel(ITuningEngine tuningEngine, ISetting setting, bool dryRun)
    {
        _tuningEngine = tuningEngine;
        _setting = setting;
        _dryRun = dryRun;
        UpdateStateFromSystem();
    }

    public string Key => _setting.Key;
    public string Name => _setting.Name;
    public string Description => FormatDescription(_setting);
    public string RealisticEffect => _setting.RealisticEffect;
    public string VerificationStatus => _setting.VerificationStatus;
    public string? DocumentationUrl => _setting.DocumentationUrl;
    private static string FormatDescription(ISetting setting) => $"{setting.Description} Verificación: {setting.VerificationStatus}." +
        (setting.DocumentationUrl is { Length: > 0 } url ? $" Fuente: {url}" : string.Empty);
    public SettingRiskLevel RiskLevel => _setting.RiskLevel;
    public bool IsAdvanced => _setting.RiskLevel == SettingRiskLevel.Advanced;
    public bool IsWindowsVersionSupported => _setting.IsWindowsVersionSupported;

    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (_isOn == value) return;
            _isOn = value;
            OnPropertyChanged();
            _ = ApplyAsync();
        }
    }

    public bool IsApplying
    {
        get => _isApplying;
        set { _isApplying = value; OnPropertyChanged(); }
    }

    private void UpdateStateFromSystem()
    {
        var val = _setting.ReadCurrentState();
        _isOn = val is bool b ? b : val != null;
        OnPropertyChanged(nameof(IsOn));
    }

    private async System.Threading.Tasks.Task ApplyAsync()
    {
        IsApplying = true;
        try
        {
            var result = _tuningEngine.ApplySetting(_setting.Key, _isOn, _dryRun);
            if (!result.Success)
            {
                _isOn = !_isOn;
                OnPropertyChanged(nameof(IsOn));
            }
        }
        finally
        {
            IsApplying = false;
        }
    }

    public void Revert()
    {
        _tuningEngine.RevertSetting(_setting.Key, _dryRun);
        UpdateStateFromSystem();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
