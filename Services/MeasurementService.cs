using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace PC_Optimizer.Services;

public interface IMeasurementService
{
    MeasurementSnapshot TakeSnapshot(string label);
    MeasurementReport Compare(string beforeLabel, string afterLabel);
    IReadOnlyList<MeasurementSnapshot> Snapshots { get; }
}

/// <summary>
/// Márgenes de ruido usados para no llamar «mejora» a una variación normal.
/// Se muestran en la interfaz para que la comparación sea comprobable.
/// </summary>
public static class MeasurementMargins
{
    public const double CpuPercent = 2.0;
    public const double RamPercent = 1.0;
    public const int ProcessCount = 2;
    public const long FreeSpaceBytes = 50L * 1024 * 1024;
    public const long HiberfilBytes = 50L * 1024 * 1024;
    public const int StartupPrograms = 0;
    public const double BootSeconds = 2.0;
    public const double DnsMs = 10.0;
    public const double PingMs = 5.0;

    /// <summary>Etiqueta de las métricas que varían por sí solas: se muestran, pero no se juzgan.</summary>
    public const string InformativeChip = "Informativo (varía por sí solo)";

    /// <summary>Dos medidas que no se hicieron de la misma forma o con otro servidor que no se pudo medir igual.</summary>
    public const string NotComparableChip = "No comparable";

    public const string Description =
        "Margen de ruido (dentro de él se muestra «Sin cambio medible»): espacio libre ±50 MB, " +
        "hiberfil.sys ±50 MB, programas de inicio 0, último arranque ±2 s, DNS ±10 ms, ping ±5 ms. " +
        "CPU, RAM y procesos son informativos: varían por sí solos según lo que esté abierto y no se cuentan " +
        "como efecto de ningún ajuste.";
}

public class MeasurementSnapshot
{
    public string Label { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.Now;

    /// <summary>Mediana de las muestras de CPU, no una sola lectura.</summary>
    public double CpuUsagePercent { get; set; }
    public long RamUsedBytes { get; set; }
    public long RamTotalBytes { get; set; }
    public double RamUsagePercent => RamTotalBytes > 0 ? (double)RamUsedBytes / RamTotalBytes * 100 : 0;
    public int CpuSampleCount { get; set; }

    public int ProcessCount { get; set; }

    /// <summary>Bytes libres en la unidad del sistema. null = no disponible.</summary>
    public long? FreeSpaceBytes { get; set; }

    /// <summary>hiberfil.sys: existe o no, tamaño y si se pudo leer.</summary>
    public HiberfilInfo? Hiberfil { get; set; }

    /// <summary>Programas de inicio habilitados. null = no disponible.</summary>
    public int? StartupProgramsEnabled { get; set; }

    /// <summary>Último arranque de Windows en segundos (evento 100). null = no disponible.</summary>
    public double? LastBootTimeSeconds { get; set; }

    /// <summary>Por qué no hay dato de arranque (null si lo hay).</summary>
    public string? BootTimeUnavailableReason { get; set; }

    /// <summary>Mediana del tiempo de resolución DNS sin caché. −1 = no disponible.</summary>
    public long DnsResolutionMs { get; set; } = -1;
    public string DnsServersUsed { get; set; } = "no disponible";
    public int DnsSuccessfulQueries { get; set; }

    /// <summary>Por qué el DNS no se pudo medir con fiabilidad (null si hay dato).</summary>
    public string? DnsUnavailableReason { get; set; }

    /// <summary>Cómo se midió el DNS (nombres nuevos o conocidos): solo se comparan medidas del mismo tipo.</summary>
    public DnsMeasureMethod DnsMethod { get; set; } = DnsMeasureMethod.None;

    /// <summary>Consultas DNS que no dieron una medida fiable (sin respuesta, descartadas).</summary>
    public int DnsDiscardedQueries { get; set; }

    /// <summary>Ping a 1.1.1.1. −1 = no disponible.</summary>
    public long PingMs { get; set; } = -1;
    public bool HasNetwork { get; set; }
}

/// <summary>Comparación de una métrica: valores, margen aplicado y clasificación honesta.</summary>
public class MetricComparison
{
    public string Label { get; init; } = string.Empty;
    public string BeforeText { get; init; } = "no disponible";
    public string AfterText { get; init; } = "no disponible";
    public string DeltaText { get; init; } = string.Empty;
    public string DeltaChip { get; init; } = "No disponible";
    public string Influences { get; init; } = string.Empty;
    public bool IsSmallChange => DeltaChip is "Sin cambio medible" or "No disponible"
        || DeltaChip == MeasurementMargins.InformativeChip || DeltaChip == MeasurementMargins.NotComparableChip;
}

public class MeasurementReport
{
    public string BeforeLabel { get; set; } = string.Empty;
    public string AfterLabel { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; } = DateTime.Now;
    public double CpuDeltaPercent { get; set; }
    public long RamDeltaBytes { get; set; }
    public double RamDeltaPercent { get; set; }
    public int ProcessDeltaCount { get; set; }
    public long DnsDeltaMs { get; set; }
    public long PingDeltaMs { get; set; }
    public string Summary { get; set; } = string.Empty;
    public bool HasMeasurableChange { get; set; }

