using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;

namespace PC_Optimizer.Services;

/// <summary>Una unidad fija con su tipo real y su espacio. MediaType: «SSD», «HDD» o «desconocido».</summary>
public sealed record StorageVolume(string Name, string MediaType, long FreeBytes, long TotalBytes)
{
    public double FreePercent => TotalBytes > 0 ? FreeBytes * 100.0 / TotalBytes : 0;
}

/// <summary>Estado de TRIM. Available=false → no se pudo consultar (nunca se inventa el valor).</summary>
public sealed record TrimStatus(bool Available, bool? Enabled, string Text, string Detail)
{
    public static readonly TrimStatus Unknown = new(false, null, "no disponible", string.Empty);
}

public sealed record StorageReport(IReadOnlyList<StorageVolume> Volumes, TrimStatus Trim, string? Note);

/// <summary>
/// Fuente de «Almacenamiento» (solo lectura). Detrás de una interfaz para que las pruebas usen
/// datos simulados y nunca consulten WMI ni ejecuten fsutil.
/// </summary>
public interface IStorageDiagnosticsService
{
    StorageReport GetReport(bool isAdmin);
}

/// <summary>
/// Implementación real: unidades fijas con <see cref="DriveInfo"/>, tipo por
/// MSFT_PhysicalDisk.MediaType (espacio de nombres Storage) y TRIM con
/// <c>fsutil behavior query DisableDeleteNotify</c>. Todo es solo lectura.
/// </summary>
public sealed class SystemStorageDiagnosticsService : IStorageDiagnosticsService
{
    public const string TrimArguments = "behavior query DisableDeleteNotify";
    private const string StorageNamespace = @"root\Microsoft\Windows\Storage";

    private readonly IExternalCommandRunner _runner;
    private readonly Func<(IReadOnlyList<StorageVolume> Volumes, string? Note)> _volumeReader;

    public SystemStorageDiagnosticsService(IExternalCommandRunner? runner = null,
        Func<(IReadOnlyList<StorageVolume> Volumes, string? Note)>? volumeReader = null)
    {
        _runner = runner ?? new ExternalCommandRunner();
        _volumeReader = volumeReader ?? (() =>
        {
            var volumes = ReadVolumes(out var note);
            return (volumes, note);
        });
    }

    public StorageReport GetReport(bool isAdmin)
    {
        var (volumes, note) = _volumeReader();
        var trim = ReadTrim(isAdmin, volumes);
        return new StorageReport(volumes, trim, note);
    }

