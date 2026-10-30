using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace PC_Optimizer.Services;

/// <summary>
/// Cronómetro de navegación para diagnosticar lag. Solo actúa si existe la variable de entorno
/// PCOPT_NAVTIMING=1; escribe en el registro local (App.WriteLog) y no cambia nada más.
/// PCOPT_NAVBENCH=1 además recorre las secciones solo, dos pasadas, y cierra la app.
/// </summary>
public static class NavTiming
{
    public static bool Enabled { get; } =
        Environment.GetEnvironmentVariable("PCOPT_NAVTIMING") == "1" ||
        Environment.GetEnvironmentVariable("PCOPT_NAVBENCH") == "1";

    public static bool BenchRequested { get; } = Environment.GetEnvironmentVariable("PCOPT_NAVBENCH") == "1";

    /// <summary>Tiempo desde que se pide la sección hasta que la interfaz queda inactiva (ya dibujada).</summary>
    public static Task MeasureAsync(string section, Stopwatch sw)
    {
        var tcs = new TaskCompletionSource();
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null) { tcs.SetResult(); return tcs.Task; }
        dispatcher.BeginInvoke(new Action(() =>
        {
            App.WriteLog($"[NAV] {section}: {sw.Elapsed.TotalMilliseconds:0} ms");
            tcs.SetResult();
        }), DispatcherPriority.ContextIdle);
        return tcs.Task;
    }

    public static async Task RunBenchAsync(Action<string> navigate)
    {
        // Herramienta de diagnóstico: ninguna excepción puede hacer fallar la app.
        try
        {
            await Task.Delay(4000); // deja terminar la carga inicial
            string[] sections = { "Diagnostics", "Cleanup", "Measurement", "Profile", "BackupHistory", "Guided", "Panel", "Home" };
            for (int pass = 1; pass <= 2; pass++)
            {
                App.WriteLog($"[NAV] --- pasada {pass} ---");
                foreach (var s in sections)
                {
                    navigate(s);
                    await Task.Delay(1500);
                }
            }
            await Task.Delay(3000);
        }
        catch (Exception ex)
        {
            try { App.WriteLog($"[NAV] bench interrumpido: {ex.GetType().Name}"); } catch { /* sin registro, se ignora */ }
        }
        finally
        {
            try { System.Windows.Application.Current?.Dispatcher.Invoke(() => System.Windows.Application.Current.Shutdown()); } catch { }
        }
    }
}
