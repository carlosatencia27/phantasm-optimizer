using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using PC_Optimizer.Core;
using PC_Optimizer.Models;
using PC_Optimizer.Services;

namespace PC_Optimizer;

public partial class MainWindow : Window
{
    private ITrayIconService? _trayIconService;
    private bool _isClosing;
    private bool _stateInitialized;

    /// <summary>
    /// El tamaño inicial nunca supera el área de trabajo: en un portátil de 1366x768 con escala del
    /// 150 % solo caben unos 910x510 puntos y una ventana de 1240x780 se saldría de la pantalla.
    /// </summary>
    private void FitToWorkArea()
    {
        var area = SystemParameters.WorkArea;
        Width = Math.Min(Width, area.Width);
        Height = Math.Min(Height, area.Height);
    }

    public MainWindow()
    {
        InitializeComponent();
        FitToWorkArea();
        ApplyDarkTitleBar();

        StateChanged += OnStateChanged;
        // Sin foco no hay nadie mirando las métricas: se pausa la lectura y se retoma al volver.
        Deactivated += (_, _) => { if (DataContext is ViewModels.MainViewModel vm) vm.SetMetricsPaused(true); };
        Activated += (_, _) => { if (DataContext is ViewModels.MainViewModel vm && IsVisible && WindowState != WindowState.Minimized) vm.SetMetricsPaused(false); };
        Closing      += OnClosing;
        KeyDown      += OnWindowKeyDown;

        // La ventana se muestra primero: la construcción de servicios, la lectura del sistema y la
        // bandeja se aplazan al final de la cola de mensajes (misma lógica, solo más tarde).
        Loaded += OnLoadedDeferState;
    }

    /// <summary>
    /// Trabajo que no hace falta para que la ventana exista (motores, servicios WMI, enumeración de
    /// programas de inicio, bandeja y estado inicial). Se ejecuta una sola vez, tras el primer
    /// renderizado, para que el arranque visible no espere por él.
    /// </summary>
    /// <summary>
    /// RePág/AvPág, flechas arriba/abajo e Inicio/Fin desplazan la página aunque ningún control tenga
    /// el foco, salvo cuando se escribe en un cuadro de texto o se usa una lista desplegable.
    /// </summary>
    private void OnWindowKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // Ctrl+K: paleta de comandos (solo navega). Funciona también con el foco en un cuadro de texto.
        if (e.Key == System.Windows.Input.Key.K && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control
            && DataContext is ViewModels.MainViewModel paletteVm)
        {
            if (Palette.IsOpen) Palette.Close();
            else Palette.Open(paletteVm.BuildPaletteEntries(), paletteVm.OpenPaletteEntry);
            e.Handled = true;
            return;
        }
        if (Palette.IsOpen) return;
        if (e.Handled || System.Windows.Input.Keyboard.Modifiers != System.Windows.Input.ModifierKeys.None) return;
        // Cuadros de texto y listas con selección se quedan con sus teclas (ver ScrollKeyPolicy).
        if (Views.ScrollKeyPolicy.ControlOwnsKeys(System.Windows.Input.Keyboard.FocusedElement)) return;