    private static List<StorageVolume> ReadVolumes(out string? note)
    {
        note = null;
        var list = new List<StorageVolume>();
        try
        {
            var map = ReadMediaTypes();

            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;

                    string name = drive.Name;
                    long total = drive.TotalSize;
                    long free = drive.AvailableFreeSpace;

                    string media = "desconocido";
                    var letter = name.TrimEnd('\\', ':');
                    if (map.TryGetValue(letter, out var detected) && !string.IsNullOrEmpty(detected)) media = detected;

                    list.Add(new StorageVolume(name, media, free, total));
                }
                catch
                {
                    // Unidad sin acceso: se omite, no se inventan cifras.
                }
            }
        }
        catch (Exception ex)
        {
            note = $"No se pudo leer el tipo de disco: {PC_Optimizer.Services.UserErrorText.Of(ex)}";
        }

        return list;
    }

    /// <summary>
    /// Asocia cada letra de unidad con el tipo del disco físico: MSFT_Partition (DriveLetter →
    /// DiskNumber) y MSFT_PhysicalDisk.MediaType (0 desconocido, 3 HDD, 4 SSD, 5 SCM).
    /// Si algo no se puede leer, la unidad queda como «desconocido».
    /// </summary>
    private static Dictionary<string, string> ReadMediaTypes()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var mediaByDisk = new Dictionary<uint, string>();

        try
        {
            using var searcher = new ManagementObjectSearcher(StorageNamespace, "SELECT DeviceId, MediaType FROM MSFT_PhysicalDisk");
            foreach (var item in searcher.Get())
            {
                using var disk = item as ManagementObject;
                if (disk == null) continue;

                var deviceId = disk["DeviceId"]?.ToString();
                var mediaType = disk["MediaType"];
                if (deviceId == null || mediaType == null) continue;
                if (!uint.TryParse(deviceId, out var number)) continue;

                mediaByDisk[number] = mediaType switch
                {
                    uint value when value == 3 => "HDD",
                    uint value when value == 4 => "SSD",
                    uint value when value == 5 => "SCM",
                    _ => "desconocido"
                };
            }
        }
        catch
        {
            return result;
        }

        try
        {
            using var partitions = new ManagementObjectSearcher(StorageNamespace, "SELECT DiskNumber, DriveLetter FROM MSFT_Partition");
            foreach (var item in partitions.Get())
            {
                using var partition = item as ManagementObject;
                if (partition == null) continue;

                var letter = partition["DriveLetter"]?.ToString();
                var diskNumber = partition["DiskNumber"];
                if (string.IsNullOrWhiteSpace(letter) || diskNumber == null) continue;
                if (!uint.TryParse(diskNumber.ToString(), out var number)) continue;

                if (mediaByDisk.TryGetValue(number, out var media))
                    result[letter.TrimEnd(':', '\\')] = media;
            }
        }
        catch
        {
            // Sin asociación: las unidades se muestran como «desconocido».
        }

        return result;
    }

    private TrimStatus ReadTrim(bool isAdmin, IReadOnlyList<StorageVolume> volumes)
    {
        bool hasSsd = volumes.Any(v => string.Equals(v.MediaType, "SSD", StringComparison.OrdinalIgnoreCase));
        bool hasHdd = volumes.Any(v => string.Equals(v.MediaType, "HDD", StringComparison.OrdinalIgnoreCase));

        // En discos mecánicos TRIM no aplica: no se muestran consejos.
        if (!hasSsd && hasHdd)
            return new TrimStatus(true, null, "no aplica en discos mecánicos",
                "No se detectaron unidades SSD, así que TRIM no aplica.");

        if (!isAdmin)
            return new TrimStatus(false, null, "no disponible sin administrador",
                "fsutil necesita permisos de administrador. La app no cambia este valor.");

        var result = _runner.Run("fsutil.exe", TrimArguments, 10000);
        if (!result.Started || result.TimedOut)
            return new TrimStatus(false, null, "no disponible",
                string.IsNullOrWhiteSpace(result.FirstErrorLine())
                    ? "No se pudo consultar fsutil."
                    : $"No se pudo consultar fsutil: {result.FirstErrorLine()}");

        if (!TryParseTrim(result.Output, out bool enabled, out var raw))
            return new TrimStatus(false, null, "no disponible", "fsutil no devolvió un valor reconocible.");

        return enabled
            ? new TrimStatus(true, true, "activado", $"Windows informa DisableDeleteNotify = 0 (NTFS). Detalle: {raw}")
            : new TrimStatus(true, false, "desactivado", $"Windows informa DisableDeleteNotify = 1 (NTFS). Detalle: {raw}");
    }

    /// <summary>Interpreta «NTFS DisableDeleteNotify = 0/1». 0 = TRIM activo; 1 = desactivado.</summary>
    public static bool TryParseTrim(string output, out bool enabled, out string raw)
    {
        enabled = false;
        raw = string.Empty;
        if (string.IsNullOrWhiteSpace(output)) return false;

        string? chosen = null;
        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            if (trimmed.IndexOf("DisableDeleteNotify", StringComparison.OrdinalIgnoreCase) < 0) continue;

            if (chosen == null) chosen = trimmed;
            if (trimmed.StartsWith("NTFS", StringComparison.OrdinalIgnoreCase))
            {
                chosen = trimmed;
                break;
            }
        }

        if (chosen == null) return false;
        raw = chosen;

        var index = chosen.IndexOf('=');
        if (index < 0) return false;

        var value = chosen.Substring(index + 1).Trim();
        if (value.StartsWith("0")) { enabled = true; return true; }
        if (value.StartsWith("1")) { enabled = false; return true; }
        return false;
    }
}