    /// <summary>Una fila por métrica, con su margen y su clasificación.</summary>
    public List<MetricComparison> Metrics { get; } = new();
}

public class MeasurementService : IMeasurementService
{
    private readonly IMeasurementProvider _provider;
    private readonly List<MeasurementSnapshot> _snapshots = new();

    public IReadOnlyList<MeasurementSnapshot> Snapshots => _snapshots.AsReadOnly();

    /// <summary>Muestras de CPU y RAM (mediana). Se puede bajar en pruebas.</summary>
    public int SampleCount { get; set; } = 10;
    public int SampleIntervalMilliseconds { get; set; } = 1000;

    /// <summary>Consultas por dominio (5 dominios × 2 = 10 como mínimo pedido).</summary>
    public int DnsQueriesPerDomain { get; set; } = 2;
    public string[] DnsDomains { get; set; } = SystemMeasurementProvider.DefaultDnsDomains;

    public MeasurementService(IMeasurementProvider provider)
    {
        _provider = provider;
    }

    public MeasurementSnapshot TakeSnapshot(string label)
    {
        var cpuSamples = new List<double>();
        var ramSamples = new List<long>();
        long ramTotal = 0;

        var samples = Math.Max(1, SampleCount);
        for (var i = 0; i < samples; i++)
        {
            var metrics = _provider.GetMetrics();
            cpuSamples.Add(metrics.CpuUsagePercent);
            ramSamples.Add(metrics.RamUsedBytes);
            ramTotal = metrics.RamTotalBytes;

            if (i < samples - 1 && SampleIntervalMilliseconds > 0)
                Thread.Sleep(SampleIntervalMilliseconds);
        }

        var snapshot = new MeasurementSnapshot
        {
            Label = label,
            CpuUsagePercent = Median(cpuSamples),
            RamUsedBytes = Median(ramSamples),
            RamTotalBytes = ramTotal,
            CpuSampleCount = cpuSamples.Count,
            ProcessCount = SafeProcessCount(),
            FreeSpaceBytes = SafeFreeSpace(),
            Hiberfil = SafeHiberfil(),
            StartupProgramsEnabled = SafeStartupPrograms()
        };

        var boot = SafeBootTime();
        snapshot.LastBootTimeSeconds = boot.Seconds;
        snapshot.BootTimeUnavailableReason = boot.Seconds.HasValue ? null : boot.Reason ?? "no disponible";

        try
        {
            var probe = _provider.ProbeDns(DnsDomains, DnsQueriesPerDomain);
            snapshot.DnsServersUsed = probe.ServersUsed;
            snapshot.DnsSuccessfulQueries = probe.SuccessfulQueries;
            snapshot.DnsMethod = probe.Method;
            snapshot.DnsDiscardedQueries = probe.DiscardedQueries;

            // Nunca se guarda «0 ms»: una respuesta por debajo de 1 ms es la caché local, no el servidor DNS.
            if (probe.Available && probe.MedianMs >= DnsProbeAnalyzer.MinPlausibleNetworkMs)
            {
                snapshot.DnsResolutionMs = (long)Math.Max(1, Math.Round(probe.MedianMs));
            }
            else
            {
                snapshot.DnsResolutionMs = -1;
                snapshot.DnsUnavailableReason = probe.Available
                    ? "las respuestas llegaron en menos de 1 ms, lo normal de una respuesta guardada en memoria (la caché local de Windows o la de un router o servidor DNS de tu red) y no de una consulta a Internet."
                    : probe.Reason ?? "no se pudo consultar el servidor DNS.";
            }
        }
        catch
        {
            snapshot.DnsResolutionMs = -1;
            snapshot.DnsUnavailableReason = "la consulta DNS falló.";
        }

        var ping = _provider.PingMs("1.1.1.1", 2000);
        snapshot.PingMs = ping ?? -1;
        snapshot.HasNetwork = snapshot.DnsResolutionMs >= 0 || snapshot.PingMs >= 0;

        _snapshots.Add(snapshot);
        return snapshot;
    }

