using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using PC_Optimizer.Core;
using PC_Optimizer.Models;
using PC_Optimizer.Services;

namespace PC_Optimizer.ViewModels;

public class CleanupViewModel : INotifyPropertyChanged
{
    private readonly ITuningEngine _tuningEngine;
    private readonly IDiskCleanupService _cleanupService;
    private readonly IDeliveryOptimizationService _deliveryOptimization;
    private readonly Func<bool> _isAdmin;
    private readonly Func<string, string, bool> _confirm;
    private readonly string _userTempPath;
    private readonly string _werPath;
    private bool _dryRunMode;
    private bool _isLoading;
    private string _statusMessage = string.Empty;
    private bool _refreshInProgress;

    public ObservableCollection<CleanupItemViewModel> Items { get; } = new();

    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowEmpty)); }
    }

    private bool _hasError;
    private string _errorText = string.Empty;
    public bool HasError { get => _hasError; private set { _hasError = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowEmpty)); } }
    public string ErrorText { get => _errorText; private set { _errorText = value; OnPropertyChanged(); } }

    /// <summary>«No hay nada que limpiar» solo si ya terminó de calcular, sin error y sin elementos.</summary>
    public bool ShowEmpty => !IsLoading && !HasError && Items.Count == 0;
    public ICommand RetryCommand { get; }

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public bool DryRunMode
    {
        get => _dryRunMode;
        set { _dryRunMode = value; OnPropertyChanged(); }
    }

    public ICommand ApplyCommand { get; }
    public ICommand RevertCommand { get; }
    public ICommand RevertAllCommand { get; }
    public ICommand RestartAsAdminCommand { get; }

    /// <summary>Se dispara al pulsar «Reiniciar como administrador» (lo conecta MainViewModel).</summary>
    public Action? RestartAsAdminRequested { get; set; }

    public CleanupViewModel(ITuningEngine tuningEngine, IDiskCleanupService cleanupService, string userTempPath,
        bool dryRunMode = false, IDeliveryOptimizationService? deliveryOptimization = null,
        Func<bool>? isAdmin = null, Func<string, string, bool>? confirm = null, string? werPath = null)
    {
        _tuningEngine = tuningEngine;
        _cleanupService = cleanupService;
        _userTempPath = Path.GetFullPath(userTempPath);
        _dryRunMode = dryRunMode;
        _deliveryOptimization = deliveryOptimization ?? new SystemDeliveryOptimizationService(cleanupService);
        _isAdmin = isAdmin ?? ConfirmDialog.IsRunningAsAdmin;
        _confirm = confirm ?? DefaultConfirm;
        _werPath = werPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "Windows", "WER");

        RetryCommand = new RelayCommand(_ => Refresh());
        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ShowEmpty));
        ApplyCommand = new RelayCommand(param => ApplyItem(param?.ToString() ?? string.Empty));
        RevertCommand = new RelayCommand(_ => { });
        RevertAllCommand = new RelayCommand(_ => { });
        RestartAsAdminCommand = new RelayCommand(_ => RestartAsAdminRequested?.Invoke());

        StatusMessage = "La vista previa se calculará al abrir Limpieza.";
    }

    private static bool DefaultConfirm(string message, string title)
        => System.Windows.MessageBox.Show(message, title,
               System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning)
           == System.Windows.MessageBoxResult.Yes;

    /// <summary>Acciones de limpieza de esta pantalla. Cada una decide su vista previa y su disponibilidad.</summary>
    private IReadOnlyList<ICleanupAction> BuildActions() => new ICleanupAction[]
    {
        new TempFileSetting(_cleanupService, _userTempPath,
            "Limpiar archivos temporales del usuario",
            "Elimina archivos de la carpeta %TEMP%. No se puede deshacer.",
            "Puede liberar espacio en disco; no garantiza más FPS ni una mejora medible del rendimiento.",
            SettingRiskLevel.Safe,
            targetDescription: "la carpeta %TEMP% del usuario"),

        // Solo la carpeta de informes del usuario: nunca la de ProgramData.
        new TempFileSetting(_cleanupService, _werPath,
            "Borrar informes de errores de Windows",
            "Elimina los informes de errores guardados en tu carpeta de usuario "
                + "(%LOCALAPPDATA%\\Microsoft\\Windows\\WER). No se puede deshacer.",
            "Puede liberar espacio en disco. Windows vuelve a guardar informes cuando ocurre un error.",
            SettingRiskLevel.Safe,
            verificationStatus: "Observado en Windows, no documentado oficialmente",
            actionLabel: "Borrar informes",
            targetDescription: "tu carpeta de informes de errores (%LOCALAPPDATA%\\Microsoft\\Windows\\WER)",
            hideWhenEmpty: true),

        new DeliveryOptimizationCleanupSetting(_deliveryOptimization)
    };

    public void Refresh() => _ = RefreshAsync();

    /// <summary>
    /// Calcula la vista previa de todas las acciones en segundo plano y deja solo las que tienen algo
    /// que limpiar (o un motivo que explicar). No borra nada.
    /// </summary>
    public async Task RefreshAsync()
    {
        if (_refreshInProgress) return;
        _refreshInProgress = true;
        HasError = false;
        IsLoading = true;
        StatusMessage = "Calculando la vista previa. No se ha borrado nada.";

        bool isAdmin = SafeIsAdmin();
        List<CleanupItemViewModel> items;
        try
        {
            var actions = BuildActions();
            items = await Task.Run(() =>
            {
                var list = new List<CleanupItemViewModel>();
                foreach (var action in actions)
                {
                    CleanupAvailability availability;
                    try { availability = action.Probe(isAdmin); }
                    catch (Exception ex)
                    {
                        availability = new CleanupAvailability(0, 0, $"No se pudo leer: {PC_Optimizer.Services.UserErrorText.Of(ex)}", false, false);
                    }

                    if (action.HideWhenEmpty && !availability.HasProblem && availability.FileCount == 0) continue;

                    var item = new CleanupItemViewModel(action);
                    item.UpdateAvailability(availability);
                    list.Add(item);
                }
                return list;
            });
        }
        catch (Exception ex)
        {
            ErrorText = PC_Optimizer.Services.UserErrorText.Of(ex);
            HasError = true;
            StatusMessage = $"No se pudo calcular la vista previa: {ErrorText}";
            IsLoading = false;
            _refreshInProgress = false;
            return;
        }

        Items.Clear();
        foreach (var item in items) Items.Add(item);

        StatusMessage = Items.Count == 0
            ? "No hay nada que limpiar ahora mismo: no se encontraron archivos en las carpetas revisadas."
            : "Vista previa actualizada. No se ha borrado nada; los archivos en uso se omiten.";
        IsLoading = false;
        _refreshInProgress = false;
    }

    private bool SafeIsAdmin()
    {
        try { return _isAdmin(); } catch { return false; }
    }

    private async void ApplyItem(string key)
    {
        var item = Items.FirstOrDefault(i => i.Key == key);
        if (item == null) return;

        var action = item.Action;
        if (!action.IsWindowsVersionSupported)
        {
            StatusMessage = "Esta acción no aplica a la versión de Windows de este equipo.";
            return;
        }

        if (action.RequiresAdmin && !SafeIsAdmin())
        {
            StatusMessage = "Requiere permisos de administrador. Usa «Reiniciar como administrador».";
            return;
        }

        var before = item.Availability;

        string confirmMessage =
            $"¿Desea ejecutar «{action.Name}»?{Environment.NewLine}{Environment.NewLine}"
            + $"Borra: {action.TargetDescription}.{Environment.NewLine}"
            + (before.FileCount > 0
                ? $"Se liberarán {before.FileCount} archivo(s) ({ByteSize.Format(before.BytesToFree)}).{Environment.NewLine}"
                : string.Empty)
            + Environment.NewLine
            + (_dryRunMode
                ? "MODO SIMULACIÓN: no se borrará nada."
                : "Los archivos se borran de forma permanente y NO se puede deshacer.");

        if (!_dryRunMode && !_tuningEngine.License.CanModifySystem)
        {
            StatusMessage = "No se borró nada porque falta una licencia válida. " + _tuningEngine.License.BlockedMessage;
            return;
        }

        if (!_confirm(confirmMessage, "Confirmar limpieza")) return;

        if (_dryRunMode)
        {
            var simulationNote = $"Simulación: no se borraron archivos. "
                + (before.FileCount > 0 ? $"Se habrían liberado {before.FileCount} archivo(s) ({before.BytesToFree} bytes)." : "No hay archivos.");
            _tuningEngine.RecordNonReversibleAction(key, action.Name, true, simulationNote);
            StatusMessage = "Simulación: no se borró nada. " + simulationNote;
            return;
        }

        try
        {
            bool isAdmin = SafeIsAdmin();
            var result = await Task.Run(() => action.CleanAndVerify(isAdmin));
            _tuningEngine.RecordNonReversibleAction(key, action.Name, false, result.HistoryNote);
            item.UpdateAvailability(result.After);

            if (!result.Succeeded)
            {
                StatusMessage = $"No se pudo completar la limpieza: {result.Message}";
                return;
            }

            StatusMessage = result.Message;

            // Si ya no queda nada, la tarjeta se retira como cualquier vista previa vacía.
            if (action.HideWhenEmpty && !result.After.HasProblem && result.After.FileCount == 0)
            {
                Items.Remove(item);
                if (Items.Count == 0)
                    StatusMessage = result.Message + " No queda nada más que limpiar.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"No se pudo completar la limpieza: {PC_Optimizer.Services.UserErrorText.Of(ex)}";
        }
    }

    private void RevertItem(string key)
    {
        // Las limpiezas no se revierten: solo se vuelve a calcular la vista previa.
        _ = RefreshAsync();
    }

    private void RevertAllItems() => _ = RefreshAsync();

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public class CleanupItemViewModel : INotifyPropertyChanged
{
    public ICleanupAction Action { get; }
    public ISetting Setting => Action;
    public string Key => Action.Key;
    public string Name => Action.Name;
    public string Description => Action.Description;
    public string RealisticEffect => Action.RealisticEffect;
    public SettingRiskLevel RiskLevel => Action.RiskLevel;
    public string ActionLabel => Action.ActionLabel;

    /// <summary>Nombre del nivel de riesgo en español.</summary>
    public string RiskText => RiskLevel switch
    {
        SettingRiskLevel.Safe => "Seguro",
        SettingRiskLevel.Moderate => "Moderado",
        SettingRiskLevel.Advanced => "Avanzado",
        _ => RiskLevel.ToString()
    };

    /// <summary>Símbolo del nivel de riesgo (no depende solo del color).</summary>
    public string RiskGlyph => RiskLevel switch
    {
        SettingRiskLevel.Safe => "✓",
        SettingRiskLevel.Moderate => "!",
        SettingRiskLevel.Advanced => "⚠",
        _ => "•"
    };

    /// <summary>Etiqueta de verificación del ajuste.</summary>
    public string VerificationChip => string.IsNullOrWhiteSpace(Setting.VerificationStatus)
        ? "Verificación: no disponible"
        : $"Verificación: {Setting.VerificationStatus}";

    public bool HasSource => !string.IsNullOrWhiteSpace(Setting.DocumentationUrl);
    public string? DocumentationUrl => Setting.DocumentationUrl;

    public bool IsWindowsVersionSupported => Setting.IsWindowsVersionSupported;

    private CleanupAvailability _availability = new(0, 0, null, true, false);

    public CleanupAvailability Availability => _availability;

    /// <summary>Motivo por el que la acción no se puede ofrecer (administrador, cmdlet ausente…).</summary>
    public string? ProblemText => _availability.ProblemText;
    public bool HasProblem => _availability.HasProblem;
    public bool CanClean => _availability.CanClean && IsWindowsVersionSupported;
    public bool ShowElevate => _availability.ShowElevate;

    private string _currentValueText = string.Empty;
    public string CurrentValueText
    {
        get => _currentValueText;
        set { _currentValueText = value; OnPropertyChanged(); }
    }

    public bool CanRevert => Setting.HasOriginal;
    public bool IsReversible => Setting.IsReversible;

    private string _previewText = string.Empty;
    public string PreviewText
    {
        get => _previewText;
        set { _previewText = value; OnPropertyChanged(); }
    }

    /// <summary>Chip «A liberar: …»; si la vista previa no está disponible, lo dice sin inventar cifras.</summary>
    public string PreviewChip => HasProblem ? "Vista previa: no disponible" : $"A liberar: {PreviewText}";

    public string CleanTooltip =>
        $"Pide confirmación y borra de forma permanente: {Action.TargetDescription}. No se puede deshacer.";

    public CleanupItemViewModel(ICleanupAction action)
    {
        Action = action;
        UpdateCurrentValueText();
        UpdatePreviewText();
    }

    public void UpdateAvailability(CleanupAvailability availability)
    {
        _availability = availability;
        UpdatePreviewText();
        OnPropertyChanged(nameof(Availability));
        OnPropertyChanged(nameof(ProblemText));
        OnPropertyChanged(nameof(HasProblem));
        OnPropertyChanged(nameof(CanClean));
        OnPropertyChanged(nameof(ShowElevate));
        OnPropertyChanged(nameof(PreviewChip));
        OnPropertyChanged(nameof(CanRevert));
    }

    private void UpdatePreviewText()
    {
        if (_availability.HasProblem)
        {
            PreviewText = string.Empty;
            CurrentValueText = "No disponible";
            return;
        }

        PreviewText = _availability.FileCount > 0
            ? $"{_availability.FileCount} archivos · {ByteSize.Format(_availability.BytesToFree)}"
            : "Sin archivos detectados";
        CurrentValueText = ByteSize.Format(_availability.BytesToFree);
    }

    private void UpdateCurrentValueText()
    {
        if (_availability.HasProblem) CurrentValueText = "No disponible";
    }

    public static string FormatBytes(long bytes) => ByteSize.Format(bytes);

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
