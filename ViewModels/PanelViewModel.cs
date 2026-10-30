using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using PC_Optimizer.Core;
using PC_Optimizer.Models;
using PC_Optimizer.Services;

namespace PC_Optimizer.ViewModels;

public class PanelViewModel : INotifyPropertyChanged
{
    private readonly ITuningEngine _tuningEngine;
    private readonly ISystemInfoService? _systemInfoService;
    private bool _dryRunMode;
    private string _statusMessage = string.Empty;
    private SystemMetrics _metrics = new();
    private int _metricsRefreshInProgress;

    public ObservableCollection<PanelGroup> Groups { get; } = new();

    public SystemMetrics Metrics
    {
        get => _metrics;
        private set
        {
            _metrics = value; OnPropertyChanged();
            RecordHistory();
        }
    }

    // ── Telemetría: disponibilidad real y últimas ~60 muestras (solo cuando hay dato) ──
    private readonly MetricHistory _cpuHistory = new(), _ramHistory = new(), _diskHistory = new(), _netHistory = new();
    public IReadOnlyList<double> CpuHistory { get; private set; } = Array.Empty<double>();
    public IReadOnlyList<double> RamHistory { get; private set; } = Array.Empty<double>();
    public IReadOnlyList<double> DiskHistory { get; private set; } = Array.Empty<double>();
    public IReadOnlyList<double> NetHistory { get; private set; } = Array.Empty<double>();
    /// <summary>La CPU se considera disponible cuando ya llegó la primera lectura completa (hay RAM total).</summary>
    public bool CpuAvailable => _metrics.RamTotalBytes > 0;
    public bool RamAvailable => _metrics.RamTotalBytes > 0;
    public bool DiskAvailable => _metrics.DiskTotalBytes > 0;
    public bool NetAvailable => !string.IsNullOrWhiteSpace(_metrics.NetworkDescription) || !string.IsNullOrWhiteSpace(_metrics.NetworkSpeed);

    public string CpuText => CpuAvailable ? $"{_metrics.CpuUsagePercent:0} %" : "No disponible";
    public string RamText => RamAvailable ? $"{_metrics.RamUsagePercent:0} %" : "No disponible";
    public string DiskText => DiskAvailable ? $"{_metrics.DiskUsagePercent:0} %" : "No disponible";

    private void RecordHistory()
    {
        if (CpuAvailable) { _cpuHistory.Add(_metrics.CpuUsagePercent); CpuHistory = _cpuHistory.Snapshot(); }
        if (RamAvailable) { _ramHistory.Add(_metrics.RamUsagePercent); RamHistory = _ramHistory.Snapshot(); }
        if (DiskAvailable) { _diskHistory.Add(_metrics.DiskUsagePercent); DiskHistory = _diskHistory.Snapshot(); }
        if (NetAvailable) { _netHistory.Add(_metrics.NetworkUsagePercent); NetHistory = _netHistory.Snapshot(); }
        OnPropertyChanged(nameof(CpuText)); OnPropertyChanged(nameof(RamText)); OnPropertyChanged(nameof(DiskText));
        OnPropertyChanged(nameof(CpuHistory)); OnPropertyChanged(nameof(RamHistory));
        OnPropertyChanged(nameof(DiskHistory)); OnPropertyChanged(nameof(NetHistory));
        OnPropertyChanged(nameof(CpuAvailable)); OnPropertyChanged(nameof(RamAvailable));
        OnPropertyChanged(nameof(DiskAvailable)); OnPropertyChanged(nameof(NetAvailable));
    }

    /// <summary>Avisos flotantes: se muestran DESPUÉS de que el motor confirma el resultado real.</summary>
    public ToastManager? Toasts { get; set; }

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    private bool _isLoadingSettings;
    public bool IsLoadingSettings
    {
        get => _isLoadingSettings;
        set { _isLoadingSettings = value; OnPropertyChanged(); }
    }

    public bool DryRunMode
    {
        get => _dryRunMode;
        set { _dryRunMode = value; OnPropertyChanged(); }
    }

    public ICommand AnalyzeCommand { get; }

    /// <summary>
    /// Pregunta de confirmación (título, mensaje). En las pruebas se reemplaza para no abrir ventanas.
    /// </summary>
    public Func<string, string, bool> ConfirmAction { get; set; } = DefaultConfirm;

