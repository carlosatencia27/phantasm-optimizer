using System;
using System.Diagnostics;

namespace PC_Optimizer.Services;

/// <summary>
/// Vacía la caché DNS del resolver de Windows. Detrás de una interfaz para que las pruebas no
/// ejecuten ningún comando real.
/// </summary>
public interface IDnsCacheService
{
    bool TryFlush(out string? error);
}

/// <summary>
/// Usa «ipconfig /flushdns» (comando documentado por Microsoft: ipconfig, /flushdns), sin
/// ventana y con 5 segundos de tiempo máximo.
/// </summary>
public sealed class WindowsDnsCacheService : IDnsCacheService
{
    private const int TimeoutMilliseconds = 5000;

    public bool TryFlush(out string? error)
    {
        error = null;
        try
        {
            var startInfo = new ProcessStartInfo("ipconfig", "/flushdns")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                error = "No se pudo iniciar ipconfig.";
                return false;
            }

            if (!process.WaitForExit(TimeoutMilliseconds))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                error = $"ipconfig /flushdns no terminó en {TimeoutMilliseconds / 1000} s.";
                return false;
            }

            if (process.ExitCode != 0)
            {
                error = $"ipconfig /flushdns terminó con código {process.ExitCode}.";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = PC_Optimizer.Services.UserErrorText.Of(ex);
            return false;
        }
    }
}