    public MeasurementReport Compare(string beforeLabel, string afterLabel)
    {
        var before = _snapshots.LastOrDefault(s => s.Label == beforeLabel);
        var after = _snapshots.LastOrDefault(s => s.Label == afterLabel);

        var report = new MeasurementReport { BeforeLabel = beforeLabel, AfterLabel = afterLabel };

        if (before == null || after == null)
        {
            report.Summary = "No se encontraron ambas instantáneas para comparar.";
            report.HasMeasurableChange = false;
            return report;
        }

        report.CpuDeltaPercent = after.CpuUsagePercent - before.CpuUsagePercent;
        report.RamDeltaBytes = after.RamUsedBytes - before.RamUsedBytes;
        report.RamDeltaPercent = before.RamUsagePercent > 0 ? after.RamUsagePercent - before.RamUsagePercent : 0;
        report.ProcessDeltaCount = after.ProcessCount - before.ProcessCount;
        report.DnsDeltaMs = before.DnsResolutionMs >= 0 && after.DnsResolutionMs >= 0
            ? after.DnsResolutionMs - before.DnsResolutionMs : -1;
        report.PingDeltaMs = before.PingMs >= 0 && after.PingMs >= 0 ? after.PingMs - before.PingMs : -1;

        const string variesAlone = "Informativo: varía por sí solo según lo que esté abierto; no se atribuye a ningún ajuste.";

        AddMetric(report, "CPU en uso (mediana)", before.CpuUsagePercent, after.CpuUsagePercent,
            v => $"{v:0.0} %", MeasurementMargins.CpuPercent, lowerIsBetter: true, variesAlone, informational: true);

        AddMetric(report, "RAM en uso (mediana)", before.RamUsagePercent, after.RamUsagePercent,
            v => $"{v:0.0} %", MeasurementMargins.RamPercent, lowerIsBetter: true, variesAlone, informational: true);

        AddMetric(report, "Procesos", before.ProcessCount, after.ProcessCount,
            v => $"{v:0}", MeasurementMargins.ProcessCount, lowerIsBetter: true, variesAlone, informational: true);

        AddMetric(report, "Espacio libre en C:", before.FreeSpaceBytes, after.FreeSpaceBytes,
            v => FormatBytes((long)v), MeasurementMargins.FreeSpaceBytes, lowerIsBetter: false,
            "Pueden influir: limpieza de %TEMP% y desactivar la hibernación.");

        var beforeHiberfil = before.Hiberfil is { Available: true, Exists: true } hb ? hb.Bytes : (long?)0;
        var afterHiberfil = after.Hiberfil is { Available: true, Exists: true } ha ? ha.Bytes : (long?)0;
        var hiberfilAvailable = before.Hiberfil is { Available: true } && after.Hiberfil is { Available: true };
        AddMetric(report, "Tamaño de hiberfil.sys",
            hiberfilAvailable ? beforeHiberfil : null,
            hiberfilAvailable ? afterHiberfil : null,
            v => v <= 0 ? "no existe" : FormatBytes((long)v),
            MeasurementMargins.HiberfilBytes, lowerIsBetter: true,
            "Puede influir: desactivar la hibernación.",
            // La diferencia es una cantidad de bytes: 0 es «sin cambio», nunca «no existe».
            formatDelta: v => v <= 0 ? "sin cambio" : FormatBytes((long)v));

        AddMetric(report, "Programas de inicio habilitados", before.StartupProgramsEnabled, after.StartupProgramsEnabled,
            v => $"{v:0}", MeasurementMargins.StartupPrograms, lowerIsBetter: true,
            "Pueden influir: programas de inicio.");

        AddMetric(report, "Último arranque de Windows", before.LastBootTimeSeconds, after.LastBootTimeSeconds,
            v => $"{v:0.0} s", MeasurementMargins.BootSeconds, lowerIsBetter: true,
            "Pueden influir: programas de inicio e inicio rápido. Con el inicio rápido activo, apagar y encender tarda distinto que reiniciar: compara siempre el mismo tipo de arranque (reiniciar no usa el inicio rápido)." +
            ReasonSuffix(before.BootTimeUnavailableReason, after.BootTimeUnavailableReason));

        AddDnsMetric(report, before, after);

        AddMetric(report, "Ping a 1.1.1.1",
            before.PingMs >= 0 ? before.PingMs : null,
            after.PingMs >= 0 ? after.PingMs : null,
            v => $"{v:0} ms", MeasurementMargins.PingMs, lowerIsBetter: true,
            "Mide la ruta de red hasta 1.1.1.1; no depende de los servidores DNS.");

        var classified = report.Metrics.Where(m => m.DeltaChip is "Mejora medible" or "Peor").ToList();
        report.HasMeasurableChange = classified.Count > 0;
        report.Summary = classified.Count == 0
            ? "Sin cambio medible en las métricas medidas (todas dentro de su margen de ruido)."
            : "Cambios fuera del margen: " + string.Join("; ", classified.Select(m =>
                $"{m.Label}: {m.BeforeText} → {m.AfterText} ({m.DeltaChip})")) + ".";

        return report;
    }