    internal static bool DefaultConfirm(string title, string message)
        => System.Windows.MessageBox.Show(message, title, System.Windows.MessageBoxButton.YesNo,
               System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes;

    /// <summary>Navegación solicitada desde el Panel (la conecta MainViewModel).</summary>
    public Action<string>? NavigationRequested { get; set; }

    public ICommand GoToCleanupCommand { get; }

    public PanelViewModel(ITuningEngine tuningEngine, ISystemInfoService? systemInfoService = null,
        bool dryRunMode = false, IDeviceInfoService? deviceInfo = null)
    {
        _deviceInfo = deviceInfo;
        _tuningEngine = tuningEngine;
        _systemInfoService = systemInfoService;
        _dryRunMode = dryRunMode;
        AnalyzeCommand = new RelayCommand(_ => LoadStatesAsync());
        GoToCleanupCommand = new RelayCommand(_ => NavigationRequested?.Invoke("Cleanup"));
        LoadGroups();
        RefreshMetrics();
        LoadStatesAsync();
        LoadDeviceSummary();
    }

    private readonly IDeviceInfoService? _deviceInfo;
    private string _deviceSummary = "Tipo de equipo: detectando…";

    /// <summary>Tipo de equipo (portátil o escritorio, batería y corriente), solo lectura.</summary>
    public string DeviceSummary
    {
        get => _deviceSummary;
        private set { _deviceSummary = value; OnPropertyChanged(); }
    }

    /// <summary>La lectura (WMI y estado de energía) se hace en segundo plano para no retrasar el arranque.</summary>
    private void LoadDeviceSummary()
    {
        if (_deviceInfo == null) { DeviceSummary = "Tipo de equipo: no disponible"; return; }
        var service = _deviceInfo;
        System.Threading.Tasks.Task.Run(() =>
        {
            string text;
            try { text = service.Get().Summary; }
            catch { text = "Tipo de equipo: no se puede determinar"; }
            DeviceSummary = text;
        });
    }

    public void RefreshMetrics()
    {
        if (_systemInfoService == null || System.Threading.Interlocked.Exchange(ref _metricsRefreshInProgress, 1) != 0) return;
        _ = System.Threading.Tasks.Task.Run(() =>
        {
            SystemMetrics metrics;
            try { metrics = _systemInfoService.GetCurrentMetrics(); }
            catch { metrics = new SystemMetrics(); }

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted)
            {
                System.Threading.Interlocked.Exchange(ref _metricsRefreshInProgress, 0);
                return;
            }
            dispatcher.InvokeAsync(() =>
            {
                Metrics = metrics;
                System.Threading.Interlocked.Exchange(ref _metricsRefreshInProgress, 0);
            });
        });
    }

    private void LoadGroups()
    {
        Groups.Clear();
        var groups = new Dictionary<string, PanelGroup>();

        foreach (var setting in _tuningEngine.Settings)
        {
            if (setting.Key.StartsWith("Demo.", StringComparison.Ordinal))
                continue; // ajuste solo de pruebas, no se muestra al usuario

            var category = CategoryFor(setting.Key);
            if (!groups.TryGetValue(category, out var group))
            {
                group = new PanelGroup(category);
                groups[category] = group;
                Groups.Add(group);
            }

            // Inicialmente en estado de carga para abrir la ventana de inmediato sin bloquear la UI
            group.Items.Add(new PanelItemViewModel(setting, null, this, isLoading: true));
        }

        // Orden fijo de grupos
        var ordered = Groups.OrderBy(g => GroupOrder(g.Title)).ToList();
        Groups.Clear();
        foreach (var g in ordered) Groups.Add(g);

        StatusMessage = "Cargando estado de los ajustes...";
    }

    /// <summary>
    /// Lee el estado de todos los ajustes fuera del hilo de UI para que la ventana abra de inmediato.
    /// </summary>
    public void LoadStatesAsync()
    {
        IsLoadingSettings = true;
        StatusMessage = "Leyendo estado actual de los ajustes...";

        System.Threading.Tasks.Task.Run(() =>
        {
            var states = ReadAllStates();

            var app = System.Windows.Application.Current;
            if (app?.Dispatcher != null && !app.Dispatcher.HasShutdownStarted)
            {
                app.Dispatcher.InvokeAsync(() => ApplyStates(states));
            }
            else
            {
                ApplyStates(states);
            }
        });
    }

