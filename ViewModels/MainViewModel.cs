using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using PC_Optimizer.Core;
using PC_Optimizer.Services;

namespace PC_Optimizer.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    // ── Servicios ──────────────────────────────────────────────────────────────
    private readonly ITuningEngine _tuningEngine;
    private readonly ISystemInfoService _systemInfoService;
    private readonly IAdminService _adminService;
    private readonly IRestorePointService _restorePointService;
    private readonly IStartupService _startupService;
    private readonly ITrayIconService? _trayIconService;
    private readonly IUserSettingsService _userSettings;
    private readonly IDnsService? _dnsService;
    private readonly DispatcherTimer _metricsTimer;
    // En Inicio basta una lectura cada 2 s (menos CPU en reposo); el Panel de ajustes mantiene 1 s.
    private const int HomeMetricsSeconds = 2;

    // ── Estado ─────────────────────────────────────────────────────────────────
    private object? _currentView;
    private bool _isAdmin;
    private bool _isCreatingRestorePoint;
    private bool _dryRunMode;
    private string _statusMessage = string.Empty;

    // ── Propiedades públicas ───────────────────────────────────────────────────
    public object? CurrentView
    {
        get => _currentView;
        set { _currentView = value; OnPropertyChanged(); }
    }

    public bool IsAdmin
    {
        get => _isAdmin;
        set { _isAdmin = value; OnPropertyChanged(); }
    }

    /// <summary>true mientras Windows crea el punto de restauración en segundo plano.</summary>
    public bool IsCreatingRestorePoint
    {
        get => _isCreatingRestorePoint;
        private set { _isCreatingRestorePoint = value; OnPropertyChanged(); }
    }

    public const string CreatingRestorePointText = "Creando punto de restauración...";

    /// <summary>
    /// Tiempo máximo de espera a Windows. Crear un punto suele tardar de unos segundos a medio minuto;
    /// en un disco lento o con mucha actividad puede llegar a uno o dos minutos. Con 120 s la barra
    /// de progreso nunca gira sin tope y casi ningún equipo normal lo supera.
    /// </summary>
    public static readonly TimeSpan DefaultRestorePointTimeout = RestorePointCoordinator.DefaultTimeout;

    /// <summary>Un solo coordinador para el botón y la Optimización guiada: mismo límite y mismos mensajes.</summary>
    private readonly RestorePointCoordinator _restoreCoordinator = new();

    public TimeSpan RestorePointTimeout
    {
        get => _restoreCoordinator.Timeout;
        set => _restoreCoordinator.Timeout = value;
    }

    public bool DryRunMode
    {
        get => _dryRunMode;
        set
        {
            if (_dryRunMode == value) return;
            _dryRunMode = value;
            _userSettings.Settings.DryRunMode = value;
            _userSettings.Save();
            Panel.DryRunMode = value;
            Cleanup.DryRunMode = value;
            Profile.DryRunMode = value;
            BackupHistory.DryRunMode = value;
            Guided.DryRunMode = value;
            OnPropertyChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    // ── Estado de navegación (solo presentación) ───────────────────────────────
    private string _activeSection = "Home";

    /// <summary>Etiqueta de la sección activa en la barra lateral.</summary>
    public string ActiveSection
    {
        get => _activeSection;
        private set
        {
            if (_activeSection == value) return;
            _activeSection = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SectionTitle));
            OnPropertyChanged(nameof(SectionSubtitle));
            OnPropertyChanged(nameof(SectionLabel));
            OnPropertyChanged(nameof(SectionGroup));
            OnPropertyChanged(nameof(SectionNumber));
            OnPropertyChanged(nameof(SectionEyebrow));
        }
    }

    /// <summary>Título grande del encabezado de la sección.</summary>
    public string SectionTitle => ActiveSection switch
    {
        "Home"          => "Inicio",
        "Cleanup"       => "Limpieza",
        "Diagnostics"   => "Mi PC",
        "Guided"        => "Mejorar mi PC",
        "Measurement"   => "Resultados",
        "Profile"       => "Configuraciones listas",
        "BackupHistory" => "Deshacer cambios",
        "License"       => "Licencia",
        _               => "Ajustes uno por uno"
    };

    /// <summary>Subtítulo corto del encabezado de la sección.</summary>
    public string SectionSubtitle => ActiveSection switch
    {
        "Home"          => "Analiza tu PC, revisa lo recomendado y comprueba el resultado. Nada se aplica sin que tú lo elijas y lo confirmes.",
        "Cleanup"       => "Se muestra primero qué se va a borrar. Lo que borres no se puede recuperar.",
        "Diagnostics"   => "Qué consume tu PC y cómo está tu almacenamiento, sin cambiar nada.",
        "Guided"        => "Marca lo que quieras aplicar y confirma: antes se intenta crear un punto de restauración. La limpieza pide su propia confirmación.",
        "Measurement"   => "Compara cómo estaba tu PC antes y después de un cambio, con resultados reales.",
        "Profile"       => "Conjuntos de ajustes listos para tu forma de usar el PC. Tú decides cuál aplicar.",
        "BackupHistory" => "Todo lo que cambió la app y cómo revertir los ajustes. Los archivos borrados no se pueden recuperar.",
        "License"       => "Se comprueba sin internet. Sin licencia la app no aplica cambios nuevos, pero Deshacer cambios sigue funcionando.",
        _               => "Cada ajuste por separado, con su riesgo y datos reales de tu equipo."
    };

    /// <summary>Grupo del menú al que pertenece la sección (primer tramo de la migaja).</summary>
    public string SectionGroup => ActiveSection switch
    {
        "Home"                      => "Empezar",
        "Guided" or "Cleanup"       => "Mejorar",
        "Measurement" or "BackupHistory" => "Resultados",
        "License"                   => "Cuenta",
        _                           => "Explorar y ajustar"
    };

    /// <summary>Migaja de pan (último tramo).</summary>
    public string SectionLabel => ActiveSection switch
    {
        "Home"          => "Inicio",
        "Cleanup"       => "Limpieza",
        "Diagnostics"   => "Mi PC",
        "Guided"        => "Mejorar mi PC",
        "Measurement"   => "Resultados",
        "Profile"       => "Configuraciones listas",
        "BackupHistory" => "Deshacer cambios",
        "License"       => "Licencia",
        _               => "Ajustes uno por uno"
    };

    public bool StartupEnabled
    {
        get => _startupService.IsEnabled;
        set
        {
            if (value == _startupService.IsEnabled) return;
            if (value) _startupService.Enable(); else _startupService.Disable();
            OnPropertyChanged();
            _userSettings.Settings.StartupEnabled = _startupService.IsEnabled;
            _userSettings.Save();
        }
    }

    // ── Sub-ViewModels ─────────────────────────────────────────────────────────
    public HomeViewModel Home { get; private set; }
    public ToastManager Toasts { get; private set; } = null!;

    private readonly DateTime _sessionStart = DateTime.Now;
    private string _sessionText = string.Empty;
    /// <summary>«Esta sesión: N aplicados · M revertidos», calculado del registro real del motor.</summary>
    public string SessionText { get => _sessionText; private set { if (_sessionText == value) return; _sessionText = value; OnPropertyChanged(); } }

    /// <summary>Vuelve a leer todos los enlaces (tras asignar el DataContext de la ventana).</summary>
    public void RaiseAllChanged() => OnPropertyChanged(string.Empty);

    public void RefreshSession()
    {
        var counts = SessionStats.Compute(_tuningEngine.GetChangeLog(), _sessionStart);
        SessionText = counts.Text;
        if (Home != null) Home.Session = counts;
    }

    /// <summary>Entradas de la paleta (Ctrl+K): las pantallas del menú y los ajustes por nombre. Solo para navegar.</summary>
    public System.Collections.Generic.IReadOnlyList<PaletteEntry> BuildPaletteEntries()
    {
        var list = new System.Collections.Generic.List<PaletteEntry>
        {
            new("Inicio", "Empezar · tres pasos", PaletteKind.Screen, "Home"),
            new("Mejorar mi PC", "Mejorar · analizar y aplicar lo recomendado", PaletteKind.Screen, "Guided"),
            new("Limpieza", "Mejorar · archivos temporales", PaletteKind.Screen, "Cleanup"),
            new("Mi PC", "Explorar y ajustar · qué consume tu PC", PaletteKind.Screen, "Diagnostics"),
            new("Ajustes uno por uno", "Explorar y ajustar · todos los ajustes", PaletteKind.Screen, "Panel"),
            new("Configuraciones listas", "Explorar y ajustar · conjuntos de ajustes", PaletteKind.Screen, "Profile"),
            new("Resultados", "Resultados · antes y después", PaletteKind.Screen, "Measurement"),
            new("Deshacer cambios", "Resultados · historial y reversión", PaletteKind.Screen, "BackupHistory"),
        };
        foreach (var group in Panel.Groups)
            foreach (var item in group.Items)
                list.Add(new PaletteEntry(item.Name, $"Ajuste · {group.Title}", PaletteKind.Setting, "Panel", item.Setting.Key));
        return list;
    }

    /// <summary>Elegir una entrada solo NAVEGA a su pantalla; nunca aplica un ajuste.</summary>
    public void OpenPaletteEntry(PaletteEntry entry) => NavigateTo(entry.Section);

    /// <summary>Línea «//» sobre el título: el grupo del menú en mayúsculas.</summary>
    public string SectionEyebrow => SectionGroup.ToUpperInvariant();

    /// <summary>Numeración tipo «03 / 08» según el orden del menú.</summary>
    public string SectionNumber => ActiveSection switch
    {
        "Home" => "01 / 08", "Guided" => "02 / 08", "Cleanup" => "03 / 08", "Diagnostics" => "04 / 08",
        "Panel" => "05 / 08", "Profile" => "06 / 08", "Measurement" => "07 / 08", "BackupHistory" => "08 / 08",
        "License" => "LIC",
        _ => "05 / 08"
    };
    public PanelViewModel Panel { get; }
    public CleanupViewModel Cleanup { get; }
    public DiagnosticsViewModel Diagnostics { get; }
    public GuidedOptimizationViewModel Guided { get; }
    public MeasurementViewModel Measurement { get; }
    public ProfileViewModel Profile { get; }
    public BackupHistoryViewModel BackupHistory { get; }
    /// <summary>Pantalla Licencia; null cuando la ventana se crea sin control de licencia (pruebas).</summary>
    public LicenseViewModel? License { get; }

    // ── Comandos ───────────────────────────────────────────────────────────────
    public ICommand RestartAsAdminCommand { get; }
    public ICommand CreateRestorePointCommand { get; }
    public ICommand NavigateToCommand { get; }

    // ── Constructor principal (para la app real) ───────────────────────────────
    public MainViewModel(ITuningEngine tuningEngine,
                         ISystemInfoService systemInfoService,
                         IAdminService adminService,
                         IRestorePointService restorePointService)
        : this(tuningEngine, systemInfoService, adminService, restorePointService,
               new StartupService(
                   new RegistryRunKey(
                       Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                           @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true)!),
                   Environment.ProcessPath ?? string.Empty),
               trayIconService: null,
               userSettings: null,
               dnsService: null)
    { }

    // ── Constructor completo (inyección de dependencias / pruebas) ─────────────
    public MainViewModel(ITuningEngine tuningEngine,
                         ISystemInfoService systemInfoService,
                         IAdminService adminService,
                         IRestorePointService restorePointService,
                         IStartupService startupService,
                         ITrayIconService? trayIconService = null,
                         IUserSettingsService? userSettings = null,
                         IDnsService? dnsService = null,
                         IDiskCleanupService? cleanupService = null,
                         IProcessUsageProvider? processUsageProvider = null,
                         IStorageDiagnosticsService? storageDiagnostics = null,
                         IStartupProgramService? startupPrograms = null,
                         IGuidedAnalysisSource? guidedAnalysisSource = null,
                         IDeliveryOptimizationService? deliveryOptimization = null,
                         IMeasurementService? measurementService = null,
                         IBootBaselineStore? bootBaselineStore = null,
                         IBootHistoryService? bootHistoryService = null,
                         IMemoryDiagnosticsService? memoryDiagnostics = null,
                         INetworkDiagnosticsService? networkDiagnostics = null,
                         IDeviceInfoService? deviceInfoService = null,
                         Services.Licensing.LicenseService? licenseService = null)
    {
        _tuningEngine       = tuningEngine;
        _systemInfoService  = systemInfoService;
        _adminService       = adminService;
        _restorePointService = restorePointService;
        _startupService     = startupService;
        _trayIconService    = trayIconService;
        _userSettings       = userSettings ?? new UserSettingsService();
        _dnsService         = dnsService;

        IsAdmin     = _adminService.IsRunningAsAdmin;
        _dryRunMode = _userSettings.Settings.DryRunMode;

        // ── Comandos ──────────────────────────────────────────────────────────
        RestartAsAdminCommand    = new RelayCommand(_ => ExecuteRestartAsAdmin());
        CreateRestorePointCommand = new RelayCommand(_ => _ = CreateRestorePointAsync(), _ => !IsCreatingRestorePoint);
        NavigateToCommand        = new RelayCommand(param => NavigateTo(param?.ToString() ?? "Panel"));

        // ── Sub-ViewModels ────────────────────────────────────────────────────
        Panel       = new PanelViewModel(_tuningEngine, _systemInfoService, _dryRunMode, deviceInfoService);
        Panel.NavigationRequested = NavigateTo;
        Cleanup = new CleanupViewModel(_tuningEngine, cleanupService ?? new DiskCleanupService(), Environment.GetEnvironmentVariable("TEMP") ?? System.IO.Path.GetTempPath(), _dryRunMode);
        Cleanup.RestartAsAdminRequested = ExecuteRestartAsAdmin;
        Diagnostics = new DiagnosticsViewModel(
            processUsageProvider ?? new SystemProcessUsageProvider(),
            storageDiagnostics ?? new SystemStorageDiagnosticsService(),
            () => IsAdmin,
            startupPrograms,
            bootHistoryService ?? new SystemBootHistoryService(),
            memoryDiagnostics ?? new SystemMemoryDiagnosticsService(),
            networkDiagnostics ?? new SystemNetworkDiagnosticsService());
        Diagnostics.NavigationRequested = NavigateTo;
        Measurement = new MeasurementViewModel();
        Profile     = new ProfileViewModel(_tuningEngine, _dryRunMode);
        BackupHistory = new BackupHistoryViewModel(_tuningEngine) { DryRunMode = _dryRunMode };
        if (licenseService != null)
            License = new LicenseViewModel(licenseService, pickFile: PickLicenseFile, copyToClipboard: t => System.Windows.Clipboard.SetText(t));
        var guidedCleanup = cleanupService ?? new DiskCleanupService();
        var guidedDelivery = deliveryOptimization ?? new SystemDeliveryOptimizationService(guidedCleanup);
        var guidedStartup = startupPrograms ?? new StartupProgramService();
        var guidedDns = dnsService ?? new DnsService();
        Guided = new GuidedOptimizationViewModel(_tuningEngine,
            guidedAnalysisSource ?? new SystemGuidedAnalysisSource(guidedCleanup, guidedDelivery, guidedStartup,
                guidedDns, new SystemMeasurementProvider()),
            _restorePointService, measurementService ?? new MeasurementService(new SystemMeasurementProvider()),
            guidedCleanup, guidedDelivery, () => IsAdmin, baseline: bootBaselineStore,
            restartAsAdmin: ExecuteRestartAsAdmin, restoreCoordinator: _restoreCoordinator)
        { DryRunMode = _dryRunMode };

        Toasts = new ToastManager();
        Panel.Toasts = Toasts;
        Toasts.Changed += RefreshSession;
        Home = new HomeViewModel(NavigateTo, Guided, Panel);
        RefreshSession();
        CurrentView = Home;

        // La recolección de WMI se delega al hilo de fondo y Panel evita solapar ciclos.
        _metricsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(HomeMetricsSeconds) };
        // Las métricas solo se muestran en el Panel: fuera de él no se consultan (ver NavigateTo).
        _metricsTimer.Tick += (_, _) => { if (ActiveSection is "Panel" or "Home") { Panel.RefreshMetrics(); RefreshSession(); } };
        _metricsTimer.Start();
    }

    // ── Navegación ─────────────────────────────────────────────────────────────
    public void NavigateTo(string? viewName)
    {
        var navClock = NavTiming.Enabled ? Stopwatch.StartNew() : null;
        // Principio 3: al volver al Panel se vuelve a leer el estado real de todos los ajustes.
        if (viewName is null or "Panel") Panel.LoadStatesAsync();
        if (viewName == "Cleanup") Cleanup.Refresh();
        if (viewName == "BackupHistory") BackupHistory.Refresh();
        if (viewName == "Guided") _ = Guided.AnalyzeAsync();
        if (viewName == "Home") Home.Refresh();

        // Diagnóstico solo se actualiza mientras su pantalla está visible.
        if (viewName == "Diagnostics") Diagnostics.Start();
        else Diagnostics.Stop();

        ActiveSection = viewName is "Home" or "Cleanup" or "Diagnostics" or "Measurement" or "Profile" or "BackupHistory" or "Guided" or "License" ? viewName : "Panel";
        _metricsTimer.Interval = TimeSpan.FromSeconds(ActiveSection == "Home" ? HomeMetricsSeconds : 1);
        if (ActiveSection is "Panel" or "Home") Panel.RefreshMetrics(); // al volver, datos frescos de inmediato
        RefreshSession();
        CurrentView = viewName switch
        {
            "Home"         => Home,
            "Panel"        => Panel,
            "Cleanup"      => Cleanup,
            "Diagnostics"  => Diagnostics,
            "Measurement"  => Measurement,
            "Profile"      => Profile,
            "BackupHistory" => BackupHistory,
            "License"      => (object?)License ?? Panel,
            "Guided" => Guided,
            _              => Panel
        };
        if (navClock != null) _ = NavTiming.MeasureAsync(viewName ?? "Panel", navClock);
    }

    /// <summary>Selector de archivo .lic; devuelve su texto o null si se cancela o no se puede leer.</summary>
    private static string? PickLicenseFile()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Cargar licencia", Filter = "Licencia (*.lic)|*.lic|Todos los archivos|*.*" };
        if (dlg.ShowDialog() != true) return null;
        try { return new System.IO.FileInfo(dlg.FileName).Length > 8192 ? null : System.IO.File.ReadAllText(dlg.FileName); }
        catch { return null; }
    }

    public void SetMetricsPaused(bool paused)
    {
        if (paused)
        {
            _metricsTimer.Stop();
            Diagnostics.Stop();
        }
        else
        {
            Panel.RefreshMetrics();
            _metricsTimer.Start();
            if (ActiveSection == "Diagnostics") Diagnostics.Start();
        }
    }

    // ── Lógica de comandos ─────────────────────────────────────────────────────
    private void ExecuteRestartAsAdmin()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                StatusMessage = "No se pudo determinar la ruta del ejecutable.";
                return;
            }
            Process.Start(new ProcessStartInfo { FileName = exePath, Verb = "runas", UseShellExecute = true });
            System.Windows.Application.Current?.Shutdown();
        }
        catch (Exception ex)
        {
            StatusMessage = $"No se pudo reiniciar como administrador: {PC_Optimizer.Services.UserErrorText.Of(ex)}";
        }
    }

    /// <summary>
    /// Crea el punto de restauración en segundo plano: la ventana sigue respondiendo, el botón queda
    /// desactivado y se muestra un indicador. En modo simulación no toca Windows.
    /// </summary>
    public async Task CreateRestorePointAsync()
    {
        if (IsCreatingRestorePoint) return;

        // Principio 6: en modo simulación no se cambia nada, tampoco el sistema de restauración.
        if (_dryRunMode)
        {
            StatusMessage = "Modo simulación: no se crea ningún punto de restauración ni se cambia Windows.";
            return;
        }

        if (!_restorePointService.IsSupported)
        {
            StatusMessage = RestorePointMessages.NotSupported;
            return;
        }

        if (!IsAdmin)
        {
            StatusMessage = RestorePointMessages.NeedsAdmin;
            return;
        }

        // Un intento anterior que se quedó esperando a Windows y todavía no terminó: no se lanza otro encima.
        if (_restoreCoordinator.IsRunning)
        {
            StatusMessage = RestorePointMessages.StillRunning;
            return;
        }

        IsCreatingRestorePoint = true;
        StatusMessage = CreatingRestorePointText;
        RefreshCommandStates();
        try
        {
            var result = await _restoreCoordinator.CreateAsync(_restorePointService, "Phantasm Optimizer - antes de aplicar ajustes");
            StatusMessage = result.Outcome switch
            {
                RestorePointOutcome.Created => $"✔ {result.Message}",
                RestorePointOutcome.TooSoon => $"ℹ {result.Message}",
                RestorePointOutcome.StillRunning => result.Message,
                _ => $"✖ {result.Message}"
            };
        }
        catch (Exception ex)
        {
            StatusMessage = $"✖ {RestorePointMessages.FromException(ex).Message}";
        }
        finally
        {
            IsCreatingRestorePoint = false;
            RefreshCommandStates();
        }
    }

    private static void RefreshCommandStates()
    {
        try { CommandManager.InvalidateRequerySuggested(); }
        catch { /* sin interfaz (pruebas) */ }
    }

    // ── INotifyPropertyChanged ────────────────────────────────────────────────
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
