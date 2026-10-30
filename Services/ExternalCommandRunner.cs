using System;
using System.Diagnostics;

namespace PC_Optimizer.Services;

/// <summary>Resultado de una herramienta del sistema ejecutada sin ventana.</summary>
public sealed record CommandResult(bool Started, int ExitCode, string Output, string Error, bool TimedOut)
{
    /// <summary>Primera línea con texto de la salida de error (para mensajes cortos y honestos).</summary>
    public string FirstErrorLine()
    {
        foreach (var line in (Error ?? string.Empty).Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0) return trimmed;
        }
        return string.Empty;
    }
}

/// <summary>
/// Ejecuta una herramienta del sistema (PowerShell, fsutil…) sin ventana, con tiempo máximo y
/// salida capturada. Está detrás de una interfaz para que las pruebas usen un ejecutor simulado
/// y nunca lancen procesos reales.
/// </summary>
public interface IExternalCommandRunner
{
    CommandResult Run(string fileName, string arguments, int timeoutMilliseconds);
}

public sealed class ExternalCommandRunner : IExternalCommandRunner
{
    public CommandResult Run(string fileName, string arguments, int timeoutMilliseconds)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var process = Process.Start(psi);
            if (process == null) return new CommandResult(false, -1, string.Empty, "No se pudo iniciar el proceso.", false);

            // Lectura asíncrona para no bloquearse si la herramienta escribe mucho.
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            bool exited = process.WaitForExit(Math.Max(1, timeoutMilliseconds));
            if (!exited)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return new CommandResult(true, -1, string.Empty, "La herramienta no terminó en el tiempo máximo.", true);
            }

            // Margen corto para que termine de vaciarse la salida redirigida.
            WaitFor(outputTask, 2000);
            WaitFor(errorTask, 2000);

            return new CommandResult(true, process.ExitCode, SafeResult(outputTask), SafeResult(errorTask), false);
        }
        catch (Exception ex)
        {
            return new CommandResult(false, -1, string.Empty, PC_Optimizer.Services.UserErrorText.Of(ex), false);
        }
    }

    private static void WaitFor(System.Threading.Tasks.Task<string> task, int milliseconds)
    {
        try { task.Wait(milliseconds); } catch { }
    }

    private static string SafeResult(System.Threading.Tasks.Task<string> task)
    {
        try { return task.Status == System.Threading.Tasks.TaskStatus.RanToCompletion ? task.Result : string.Empty; }
        catch { return string.Empty; }
    }
}