    /// <summary>
    /// Añade una fila comparando con el margen de su métrica. La dirección indica si bajar es
    /// mejor (CPU, RAM, DNS...) o si bajar es peor (espacio libre).
    /// </summary>
    private static void AddMetric(MeasurementReport report, string label, double? before, double? after,
        Func<double, string> format, double margin, bool lowerIsBetter, string influences, bool informational = false,
        Func<double, string>? formatDelta = null)
    {
        if (before == null || after == null)
        {
            report.Metrics.Add(new MetricComparison
            {
                Label = label,
                BeforeText = before == null ? "no disponible" : format(before.Value),
                AfterText = after == null ? "no disponible" : format(after.Value),
                DeltaChip = "No disponible",
                Influences = influences
            });
            return;
        }

        var delta = after.Value - before.Value;
        var changed = Math.Abs(delta) > margin;

        string chip;
        if (informational) chip = MeasurementMargins.InformativeChip;
        else if (!changed) chip = "Sin cambio medible";
        else if (lowerIsBetter) chip = delta < 0 ? "Mejora medible" : "Peor";
        else chip = delta > 0 ? "Mejora medible" : "Peor";

        var sign = delta > 0 ? "+" : delta < 0 ? "−" : string.Empty; // «−» (U+2212) para que la baja se vea como baja
        report.Metrics.Add(new MetricComparison
        {
            Label = label,
            BeforeText = format(before.Value),
            AfterText = format(after.Value),
            DeltaText = $"{sign}{(formatDelta ?? format)(Math.Abs(delta))}",
            DeltaChip = chip,
            Influences = influences
        });
    }

    private int SafeProcessCount()
    {
        try { return _provider.GetProcessCount(); }
        catch { return 0; }
    }

    private long? SafeFreeSpace()
    {
        try { return _provider.GetFreeSpaceOnSystemDrive(); }
        catch { return null; }
    }

    private HiberfilInfo? SafeHiberfil()
    {
        try { return _provider.GetHiberfil(); }
        catch { return null; }
    }

    private int? SafeStartupPrograms()
    {
        try { return _provider.GetEnabledStartupPrograms(); }
        catch { return null; }
    }

    private BootTimeReading SafeBootTime()
    {
        try { return _provider.GetLastBootTime(); }
        catch { return new BootTimeReading(null, "no se pudo leer el registro de eventos de Windows."); }
    }

    private static string DescribeMethod(DnsMeasureMethod method) => method switch
    {
        DnsMeasureMethod.UniqueNames => "con nombres nuevos en cada consulta",
        DnsMeasureMethod.KnownNames => "con dominios conocidos",
        _ => "sin dato"
    };