    /// <summary>
    /// Vuelve a leer el estado real de todos los ajustes y lo refleja en las filas (principio 3).
    /// Se usa al volver a la pantalla y en las pruebas, donde no hay bucle de mensajes de WPF.
    /// </summary>
    public void ReloadStatesNow() => ApplyStates(ReadAllStates());

    private Dictionary<string, object?> ReadAllStates()
    {
        var states = new Dictionary<string, object?>();
        foreach (var setting in _tuningEngine.Settings)
        {
            try
            {
                states[setting.Key] = setting.ReadCurrentState();
            }
            catch
            {
                states[setting.Key] = null;
            }
        }
        return states;
    }

    private void ApplyStates(Dictionary<string, object?> states)
    {
        foreach (var group in Groups)
        {
            foreach (var item in group.Items)
            {
                if (states.TryGetValue(item.Setting.Key, out var val))
                {
                    item.RefreshState(val);
                }
            }
        }
        IsLoadingSettings = false;
        StatusMessage = $"{Groups.Sum(g => g.Items.Count)} ajustes disponibles.";
    }

    private static string CategoryFor(string key)
    {
        if (key.StartsWith("Cleanup.", StringComparison.Ordinal)) return "Limpieza";
        if (key.StartsWith("Game.", StringComparison.Ordinal)) return "Juegos";
        if (key.StartsWith("Power.", StringComparison.Ordinal) || key.StartsWith("System.", StringComparison.Ordinal)) return "Sistema";
        if (key.StartsWith("Monitor.", StringComparison.Ordinal)) return "Pantalla";
        if (key.StartsWith("Network.", StringComparison.Ordinal)) return "Red";
        if (key.StartsWith("Startup.", StringComparison.Ordinal)) return "Inicio";
        return "Otros";
    }

    private static int GroupOrder(string title) => title switch
    {
        "Sistema" => 0,
        "Pantalla" => 1,
        "Juegos" => 2,
        "Red" => 3,
        "Inicio" => 4,
        "Limpieza" => 5,
        _ => 99
    };