        var sv = ContentScroll;
        double page = Math.Max(48, sv.ViewportHeight * 0.9);
        switch (e.Key)
        {
            case System.Windows.Input.Key.PageDown: sv.ScrollToVerticalOffset(sv.VerticalOffset + page); break;
            case System.Windows.Input.Key.PageUp:   sv.ScrollToVerticalOffset(sv.VerticalOffset - page); break;
            case System.Windows.Input.Key.Down:     sv.ScrollToVerticalOffset(sv.VerticalOffset + 48); break;
            case System.Windows.Input.Key.Up:       sv.ScrollToVerticalOffset(sv.VerticalOffset - 48); break;
            case System.Windows.Input.Key.Home:     sv.ScrollToTop(); break;
            case System.Windows.Input.Key.End:      sv.ScrollToBottom(); break;
            default: return;
        }
        e.Handled = true;
    }

    private void OnLoadedDeferState(object? sender, RoutedEventArgs e)
    {
        Loaded -= OnLoadedDeferState;
        Dispatcher.BeginInvoke(new Action(BuildApplicationState), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void BuildApplicationState()
    {
        if (_stateInitialized) return;
        _stateInitialized = true;

        // Licencias apagadas por defecto (LicenseKeys.Enforced = false): sin servicio, sin bloqueo y sin pantalla.
        var licenseService = PC_Optimizer.Services.Licensing.LicenseKeys.Enforced
            ? new PC_Optimizer.Services.Licensing.LicenseService(
                new PC_Optimizer.Services.Licensing.LicenseFileStore(), new PC_Optimizer.Services.Licensing.WindowsMachineIdSource(),
                typeof(App).Assembly.GetName().Version ?? new Version(1, 0, 0))
            : null;
        var tuningEngine = new TuningEngine(new RestorePointService(), new AppActivityLogger(),
            new HistoryStore(), new SystemNotifier(),
            licenseGate: licenseService);
        var systemInfoService = new SystemInfoService();
        var adminService = new AdminService();
        var restorePointService = new RestorePointService();
        var cleanupService = new DiskCleanupService();
        var dnsService = new DnsService();
        var deviceInfo = new SystemDeviceInfoService();   // tipo de equipo y batería (solo lectura)
        var gpuInfo = new SystemGpuInfoService();         // GPU detectadas (solo lectura)

        // ── Juego / Game Bar ───────────────────────────────────────────────────
        var gameBarReg  = new GameRegistryService(Microsoft.Win32.Registry.CurrentUser, @"Software\Microsoft\GameBar");
        var gameDvrReg  = new GameRegistryService(Microsoft.Win32.Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR");
        var directXReg  = new GameRegistryService(Microsoft.Win32.Registry.CurrentUser, @"Software\Microsoft\DirectX\UserGpuPreferences");
        tuningEngine.RegisterSetting(new GameModeSetting(gameBarReg));
        tuningEngine.RegisterSetting(new GameBarSetting(gameDvrReg));
        tuningEngine.RegisterSetting(new WindowedGameSetting(directXReg));
        tuningEngine.RegisterSetting(new HagsSetting(new HagsService(), gpuInfo));

        // ── Red ────────────────────────────────────────────────────────────────
        tuningEngine.RegisterSetting(new DnsSetting(dnsService, new WindowsDnsCacheService(), new AppActivityLogger(), deviceInfo));

        // ── Sistema ────────────────────────────────────────────────────────────
        tuningEngine.RegisterSetting(new PowerPlanSetting(new PowerPlanService(), deviceInfo));
        tuningEngine.RegisterSetting(new MonitorRefreshSetting(new MonitorService()));

        var desktopReg = new GameRegistryService(Microsoft.Win32.Registry.CurrentUser, @"Control Panel\Desktop");
        var fastStartupReg = new GameRegistryService(Microsoft.Win32.Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Power");

        tuningEngine.RegisterSetting(new MenuShowDelaySetting(desktopReg));
        tuningEngine.RegisterSetting(new FastStartupSetting(fastStartupReg));
        tuningEngine.RegisterSetting(new HibernateSetting(new HibernateService(), deviceInfo));
        tuningEngine.RegisterSetting(new MouseAccelerationSetting(new MouseService()));
        var personalizationReg = new GameRegistryService(Microsoft.Win32.Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        tuningEngine.RegisterSetting(new TransparencySetting(personalizationReg));

        // ── Programas de inicio ────────────────────────────────────────────────
        var startupProgramService = new StartupProgramService();
        foreach (var program in startupProgramService.GetStartupPrograms())
            tuningEngine.RegisterSetting(new StartupProgramSetting(startupProgramService, program));

        // ── Servicio de inicio con Windows ─────────────────────────────────────
        var runKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
        var startupService = new StartupService(
            new RegistryRunKey(runKey!),
            Environment.ProcessPath ?? string.Empty);

        // ── Bandeja de sistema ─────────────────────────────────────────────────
        var trayIcon = new TrayIconService(
            onShow: () => { Show(); WindowState = WindowState.Normal; Activate(); if (DataContext is ViewModels.MainViewModel vm) vm.SetMetricsPaused(false); },
            onExit: () => { _isClosing = true; Close(); });
        _trayIconService = trayIcon;
        trayIcon.Show();

        // ── DataContext ────────────────────────────────────────────────────────
        DataContext = new ViewModels.MainViewModel(
            tuningEngine, systemInfoService, adminService, restorePointService,
            startupService, trayIcon, null, dnsService, cleanupService,
            new SystemProcessUsageProvider(), new SystemStorageDiagnosticsService(), startupProgramService,
            deviceInfoService: deviceInfo, licenseService: licenseService);

        // «Ajustes uno por uno» es la vista más pesada de construir: se prepara cuando la interfaz está
        // inactiva, para que la primera visita no tarde.
        if (DataContext is ViewModels.MainViewModel preloadVm && !ScreenCapture.Requested)
            Dispatcher.BeginInvoke(new Action(() => ViewHost.Prewarm(preloadVm.Panel)), System.Windows.Threading.DispatcherPriority.ApplicationIdle);

        (DataContext as ViewModels.MainViewModel)?.RaiseAllChanged();

        if (NavTiming.BenchRequested && DataContext is ViewModels.MainViewModel benchVm)
            _ = NavTiming.RunBenchAsync(benchVm.NavigateTo);

        if (ScreenCapture.Requested && DataContext is ViewModels.MainViewModel shotVm)
            _ = ScreenCapture.RunAsync(this, shotVm.NavigateTo,
                () => shotVm.Toasts.Show("Ajuste activado: Modo de juego", PC_Optimizer.Services.ToastKind.Success, null, simulated: true));
    }

    /// <summary>
    /// Pinta la barra de título de esta ventana en oscuro (DWMWA_USE_IMMERSIVE_DARK_MODE).
    /// Solo afecta a esta ventana y, si la API no está disponible, se ignora en silencio:
    /// no se modifica ningún ajuste del sistema.
    /// </summary>
    private void ApplyDarkTitleBar()
    {
        try
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
            if (handle == IntPtr.Zero) return;

            int enabled = 1;
            // Atributo 20 en Windows 10 (build 18985+) y 19 en versiones anteriores de 1809-19041.
            int result = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int));
            if (result != 0)
                DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeLegacy, ref enabled, sizeof(int));
        }
        catch
        {
            // Respaldo silencioso: si dwmapi no está, la barra de título conserva el tema del sistema.
        }
    }

    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeLegacy = 19;

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Navegación lateral: los RadioButton de la barra lateral usan NavigateToCommand.</summary>
    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            if (DataContext is ViewModels.MainViewModel vm) vm.SetMetricsPaused(true);
            Hide();
            _trayIconService?.Show();
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_isClosing)
        {
            // Si el estado todavía no se construyó no hay icono de bandeja: se cierra de verdad para
            // que la app no quede invisible sin forma de volver a abrirla.
            if (!_stateInitialized) return;

            if (DataContext is ViewModels.MainViewModel vm) vm.SetMetricsPaused(true);
            e.Cancel = true;
            Hide();
            _trayIconService?.Show();
        }
    }

    /// <summary>Cicla automáticamente por las 4 pestañas para verificar enlaces y plantillas.</summary>
    public async System.Threading.Tasks.Task CycleTabsAsync()
    {
        App.LogStartupMessage("Iniciando ciclo automático de todas las secciones para auditoría de enlaces...");
        var tabs = new[] { "Panel", "Diagnostics", "Guided", "Cleanup", "Measurement", "Profile", "BackupHistory", "Panel" };
        foreach (var tag in tabs)
        {
            await System.Threading.Tasks.Task.Delay(500);
            if (DataContext is ViewModels.MainViewModel vm)
            {
                vm.NavigateTo(tag);
                App.LogStartupMessage($"Pestaña cargada: {tag}");
            }
        }
        App.LogStartupMessage("Ciclo de pestañas finalizado exitosamente.");
    }

    protected override void OnClosed(EventArgs e)
    {
        _trayIconService?.Dispose();
        base.OnClosed(e);

        // «Salir» del menú de la bandeja debe terminar el proceso por completo (sin residuales),
        // aunque alguna ventana oculta o temporizador siguiera vivo.
        if (!_isClosing) return;
        System.Windows.Application.Current?.Shutdown();
    }
}