    /// <summary>
    /// Fila del DNS: solo se compara si las dos medidas se hicieron de la misma forma. Si el servidor cambió
    /// y una no se pudo medir (p. ej. un router que contesta sin consultar hacia afuera), se dice «No
    /// comparable» con el motivo en vez de un simple «no disponible».
    /// </summary>
    private static void AddDnsMetric(MeasurementReport report, MeasurementSnapshot before, MeasurementSnapshot after)
    {
        const string label = "Tiempo de resolución DNS (mediana)";
        var beforeOk = before.DnsResolutionMs >= 0;
        var afterOk = after.DnsResolutionMs >= 0;
        var serverChanged = !string.Equals(before.DnsServersUsed, after.DnsServersUsed, StringComparison.OrdinalIgnoreCase);
        var servers = $"DNS usado antes: {before.DnsServersUsed}; después: {after.DnsServersUsed}.";
        string Value(bool ok, long ms) => ok ? $"{ms} ms" : "no medible";

        if (beforeOk && afterOk && before.DnsMethod != after.DnsMethod)
        {
            report.Metrics.Add(new MetricComparison
            {
                Label = label,
                BeforeText = Value(beforeOk, before.DnsResolutionMs),
                AfterText = Value(afterOk, after.DnsResolutionMs),
                DeltaChip = MeasurementMargins.NotComparableChip,
                Influences = $"No comparable: antes se midió {DescribeMethod(before.DnsMethod)} y después {DescribeMethod(after.DnsMethod)}, " +
                             $"y esas medidas no se pueden restar. {servers}"
            });
            return;
        }

        if ((!beforeOk || !afterOk) && serverChanged)
        {
            report.Metrics.Add(new MetricComparison
            {
                Label = label,
                BeforeText = Value(beforeOk, before.DnsResolutionMs),
                AfterText = Value(afterOk, after.DnsResolutionMs),
                DeltaChip = MeasurementMargins.NotComparableChip,
                Influences = $"No comparable: el servidor DNS cambió y no se pudo medir igual en los dos momentos. {servers}" +
                             ReasonSuffix(before.DnsUnavailableReason, after.DnsUnavailableReason)
            });
            return;
        }

        var how = beforeOk && afterOk && before.DnsMethod == DnsMeasureMethod.UniqueNames
            ? " Medido con nombres nuevos en cada consulta, sin caché: es la resolución «en frío» (puede diferir del tiempo habitual con caché, " +
              "depende de la distancia del servidor a los DNS de iana.org y la validación DNSSEC puede influir)."
            : string.Empty;
        if (beforeOk && before.DnsDiscardedQueries > 0)
            how += $" Antes: {before.DnsDiscardedQueries} consulta(s) sin medida fiable.";
        if (afterOk && after.DnsDiscardedQueries > 0)
            how += $" Después: {after.DnsDiscardedQueries} consulta(s) sin medida fiable.";
        AddMetric(report, label,
            beforeOk ? before.DnsResolutionMs : null,
            afterOk ? after.DnsResolutionMs : null,
            v => $"{v:0} ms", MeasurementMargins.DnsMs, lowerIsBetter: true,
            $"Pueden influir: servidores DNS. {servers}{how}" +
            ReasonSuffix(before.DnsUnavailableReason, after.DnsUnavailableReason));
    }

    /// <summary>Motivo(s) por los que una métrica no está disponible, para mostrarlo junto a la fila.</summary>
    private static string ReasonSuffix(string? before, string? after)
    {
        var reasons = new[] { before, after }.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct().ToList();
        return reasons.Count == 0 ? string.Empty : " No disponible: " + string.Join(" / ", reasons);
    }

    private static double Median(List<double> values)
    {
        var ordered = values.OrderBy(v => v).ToList();
        var middle = ordered.Count / 2;
        return ordered.Count % 2 == 1 ? ordered[middle] : (ordered[middle - 1] + ordered[middle]) / 2.0;
    }

    private static long Median(List<long> values)
    {
        var ordered = values.OrderBy(v => v).ToList();
        var middle = ordered.Count / 2;
        return ordered.Count % 2 == 1 ? ordered[middle] : (ordered[middle - 1] + ordered[middle]) / 2;
    }

    /// <summary>Unidad automática, sin redondear a favor (1 decimal a partir de KB).</summary>
    public static string FormatBytes(long bytes)
    {
        if (bytes < 0) return "no disponible";
        if (bytes < 1024) return $"{bytes} B";
        double value = bytes;
        string[] units = { "KB", "MB", "GB", "TB" };
        var unit = -1;
        do { value /= 1024; unit++; } while (value >= 1024 && unit < units.Length - 1);
        return value >= 100 ? $"{value:0} {units[unit]}" : $"{value:0.0} {units[unit]}";
    }
}
