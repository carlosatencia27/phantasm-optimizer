namespace PC_Optimizer.Models;

public class SystemMetrics
{
    // ── CPU ────────────────────────────────────────────────────────────────────
    public double CpuUsagePercent { get; set; }
    public string CpuName { get; set; } = string.Empty;

    // ── RAM ────────────────────────────────────────────────────────────────────
    public long RamTotalBytes { get; set; }
    public long RamUsedBytes { get; set; }
    public double RamUsagePercent => RamTotalBytes > 0 ? (double)RamUsedBytes / RamTotalBytes * 100 : 0;

    /// <summary>Descripción legible: "9.3 / 16 GB"</summary>
    public string RamDescription => RamTotalBytes > 0
        ? $"{FormatGB(RamUsedBytes)} / {FormatGB(RamTotalBytes)} GB"
        : "No disponible";

    // ── Disco ──────────────────────────────────────────────────────────────────
    public long DiskTotalBytes { get; set; }
    public long DiskUsedBytes { get; set; }
    public double DiskUsagePercent => DiskTotalBytes > 0 ? (double)DiskUsedBytes / DiskTotalBytes * 100 : 0;

    /// <summary>Descripción: "NVMe · 476 GB libres"</summary>
    public string DiskDescription => DiskTotalBytes > 0
        ? $"{FormatGB(DiskTotalBytes - DiskUsedBytes)} GB libres"
        : "No disponible";

    // ── GPU ────────────────────────────────────────────────────────────────────
    public double? GpuUsagePercent { get; set; }
    public string GpuName { get; set; } = string.Empty;

    // ── Temperatura CPU ────────────────────────────────────────────────────────
    public double? TemperatureCelsius { get; set; }

    // ── Red ────────────────────────────────────────────────────────────────────
    /// <summary>Velocidad de red actual (texto). Null si no disponible.</summary>
    public string? NetworkSpeed { get; set; }
    public double NetworkUsagePercent { get; set; }
    public string NetworkDescription { get; set; } = string.Empty;

    // ── Monitor ────────────────────────────────────────────────────────────────
    public double? CurrentRefreshHz { get; set; }
    public double? MaxRefreshHz { get; set; }

    /// <summary>Texto como "60 Hz" para mostrar en la tarjeta de monitor.</summary>
    public string MonitorRefreshDisplay => CurrentRefreshHz.HasValue
        ? $"{CurrentRefreshHz:0} Hz"
        : "No disponible";

    /// <summary>Descripción adicional: avisa si no está en la frecuencia máxima.</summary>
    public string MonitorDescription
    {
        get
        {
            if (!CurrentRefreshHz.HasValue) return string.Empty;
            if (!MaxRefreshHz.HasValue)     return $"Actual: {CurrentRefreshHz:0} Hz · Máxima: no disponible";
            if (CurrentRefreshHz < MaxRefreshHz)
                return $"Puede subir a {MaxRefreshHz:0} Hz en Configuración > Sistema > Pantalla";
            return $"Configurado a la frecuencia máxima ({MaxRefreshHz:0} Hz)";
        }
    }

    // ── Archivos temporales ────────────────────────────────────────────────────
    public long TempFilesCount { get; set; }
    public long TempFilesBytes { get; set; }

    /// <summary>Texto como "3940 archivos · 1.6 GB" o la invitación a calcular la vista previa.</summary>
    public string TempFilesDisplay => TempFilesCount > 0 || TempFilesBytes > 0
        ? $"{TempFilesCount} archivos · {FormatBytes(TempFilesBytes)}"
        : "Calcula la vista previa en Limpieza";

    // ── Timestamp ──────────────────────────────────────────────────────────────
    public DateTime Timestamp { get; set; } = DateTime.Now;

    // ── Helpers privados ───────────────────────────────────────────────────────
    private static string FormatGB(long bytes)
    {
        double gb = bytes / 1_073_741_824.0;
        return gb >= 10 ? $"{gb:0}" : $"{gb:0.0}";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:0.#} GB";
        if (bytes >= 1_048_576)     return $"{bytes / 1_048_576.0:0.#} MB";
        if (bytes >= 1_024)         return $"{bytes / 1_024.0:0.#} KB";
        return $"{bytes} B";
    }
}
