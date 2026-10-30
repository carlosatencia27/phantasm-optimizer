using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using PC_Optimizer.Core;
using PC_Optimizer.Models;

namespace PC_Optimizer.ViewModels;

public class BackupHistoryViewModel : INotifyPropertyChanged
{
    private readonly ITuningEngine _tuningEngine;
    private ObservableCollection<ChangeLogEntry> _changeLog = new();
    private string _statusMessage = string.Empty;

    /// <summary>Modo simulación: si está activo, revertir no toca el sistema real.</summary>
    public bool DryRunMode { get; set; }

    public ObservableCollection<ChangeLogEntry> ChangeLog
    {
        get => _changeLog;
        set { _changeLog = value; OnPropertyChanged(); }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public ICommand CreateRestorePointCommand { get; }
    public ICommand RevertEntryCommand { get; }

    /// <summary>
    /// Pregunta de confirmación (título, mensaje) para no pisar un cambio hecho fuera de la app.
    /// En las pruebas se reemplaza para no abrir ventanas.
    /// </summary>
    public Func<string, string, bool> ConfirmAction { get; set; } = DefaultConfirm;

    internal static bool DefaultConfirm(string title, string message)
        => System.Windows.MessageBox.Show(message, title, System.Windows.MessageBoxButton.YesNo,
               System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes;

    public BackupHistoryViewModel(ITuningEngine tuningEngine)
    {
        _tuningEngine = tuningEngine;
        CreateRestorePointCommand = new RelayCommand(_ => { /* gestionado en MainViewModel */ });
        RevertEntryCommand = new RelayCommand(param => RevertEntry(param?.ToString()));
        Refresh();
    }

    public void Refresh()
    {
        var entries = _tuningEngine.GetChangeLog()
            .OrderByDescending(c => c.OccurredAt)
            .ToList();

        // Solo se puede revertir lo que sigue teniendo su copia del valor original
        // (incluida la recuperada del disco al arrancar la app).
        foreach (var entry in entries)
            entry.RevertAvailable = entry.CanRevert && _tuningEngine.GetSetting(entry.SettingKey)?.HasOriginal == true;

        ChangeLog = new ObservableCollection<ChangeLogEntry>(entries);
        OnPropertyChanged(nameof(CanRevertAll));
    }

    private void RevertEntry(string? settingKey)
    {
        if (string.IsNullOrWhiteSpace(settingKey)) return;

        // Si el valor actual ya no es el que aplicó la app (se cambió a mano en Windows), revertir
        // pisaría ese cambio manual: se avisa y se pide confirmación antes de restaurar.
        var setting = _tuningEngine.GetSetting(settingKey);
        if (ManualChangeCheck.WasChangedOutsideTheApp(setting) &&
            !ConfirmAction("Confirmar reversión",
                $"{ManualChangeCheck.WarningText} ¿Desea restaurar de todos modos el valor guardado?" +
                $"{Environment.NewLine}{Environment.NewLine}Al continuar se sobrescribe el valor que hay ahora en Windows."))
        {
            StatusMessage = $"✖ Reversión cancelada: {ManualChangeCheck.WarningText} Se conserva el valor actual.";
            Refresh();
            return;
        }

        var result = _tuningEngine.RevertSetting(settingKey, dryRun: DryRunMode);
        StatusMessage = DryRunMode
            ? (result.Success ? $"◐ Solo simulado: no se cambió nada ({result.Message})" : $"✖ {result.Message}")
            : (result.Success ? $"✔ Revertido: {result.Message}" : $"✖ {result.Message}");
        Refresh();
    }

    /// <summary>true si hay un cambio aplicado que todavía se puede revertir.</summary>
    public bool CanRevertAll => ChangeLog.Any(c => c.CanRevert);
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
