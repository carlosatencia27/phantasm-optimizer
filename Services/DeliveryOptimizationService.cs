using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PC_Optimizer.Core;

namespace PC_Optimizer.Services;

/// <summary>
/// Estado de la caché de Optimización de entrega (solo lectura).
/// ProblemText != null → la acción no se puede ofrecer y se explica el motivo.
/// </summary>
public sealed record DeliveryOptimizationStatus(
    bool CmdletAvailable,
    IReadOnlyList<string> ExistingFolders,
    int FileCount,
    long Bytes,
    string? ProblemText);

/// <summary>Resultado de vaciar la caché. El espacio liberado se vuelve a medir después.</summary>
public sealed record DeliveryOptimizationOutcome(
    bool Succeeded,
    bool CmdletAvailable,
    int FileCountBefore,
    long BytesFreedMeasured,
    string Message);

/// <summary>
/// Caché de Optimización de entrega: se mide en solo lectura y se vacía con el cmdlet oficial
/// <c>Delete-DeliveryOptimizationCache -Force</c>. Detrás de una interfaz para las pruebas.
/// </summary>
public interface IDeliveryOptimizationService
{
    /// <summary>Rutas reales que se comprueban (solo lectura).</summary>
    IReadOnlyList<string> CandidateFolders { get; }

    DeliveryOptimizationStatus GetStatus(bool isAdmin);

    DeliveryOptimizationOutcome Clean(bool isAdmin);
}

/// <summary>
/// Implementación real. La caché de Optimización de entrega vive en el perfil del servicio
/// (NetworkService) y, según la versión, también en SoftwareDistribution o ProgramData: se
/// comprueban las tres y se suma lo que exista. El borrado lo hace el cmdlet oficial, nunca
/// esta clase.
/// </summary>
public sealed class SystemDeliveryOptimizationService : IDeliveryOptimizationService
{
    public const string CmdletName = "Delete-DeliveryOptimizationCache";
    public const int CleanTimeoutMs = 60000;
    public const int ProbeTimeoutMs = 30000;

    private static readonly string[] Folders = BuildCandidateFolders();

    private readonly IDiskCleanupService _folders;
    private readonly IExternalCommandRunner _runner;
    private bool? _cmdletAvailable;

    public SystemDeliveryOptimizationService(IDiskCleanupService? folders = null, IExternalCommandRunner? runner = null,
        IEnumerable<string>? candidateFolders = null)
    {
        _folders = folders ?? new DiskCleanupService();
        _runner = runner ?? new ExternalCommandRunner();
        CandidateFolders = candidateFolders?.ToArray() ?? Folders;
    }

    public IReadOnlyList<string> CandidateFolders { get; }

    /// <summary>
    /// Rutas comprobadas: la del perfil del servicio (observada en Windows 10 y 11 con contenido
    /// descargado), la de SoftwareDistribution (versiones anteriores) y la de ProgramData.
    /// </summary>
    private static string[] BuildCandidateFolders()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var list = new List<string>();

        if (!string.IsNullOrEmpty(windows))
        {
            list.Add(Path.Combine(windows, "ServiceProfiles", "NetworkService", "AppData", "Local",
                "Microsoft", "Windows", "DeliveryOptimization"));
            list.Add(Path.Combine(windows, "SoftwareDistribution", "DeliveryOptimization"));
        }

        if (!string.IsNullOrEmpty(programData))
            list.Add(Path.Combine(programData, "Microsoft", "Windows", "DeliveryOptimization"));

        return list.ToArray();
    }

    public DeliveryOptimizationStatus GetStatus(bool isAdmin)
    {
        var existing = ExistingFolders();
        var (files, bytes) = Measure(existing);
        bool cmdlet = CmdletAvailable();

        string? problem = null;
        if (!cmdlet)
            problem = $"Este equipo no tiene el cmdlet {CmdletName} (Windows 10 1809 o posterior). "
                + "La acción no está disponible.";
        else if (!isAdmin)
            problem = "Requiere administrador: la caché está dentro de la carpeta de Windows. "
                + "Reinicia la app como administrador para ver el tamaño real y vaciarla.";

        return new DeliveryOptimizationStatus(cmdlet, existing, files, bytes, problem);
    }

    public DeliveryOptimizationOutcome Clean(bool isAdmin)
    {
        var existing = ExistingFolders();
        var (filesBefore, bytesBefore) = Measure(existing);

        if (!isAdmin)
            return new DeliveryOptimizationOutcome(false, _cmdletAvailable ?? false, filesBefore, 0,
                "Se necesitan permisos de administrador para vaciar la caché de Optimización de entrega. No se borró nada.");

        if (!CmdletAvailable())
            return new DeliveryOptimizationOutcome(false, false, filesBefore, 0,
                $"El cmdlet {CmdletName} no está disponible en este equipo. No se borró nada.");

        var result = _runner.Run("powershell.exe",
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{CmdletName} -Force\"",
            CleanTimeoutMs);

        if (!result.Started)
            return new DeliveryOptimizationOutcome(false, true, filesBefore, 0,
                $"No se pudo iniciar PowerShell: {result.FirstErrorLine()}. No se borró nada.");
        if (result.TimedOut)
            return new DeliveryOptimizationOutcome(false, true, filesBefore, 0,
                $"El cmdlet no terminó en {CleanTimeoutMs / 1000} s. No se pudo confirmar la limpieza.");
        if (result.ExitCode != 0)
            return new DeliveryOptimizationOutcome(false, true, filesBefore, 0,
                $"El cmdlet devolvió el código {result.ExitCode}: {result.FirstErrorLine()}");

        var (filesAfter, bytesAfter) = Measure(existing);
        long freed = Math.Max(0, bytesBefore - bytesAfter);
        var message = $"Caché de Optimización de entrega vaciada con el cmdlet. "
            + $"Verificación posterior: espacio liberado medido {ByteSize.Format(freed)}; quedan {filesAfter} archivo(s).";
        return new DeliveryOptimizationOutcome(true, true, filesBefore, freed, message);
    }

    private List<string> ExistingFolders()
    {
        var list = new List<string>();
        foreach (var folder in CandidateFolders)
        {
            try { if (Directory.Exists(folder)) list.Add(folder); } catch { }
        }
        return list;
    }

    private (int Files, long Bytes) Measure(IReadOnlyList<string> existing)
    {
        int files = 0;
        long bytes = 0;
        foreach (var folder in existing)
        {
            try
            {
                var preview = _folders.GetTempFolderPreview(folder);
                files += preview.FilesDeleted;
                bytes += preview.BytesFreed;
            }
            catch { }
        }
        return (files, bytes);
    }

    /// <summary>
    /// Comprueba una sola vez si el cmdlet existe. Solo lee: no cambia nada del sistema.
    /// </summary>
    private bool CmdletAvailable()
    {
        if (_cmdletAvailable.HasValue) return _cmdletAvailable.Value;

        var result = _runner.Run("powershell.exe",
            "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "
            + $"\"if (Get-Command -Name {CmdletName} -ErrorAction SilentlyContinue) {{ 'yes' }}\"",
            ProbeTimeoutMs);

        _cmdletAvailable = result.Started && !result.TimedOut
            && result.Output.Contains("yes", StringComparison.OrdinalIgnoreCase);
        return _cmdletAvailable.Value;
    }
}
