using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace PC_Optimizer;

/// <summary>
/// Listener para registrar advertencias y errores de enlaces WPF (PresentationTraceSources) en startup.log.
/// </summary>
public class BindingTraceListener : TraceListener
{
    private readonly string _logPath;
    public BindingTraceListener(string logPath) => _logPath = logPath;

    public override void Write(string? message)
    {
        if (string.IsNullOrEmpty(message)) return;
        try { File.AppendAllText(_logPath, message); } catch { }
    }

    public override void WriteLine(string? message)
    {
        if (string.IsNullOrEmpty(message)) return;
        try
        {
            File.AppendAllText(_logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [BINDING ERROR/WARN] {message}{Environment.NewLine}");
        }
        catch { }
    }
}

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : global::System.Windows.Application
{
    /// <summary>
    /// Mutex con nombre fijo para que el instalador y el desinstalador de Inno Setup (AppMutex)
    /// detecten que la app está abierta. No controla instancias únicas: solo anuncia la presencia.
    /// </summary>
    public const string AppMutexName = "PCOptimizerAppMutex";
    private Mutex? _appMutex;

    private static readonly string LogFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PC Optimizer",
        "error.log");

    public static readonly string StartupLogFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PC Optimizer",
        "startup.log");

    private static void EnsureLogDirectory()
    {
        try
        {
            var dir = Path.GetDirectoryName(LogFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }
        catch { }
    }

    public static void WriteLog(string message)
    {
        try
        {
            EnsureLogDirectory();
            File.AppendAllText(LogFilePath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch { }
    }

    public static void LogStartupMessage(string message)
    {
        try
        {
            EnsureLogDirectory();
            File.AppendAllText(StartupLogFilePath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch { }
    }

    private static void WriteStartupLog()
    {
        try
        {
            EnsureLogDirectory();
            File.AppendAllText(LogFilePath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Iniciando Phantasm Optimizer (Carpeta: {AppContext.BaseDirectory}){Environment.NewLine}");
        }
        catch { }
    }

    private static void InitializeBindingListener()
    {
        try
        {
            EnsureLogDirectory();
            File.WriteAllText(StartupLogFilePath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] === Inicio de sesión de diagnóstico de enlaces WPF ==={Environment.NewLine}");

            PresentationTraceSources.Refresh();
            PresentationTraceSources.DataBindingSource.Listeners.Add(new BindingTraceListener(StartupLogFilePath));
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning | SourceLevels.Error;
            LogStartupMessage("PresentationTraceSources.DataBindingSource configurado con nivel Warning | Error.");
        }
        catch (Exception ex)
        {
            WriteLog($"No se pudo inicializar BindingTraceListener: {ex.Message}");
        }
    }

    public App()
    {
        WriteStartupLog();
        Dispatcher.UnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnTaskSchedulerUnobservedTaskException;
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteLog($"EXCEPCIÓN NO CONTROLADA (Dispatcher): {e.Exception}");
        LogStartupMessage($"EXCEPCIÓN NO CONTROLADA (Dispatcher): {e.Exception}");
        try
        {
            System.Windows.MessageBox.Show(
                $"Ocurrió un error inesperado en la aplicación:\n\n{PC_Optimizer.Services.UserErrorText.Of(e.Exception)}\n\nLos detalles se guardaron en:\n{LogFilePath}",
                "Phantasm Optimizer - Error inesperado",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
        catch { }
        e.Handled = true;
    }

    private static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString() ?? "desconocido");
        WriteLog($"EXCEPCIÓN NO CONTROLADA (AppDomain): {ex}");
        LogStartupMessage($"EXCEPCIÓN NO CONTROLADA (AppDomain): {ex}");
        try
        {
            System.Windows.MessageBox.Show(
                $"Ocurrió un error inesperado en la aplicación:\n\n{PC_Optimizer.Services.UserErrorText.Of(ex)}\n\nLos detalles se guardaron en:\n{LogFilePath}",
                "Phantasm Optimizer - Error inesperado",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
        catch { }
    }

    private static void OnTaskSchedulerUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteLog($"EXCEPCIÓN NO OBSERVADA (Task): {e.Exception}");
        LogStartupMessage($"EXCEPCIÓN NO OBSERVADA (Task): {e.Exception}");
        e.SetObserved();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        PC_Optimizer.Services.UserErrorText.Logger = new PC_Optimizer.Services.AppActivityLogger();

        // Se crea (sin reclamar propiedad) para que el instalador sepa que la app está abierta.
        try { _appMutex = new Mutex(initiallyOwned: false, AppMutexName); }
        catch { /* si no se puede crear, la app funciona igual */ }

        // Movimiento reducido: si Windows tiene las animaciones desactivadas, las de la app pasan a 0 ms.
        PC_Optimizer.Services.Motion.ApplyTo(Resources);
        // Pinceles y geometrías de los recursos: congelados (más baratos de dibujar).
        foreach (var dict in Resources.MergedDictionaries)
            foreach (var value in dict.Values)
                if (value is System.Windows.Freezable f && f.CanFreeze && !f.IsFrozen) f.Freeze();
        PC_Optimizer.Views.ScrollWheelForwarding.Register();
        InitializeBindingListener();
        WriteLog("Iniciando Phantasm Optimizer.");
        LogStartupMessage("Iniciando Phantasm Optimizer.");

        try
        {
            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
            LogStartupMessage("MainWindow instanciada y mostrada correctamente.");

            if (e.Args.Length > 0 && Array.Exists(e.Args, a => a.Equals("--cycle-tabs", StringComparison.OrdinalIgnoreCase)))
            {
                _ = mainWindow.CycleTabsAsync();
            }
        }
        catch (Exception ex)
        {
            WriteLog($"ERROR CRÍTICO AL INICIALIZAR MAINWINDOW: {ex}");
            LogStartupMessage($"ERROR CRÍTICO AL INICIALIZAR MAINWINDOW: {ex}");
            try
            {
                System.Windows.MessageBox.Show(
                    $"No se pudo abrir la ventana principal de Phantasm Optimizer:\n\n{PC_Optimizer.Services.UserErrorText.Of(ex)}\n\nLos detalles se guardaron en:\n{LogFilePath}",
                    "Phantasm Optimizer - Error al iniciar",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
            catch { }
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        WriteLog($"Finalizando Phantasm Optimizer (código {e.ApplicationExitCode}).");
        LogStartupMessage($"Finalizando Phantasm Optimizer (código {e.ApplicationExitCode}).");
        try { _appMutex?.Dispose(); } catch { }
        _appMutex = null;
        base.OnExit(e);
    }
}