    public async void ApplyItem(PanelItemViewModel item)
    {
        var setting = item.Setting;
        if (setting == null) return;

        if (setting.RequiresAdmin && !ConfirmDialog.IsRunningAsAdmin())
        {
            const string message = "Se necesita administrador. Use el botón ‘Reiniciar como administrador’.";
            _tuningEngine.RecordBlockedAction(setting.Key, setting.Name, _dryRunMode, message);
            StatusMessage = message;
            return;
        }

        string? preview = null;
        if (setting is Core.TempFileSetting tf)
        {
            var (fileCount, bytes) = tf.GetPreview();
            preview = fileCount > 0
                ? $"{Environment.NewLine}Se liberarán {fileCount} archivo(s) ({CleanupItemViewModelFormatBytes(bytes)})."
                : $"{Environment.NewLine}Sin archivos detectados.";
        }

        bool confirm;
        if (setting is Core.TempFileSetting)
        {
            var reqs = ConfirmDialog.RequirementsText(setting);
            var msg = $"¿Desea borrar los archivos temporales de \"{setting.Name}\"?{preview}{Environment.NewLine}{Environment.NewLine}Los archivos se borran permanentemente y no se pueden recuperar."
                + (string.IsNullOrEmpty(reqs) ? "" : $"{Environment.NewLine}{Environment.NewLine}Requisitos: {reqs}.");
            confirm = System.Windows.MessageBox.Show(msg, "Confirmar limpieza", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes;
        }
        else
        {
            confirm = ConfirmDialog.Request(setting, ConfirmDialog.IsRunningAsAdmin());
        }

        if (!confirm) return;

        // Cada ajuste decide su propio valor de «aplicar» (bloque A): nunca un «true» genérico.
        object? requestedValue = setting.ApplyValue;
        if (requestedValue == null)
        {
            StatusMessage = setting is PowerPlanSetting unavailablePlan
                ? unavailablePlan.UnavailableMessage
                : "Este ajuste no se puede aplicar: no define ningún valor de aplicación.";
            return;
        }
        if (setting is DnsSetting dnsBeforeApply)
            StatusMessage = $"Aplicando DNS Cloudflare en {dnsBeforeApply.DnsService.GetAdapterName()}...";
        IsLoadingSettings = true;
        var result = await System.Threading.Tasks.Task.Run(() => _tuningEngine.ApplySetting(setting.Key, requestedValue, _dryRunMode));
        IsLoadingSettings = false;
        RefreshHistory();
        if (result.Success)
        {
            item.RefreshState(setting.ReadCurrentState());
            StatusMessage = _dryRunMode
                ? $"Simulación: {result.Message}"
                : $"Ajuste aplicado. {result.Message}";
            // «Deshacer» usa la MISMA ruta de reversión de siempre (RevertItem, con sus confirmaciones).
            Toasts?.Show(_dryRunMode ? $"Ajuste probado sin cambiar nada: {item.Name}" : $"Ajuste activado: {item.Name}", ToastKind.Success,
                undo: _dryRunMode ? null : () => RevertItem(item), simulated: _dryRunMode);
            if (!_dryRunMode && setting is DnsSetting dns && dns.RequiresRestartAfterApply)
                StatusMessage += " WMI indicó que se requiere reiniciar para completar el cambio DNS.";
        }
        else
        {
            StatusMessage = $"Error: {result.Message}";
            Toasts?.Show($"No se pudo aplicar «{item.Name}»: {result.Message}", ToastKind.Error, simulated: _dryRunMode);
        }
    }

    public async void RevertItem(PanelItemViewModel item)
    {
        var setting = item.Setting;

        // Si el ajuste se cambió a mano en Windows, revertir pisaría ese cambio: se avisa primero.
        if (ManualChangeCheck.WasChangedOutsideTheApp(setting) &&
            !ConfirmAction("Confirmar reversión",
                $"{ManualChangeCheck.WarningText} ¿Desea restaurar de todos modos el valor guardado?" +
                $"{Environment.NewLine}{Environment.NewLine}Al continuar se sobrescribe el valor que hay ahora en Windows."))
        {
            StatusMessage = $"Reversión cancelada: {ManualChangeCheck.WarningText} Se conserva el valor actual.";
            return;
        }

        IsLoadingSettings = true;
        // Si no hay copia original, se escribe el valor de reversión que declara el propio ajuste
        // (comportamiento predeterminado de Windows) en lugar de fallar sin explicación.
        var result = !setting.HasOriginal && setting.RevertValue != null
            ? await System.Threading.Tasks.Task.Run(() => _tuningEngine.RestoreDefaultValue(setting.Key, setting.RevertValue, _dryRunMode))
            : await System.Threading.Tasks.Task.Run(() => _tuningEngine.RevertSetting(setting.Key, _dryRunMode));
        IsLoadingSettings = false;
        RefreshHistory();
        item.RefreshState(setting.ReadCurrentState());
        StatusMessage = _dryRunMode
            ? $"Simulación de reversión: {result.Message}"
            : $"Reversión: {result.Message}";
        if (result.Success)
            Toasts?.Show(_dryRunMode ? $"Reversión probada sin cambiar nada: {item.Name}" : $"Ajuste revertido: {item.Name}", ToastKind.Success, simulated: _dryRunMode);
        else
            Toasts?.Show($"No se pudo revertir «{item.Name}»: {result.Message}", ToastKind.Error, simulated: _dryRunMode);
    }

    /// <summary>
    /// El interruptor alterna según el estado REAL leído del sistema: si el valor actual ya es el
    /// valor «aplicado», revierte (solo si la app guardó el valor original); si no, aplica.
    /// </summary>
    public void ToggleItem(PanelItemViewModel item)
    {
        if (!item.IsAppliedInSystem)
        {
            ApplyItem(item);
            return;
        }

        if (item.Setting.HasOriginal)
        {
            RevertItem(item);
            return;
        }

        // Windows ya está en ese estado y la app no guardó ningún valor original que restaurar.
        StatusMessage = item.AppliedWithoutBackupTooltip;
    }

    public async void RevertAll()
    {
        IsLoadingSettings = true;
        var result = await System.Threading.Tasks.Task.Run(() => _tuningEngine.RevertAll(_dryRunMode));
        IsLoadingSettings = false;
        RefreshHistory();
        foreach (var group in Groups)
            foreach (var item in group.Items)
                item.RefreshState(item.Setting.ReadCurrentState());
        StatusMessage = _dryRunMode
            ? $"Simulación de reversión total: {result.Message}"
            : $"Reversión total: {result.Message}";
    }

    private static string CleanupItemViewModelFormatBytes(long bytes)
    {
        string[] suf = { "B", "KB", "MB", "GB" };
        double val = bytes;
        int i = 0;
        while (val >= 1024 && i < suf.Length - 1) { val /= 1024; i++; }
        return $"{val:0.##} {suf[i]}";
    }

    private void RefreshHistory()
    {
        if (System.Windows.Application.Current?.MainWindow?.DataContext is MainViewModel main)
            main.BackupHistory.Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public class PanelGroup
{
    public string Title { get; }
    public ObservableCollection<PanelItemViewModel> Items { get; } = new();
    public PanelGroup(string title) => Title = title;
}

public class PanelItemViewModel : INotifyPropertyChanged
{
    public ISetting Setting { get; }
    public string Key => Setting.Key;

    /// <summary>
    /// Nombre visible. Los nombres técnicos (MenuShowDelay, Fast Startup...) se
    /// trasladan al tooltip para que la vista principal use lenguaje simple.
    /// </summary>
    public string Name => Setting.Key switch
    {
        "System.MenuShowDelay"   => "Eliminar el retardo de los menús",
        "System.FastStartup"     => "Desactivar el inicio rápido de Windows",
        _ => Setting.Name
    };

    public string Description => Setting.Description;

    /// <summary>Explicación corta de la jerga (HAGS, DNS…) que aparezca en el nombre o la descripción.</summary>
    public string? JargonHint => Glossary.HintFor(Name, Description);
    public string RealisticEffect => Setting.RealisticEffect;
    public string VerificationStatus => Setting.VerificationStatus;
    public string? DocumentationUrl => Setting.DocumentationUrl;

    /// <summary>Cuándo se nota el cambio en Windows (lenguaje simple, sin tecnicismos).</summary>
    public string EffectTimingText => Setting.EffectTimingText;

    public string EffectTimingTooltip => EffectTiming.Tooltip(Setting.Key);

    /// <summary>Si el efecto se puede medir con «Antes y después» (lenguaje simple).</summary>
    public string MeasurableText => Setting.MeasurableText;

    /// <summary>Etiqueta de verificación para mostrar como chip.</summary>
    public string VerificationChip => string.IsNullOrWhiteSpace(Setting.VerificationStatus)
        ? "Verificación: no disponible"
        : $"Verificación: {Setting.VerificationStatus}";

    /// <summary>Explicación completa de la verificación (tooltip del chip).</summary>
    public string VerificationTooltip => string.IsNullOrWhiteSpace(Setting.VerificationStatus)
        ? "Sin verificación documentada."
        : $"Verificación: {Setting.VerificationStatus}";

    /// <summary>Efecto realista completo (tooltip del chip «Efecto»).</summary>
    public string EffectTooltip => string.IsNullOrWhiteSpace(Setting.RealisticEffect)
        ? "Efecto realista no disponible."
        : $"Efecto realista: {Setting.RealisticEffect}";

    /// <summary>Nombre de la clave técnica; se muestra solo en el tooltip.</summary>
    public string TechnicalTooltip => Setting.Key switch
    {
        "System.FastStartup"     => "Clave técnica: HKLM\\SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Power → HiberbootEnabled (REG_DWORD)",
        "System.MenuShowDelay"   => "Clave técnica: HKCU\\Control Panel\\Desktop → MenuShowDelay (REG_SZ)",
        "System.MouseAcceleration" => "Clave técnica: HKCU\\Control Panel\\Mouse → MouseSpeed, MouseThreshold1, MouseThreshold2 (REG_SZ)",
        "System.Transparency"    => "Clave técnica: HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize → EnableTransparency (REG_DWORD)",
        "Power.ActivePlan"       => "Técnico: PowerSetActiveScheme (powrprof.dll) sobre el GUID del plan activo",
        "Power.Hibernate"        => "Técnico: powercfg /hibernate (archivo hiberfil.sys)",
        "Monitor.RefreshRate"    => "Técnico: EnumDisplaySettings (user32.dll)",
        "Network.Dns"            => "Técnico: WMI Win32_NetworkAdapterConfiguration (métodos de servidores DNS)",
        "Game.GameMode"          => "Clave técnica: HKCU\\Software\\Microsoft\\GameBar → AutoGameModeEnabled (REG_DWORD)",
        "Game.GameBar"           => "Clave técnica: HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\GameDVR → AppCaptureEnabled (REG_DWORD)",
        "Game.WindowedOptimization" => "Clave técnica: HKCU\\Software\\Microsoft\\DirectX → DirectXUserGlobalSettings → SwapEffectUpgradeEnable",
        "Game.Hags"              => "Clave técnica: HKLM\\SYSTEM\\CurrentControlSet\\Control\\GraphicsDrivers → HwSchMode (REG_DWORD)",
        _ when Setting.Key.StartsWith("Startup.", StringComparison.Ordinal)
            => "Clave técnica: …\\Explorer\\StartupApproved\\Run (REG_BINARY, sin borrar la entrada Run)",
        _ => $"Identificador interno del ajuste: {Setting.Key}"
    };

    /// <summary>Explicación del nivel de riesgo (tooltip del chip de riesgo).</summary>
    public string RiskTooltip => RiskLevel switch
    {
        SettingRiskLevel.Safe => "Riesgo seguro: no requiere administrador y se revierte con un clic.",
        SettingRiskLevel.Moderate => "Riesgo moderado: cambia una opción de Windows, se puede revertir.",
        SettingRiskLevel.Advanced => "Riesgo avanzado: requiere confirmación explícita antes de aplicarse.",
        _ => "Nivel de riesgo no definido."
    };

    /// <summary>Hay una URL de documentación real que enlazar.</summary>
    public bool HasSource => !string.IsNullOrWhiteSpace(Setting.DocumentationUrl);

    /// <summary>Los ajustes de solo lectura no muestran interruptor.</summary>
    public bool IsReadOnlySetting => !Setting.IsReversible || ReadOnlyKeys.Contains(Setting.Key);

    /// <summary>
    /// Ajustes que la app no cambia nunca. La frecuencia del monitor es informativa
    /// aunque declare reversibilidad (nunca aplica nada).
    /// </summary>
    private static readonly System.Collections.Generic.HashSet<string> ReadOnlyKeys = new(StringComparer.Ordinal)
    {
        "Monitor.RefreshRate"
    };

    /// <summary>Qué hace el interruptor, en una línea (y cómo volver atrás).</summary>
    public string SwitchActionText
    {
        get
        {
            if (IsReadOnlySetting) return "Solo lectura: la app muestra este estado y nunca lo cambia.";
            if (IsUnavailable) return UnavailableReason;
            if (IsAppliedWithoutBackup) return AppliedWithoutBackupTooltip;

            var text = AppliedEffectText == null
                ? "Activado: aplica el ajuste. Para volver atrás, usa «Revertir»."
                : $"Activado: {AppliedEffectText}. Para volver atrás, usa «Revertir».";

            if (WasChangedOutsideTheApp) text += $" {ManualChangeCheck.WarningText} Al revertir se pedirá confirmación.";
            return text;
        }
    }

    private string? AppliedEffectText => Setting.Key switch
    {
        "System.FastStartup"     => "desactiva el Inicio rápido (apagado completo)",
        "System.MenuShowDelay"   => "baja el retardo de menús a 0 ms",
        "System.Hibernate"       => "desactiva la hibernación",
        "System.MouseAcceleration" => "desactiva la aceleración del ratón",
        "System.Transparency"    => "desactiva los efectos de transparencia",
        "Power.ActivePlan"       => $"cambia al plan «{TargetPlanName}» (en portátiles sube el consumo y la temperatura)",
        "Network.Dns"            => "usa el DNS de Cloudflare",
        "Game.GameMode"          => "activa el Modo de juego de Windows",
        "Game.GameBar"           => "desactiva la captura y transmisión de Game Bar (AppCaptureEnabled = 0)",
        "Game.WindowedOptimization" => "activa SwapEffectUpgradeEnable para juegos en ventana",
        "Monitor.RefreshRate"    => $"intenta la frecuencia máxima del monitor ({SuggestedRefreshText})",
        "Game.Hags"              => "activa la planificación de GPU por hardware (HwSchMode=2)",
        _ when Setting.Key.StartsWith("Startup.", StringComparison.Ordinal)
            => "quita este programa del arranque automático de Windows",
        _ => null
    };

    /// <summary>Plan al que cambia el interruptor, según lo que exista de verdad en el equipo.</summary>
    public string TargetPlanName => Setting is PowerPlanSetting pps ? pps.TargetPlanName : "no disponible";

    /// <summary>El ajuste no está disponible en este equipo (por ejemplo, no hay plan Alto rendimiento).</summary>
    public bool IsUnavailable => Setting is PowerPlanSetting pps && !pps.IsAvailable;

    /// <summary>Motivo mostrado cuando el ajuste no está disponible.</summary>
    public string UnavailableReason => Setting is PowerPlanSetting pps ? pps.UnavailableMessage : string.Empty;

    /// <summary>El interruptor se oculta en los ajustes de solo lectura (que muestran un chip).</summary>
    public bool IsToggleVisible => !IsReadOnlySetting;

    /// <summary>
    /// Estado mostrado por el interruptor: sale de LEER el sistema, no de que exista una copia
    /// guardada. Es encendido cuando el valor actual ya es el valor que aplica el ajuste.
    /// </summary>
    public bool IsAppliedInSystem => Setting.IsAppliedInSystem;

    /// <summary>La app guardó el valor original de este ajuste (lo aplicó ella).</summary>
    public bool IsAppliedByApp => Setting.HasOriginal;

    /// <summary>Windows ya está en el estado aplicado, pero la app no lo cambió: no hay nada que revertir.</summary>
    public bool IsAppliedWithoutBackup => !IsReadOnlySetting && IsAppliedInSystem && !Setting.HasOriginal;

    /// <summary>true si el valor actual ya no es el que aplicó la app (se cambió fuera de ella).</summary>
    public bool WasChangedOutsideTheApp => ManualChangeCheck.WasChangedOutsideTheApp(Setting);

    /// <summary>Etiqueta que explica que Windows ya trae este ajuste activado sin intervención de la app.</summary>
    public string AppliedWithoutBackupText => "Windows ya lo trae activado: no hay nada que cambiar.";

    /// <summary>Explicación de por qué no se puede revertir un ajuste ya aplicado fuera de la app.</summary>
    public string AppliedWithoutBackupTooltip =>
        "Windows ya trae este ajuste activado y la app no lo cambió: no hay nada que cambiar ni ningún valor " +
        "original guardado que restaurar, así que el interruptor queda deshabilitado. Para deshacerlo, cámbialo en Windows.";

    /// <summary>El interruptor se muestra deshabilitado cuando el ajuste no está disponible o no hay nada que revertir.</summary>
    public bool IsToggleEnabled => IsWindowsVersionSupported && !IsUnavailable && !IsAppliedWithoutBackup;

    private string SuggestedRefreshText
    {
        get
        {
            if (Setting is not MonitorRefreshSetting)
                return "no disponible";
            var current = _currentValue;
            if (current is string s && s.Contains("Hz", StringComparison.OrdinalIgnoreCase))
                return s;
            return "según el monitor";
        }
    }

    /// <summary>Símbolo de riesgo: distingue el nivel sin depender solo del color.</summary>
    public string RiskGlyph => RiskLevel switch
    {
        SettingRiskLevel.Safe => "✓",
        SettingRiskLevel.Moderate => "!",
        SettingRiskLevel.Advanced => "⚠",
        _ => "•"
    };
    public SettingRiskLevel RiskLevel => Setting.RiskLevel;
    public bool IsWindowsVersionSupported => Setting.IsWindowsVersionSupported;
    public bool CanRevert => Setting.HasOriginal;
    public bool RequiresConfirmation => Setting.RequiresConfirmation;
    public bool RequiresRestart => Setting.RequiresRestart;
    public bool RequiresAdmin => Setting.RequiresAdmin;

    private object? _currentValue;
    public object? CurrentValue
    {
        get => _currentValue;
        set { _currentValue = value; OnPropertyChanged(); }
    }

    private string _currentValueText = string.Empty;
    public string CurrentValueText
    {
        get => _currentValueText;
        set { _currentValueText = value; OnPropertyChanged(); }
    }

    public string RiskChip
    {
        get
        {
            return RiskLevel switch
            {
                SettingRiskLevel.Safe => "Seguro",
                SettingRiskLevel.Moderate => "Moderado",
                SettingRiskLevel.Advanced => "Avanzado",
                _ => RiskLevel.ToString()
            };
        }
    }

    public string? RequirementChip
    {
        get
        {
            var reqs = new List<string>();
            if (RequiresAdmin) reqs.Add("Administrador");
            if (RequiresRestart) reqs.Add("Reinicio");
            if (RequiresConfirmation) reqs.Add("Confirmación");
            return reqs.Count > 0 ? string.Join(" · ", reqs) : null;
        }
    }

    public bool IsReversible => Setting.IsReversible;

    private readonly PanelViewModel _parent;

    public ICommand ApplyCommand { get; }
    public ICommand RevertCommand { get; }

    /// <summary>Alterna el ajuste: aplica si no está aplicado, revierte si ya lo está.</summary>
    public ICommand ToggleCommand { get; }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(); }
    }

    public PanelItemViewModel(ISetting setting, object? currentValue, PanelViewModel parent, bool isLoading = false)
    {
        Setting = setting;
        _currentValue = currentValue;
        _parent = parent;
        _isLoading = isLoading;
        UpdateCurrentValueText();
        ApplyCommand = new RelayCommand(_ => _parent.ApplyItem(this));
        RevertCommand = new RelayCommand(_ => _parent.RevertItem(this));
        ToggleCommand = new RelayCommand(_ => _parent.ToggleItem(this));
    }

    public void RefreshState(object? currentValue)
    {
        _isLoading = false;
        _currentValue = currentValue;
        UpdateCurrentValueText();
        OnPropertyChanged(nameof(CanRevert));
        OnPropertyChanged(nameof(IsLoading));
        OnPropertyChanged(nameof(SwitchActionText));
        OnPropertyChanged(nameof(IsUnavailable));
        OnPropertyChanged(nameof(UnavailableReason));
        OnPropertyChanged(nameof(IsToggleVisible));
        OnPropertyChanged(nameof(IsToggleEnabled));
        OnPropertyChanged(nameof(TargetPlanName));
        OnPropertyChanged(nameof(IsAppliedInSystem));
        OnPropertyChanged(nameof(IsAppliedByApp));
        OnPropertyChanged(nameof(IsAppliedWithoutBackup));
        OnPropertyChanged(nameof(WasChangedOutsideTheApp));
    }

    private void UpdateCurrentValueText()
    {
        if (_isLoading && _currentValue == null)
        {
            CurrentValueText = "Cargando...";
            return;
        }

        if (Setting is PowerPlanSetting pps)
        {
            CurrentValueText = pps.GetFriendlyName();
            return;
        }

        if (Setting is DnsSetting dns)
        {
            CurrentValueText = dns.GetFriendlyState();
            return;
        }

        if (Setting is MenuShowDelaySetting menuDelay)
        {
            CurrentValueText = menuDelay.GetFriendlyState();
            return;
        }

        if (Setting is FastStartupSetting fastStartup)
        {
            CurrentValueText = fastStartup.GetFriendlyState();
            return;
        }

        if (Setting is GameBarSetting gameBar)
        {
            CurrentValueText = gameBar.GetFriendlyState();
            return;
        }

        if (Setting is StartupProgramSetting startupProgram)
        {
            CurrentValueText = startupProgram.GetFriendlyState();
            return;
        }

        if (_currentValue is bool b)
            CurrentValueText = b ? "Habilitado" : "Deshabilitado";
        else if (_currentValue is string str)
            CurrentValueText = string.IsNullOrEmpty(str) ? "No disponible" : str;
        else if (_currentValue == null)
            CurrentValueText = "No disponible";
        else
            CurrentValueText = _currentValue.ToString() ?? string.Empty;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
