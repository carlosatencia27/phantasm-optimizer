using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using PC_Optimizer.Core;

namespace PC_Optimizer.Services;

/// <summary>Un módulo de RAM tal como lo informa Windows (SMBIOS). Todo es opcional: nunca se inventa.</summary>
public sealed record MemoryModuleInfo(string? Locator, string? Bank, uint? NominalSpeed, uint? ConfiguredSpeed,
    ulong? CapacityBytes, int? SmbiosType);

/// <summary>Datos crudos de los módulos y, si Windows lo informa, el total de ranuras de la placa.</summary>
public sealed record MemoryHardwareData(IReadOnlyList<MemoryModuleInfo> Modules, int? TotalSlots);

public enum MemorySpeedStatus { Undetermined, BelowNominal, AtNominal, AboveNominal }

/// <summary>
/// Resultado ya redactado para el usuario: velocidad (configurada frente a nominal) y canales.
/// El estado de los canales nunca se afirma: Windows no informa el modo.
/// </summary>
public sealed record MemoryReport(
    MemorySpeedStatus SpeedStatus,
    string SpeedHeadline,
    string SpeedDetail,
    IReadOnlyList<string> ModuleLines,
    string ChannelHeadline,
    string ChannelDetail,
    string? Problem)
{
    public static MemoryReport Unavailable(string reason) => new(
        MemorySpeedStatus.Undetermined,
        "No se puede determinar la velocidad de la RAM",
        reason,
        Array.Empty<string>(),
        "No se puede determinar con certeza si la RAM está en dual channel",
        reason,
        reason);
}

/// <summary>
/// Solo lectura. Detrás de una interfaz para que las pruebas usen datos simulados y nunca
/// consulten el hardware real.
/// </summary>
public interface IMemoryDiagnosticsService
{
    MemoryReport GetReport();
}

/// <summary>
/// Lee Win32_PhysicalMemory y Win32_PhysicalMemoryArray (WMI, solo consulta) y compara la velocidad
/// configurada con la nominal. No cambia la BIOS ni ningún ajuste.
/// </summary>
public sealed class SystemMemoryDiagnosticsService : IMemoryDiagnosticsService
{
    /// <summary>Margen para no marcar diferencias de redondeo entre lo configurado y lo nominal.</summary>
    public const double TolerancePercent = 2.0;

    /// <summary>Valores fuera de este rango se consideran lecturas no válidas.</summary>
    public const uint MaxPlausibleSpeed = 20000;

    private readonly Func<MemoryHardwareData> _reader;

    public SystemMemoryDiagnosticsService(Func<MemoryHardwareData>? reader = null)
        => _reader = reader ?? Read;

    public MemoryReport GetReport()
    {
        try { return Analyze(_reader()); }
        catch (Exception ex)
        {
            return MemoryReport.Unavailable($"No se puede determinar: Windows no permitió leer los módulos de memoria ({ex.GetType().Name}).");
        }
    }

    public static MemoryReport Analyze(MemoryHardwareData data)
    {
        var modules = data.Modules ?? Array.Empty<MemoryModuleInfo>();
        if (modules.Count == 0)
            return MemoryReport.Unavailable("No se puede determinar: Windows no devolvió información de los módulos de memoria.");

        var lines = modules.Select(DescribeModule).ToList();
        var (status, headline, detail) = AnalyzeSpeed(modules);
        var (channelHeadline, channelDetail) = AnalyzeChannels(modules, data.TotalSlots);
        return new MemoryReport(status, headline, detail, lines, channelHeadline, channelDetail, null);
    }

    private static bool IsValid(uint? speed) => speed.HasValue && speed.Value > 0 && speed.Value <= MaxPlausibleSpeed;

    private static MemorySpeedStatus Compare(uint configured, uint nominal)
    {
        double low = nominal * (1 - TolerancePercent / 100.0);
        double high = nominal * (1 + TolerancePercent / 100.0);
        if (configured < low) return MemorySpeedStatus.BelowNominal;
        if (configured > high) return MemorySpeedStatus.AboveNominal;
        return MemorySpeedStatus.AtNominal;
    }

    /// <summary>Tipos SMBIOS LPDDR (memoria de portátil normalmente soldada a la placa, sin perfiles XMP/EXPO).</summary>
    public static bool IsLpddr(int? smbiosType) => smbiosType is 27 or 28 or 29 or 30 or 35;

    private static (MemorySpeedStatus, string, string) AnalyzeSpeed(IReadOnlyList<MemoryModuleInfo> modules)
    {
        var soldered = modules.Any(m => IsLpddr(m.SmbiosType));
        var missing = modules.Count(m => !IsValid(m.ConfiguredSpeed) || !IsValid(m.NominalSpeed));
        if (missing > 0)
        {
            string which = missing == modules.Count ? "de ningún módulo" : $"de {missing} de {modules.Count} módulos";
            return (MemorySpeedStatus.Undetermined,
                "No se puede determinar si la RAM corre por debajo de lo nominal",
                $"Windows no informa la velocidad configurada o la nominal {which}, o el valor no es válido. "
                + "Sin ambos datos la app no compara ni adivina. La app no cambia nada.");
        }

        var statuses = modules.Select(m => Compare(m.ConfiguredSpeed!.Value, m.NominalSpeed!.Value)).Distinct().ToList();
        if (statuses.Count > 1)
        {
            return (MemorySpeedStatus.Undetermined,
                "No se puede determinar: los módulos no coinciden entre sí",
                "Los módulos informan velocidades distintas respecto a lo nominal (por ejemplo, kits mezclados). "
                + "Revisa la lista de módulos. La app no cambia nada.");
        }

        uint configured = modules.Max(m => m.ConfiguredSpeed!.Value);
        uint nominal = modules.Min(m => m.NominalSpeed!.Value);
        const string honestyNote = " La app solo lee: no cambia la BIOS y no promete más FPS.";

        if (soldered)
        {
            const string lpddr = "Esta memoria es LPDDR, normalmente soldada a la placa del portátil: no tiene perfil XMP/EXPO que activar y su velocidad la fija el fabricante, así que no hay nada que cambiar. La app solo lee.";
            return statuses[0] switch
            {
                MemorySpeedStatus.BelowNominal => (MemorySpeedStatus.BelowNominal,
                    $"La RAM funciona a {configured} MHz, por debajo de la velocidad que declara ({nominal} MHz)", lpddr),
                MemorySpeedStatus.AboveNominal => (MemorySpeedStatus.AboveNominal,
                    $"La RAM funciona a {configured} MHz, por encima de la velocidad que declara ({nominal} MHz)", lpddr),
                _ => (MemorySpeedStatus.AtNominal,
                    $"La RAM funciona a su velocidad declarada ({configured} MHz)", lpddr)
            };
        }

        return statuses[0] switch
        {
            MemorySpeedStatus.BelowNominal => (MemorySpeedStatus.BelowNominal,
                $"La RAM funciona a {configured} MHz, por debajo de su velocidad nominal declarada ({nominal} MHz)",
                "Puede deberse a que el perfil XMP/EXPO no está activado en la BIOS, pero también a límites del procesador "
                + "o de la placa (por ejemplo, con varios módulos); en ese caso no hay nada que activar. Si decides probar "
                + "el perfil, se hace desde la BIOS siguiendo el manual de tu placa, y conviene comprobar la estabilidad "
                + "después. El efecto en juegos depende de cada equipo." + honestyNote),
            MemorySpeedStatus.AboveNominal => (MemorySpeedStatus.AboveNominal,
                $"La RAM funciona a {configured} MHz, por encima de la velocidad que el módulo declara ({nominal} MHz)",
                "Suele pasar cuando hay un perfil XMP/EXPO activo y la BIOS informa la velocidad base como nominal, "
                + "pero esta lectura no puede confirmarlo. No hay nada que cambiar." + honestyNote),
            _ => (MemorySpeedStatus.AtNominal,
                $"La RAM funciona a su velocidad nominal declarada ({configured} MHz)",
                "La velocidad configurada coincide con la declarada por el módulo; no hay nada que hacer. Algunos "
                + "fabricantes declaran como nominal la velocidad base estándar, así que esta lectura no confirma si hay "
                + "un perfil XMP/EXPO activo." + honestyNote)
        };
    }

    private static readonly Regex ChannelPattern = new(@"channel\s*([a-z0-9]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static (string, string) AnalyzeChannels(IReadOnlyList<MemoryModuleInfo> modules, int? totalSlots)
    {
        int count = modules.Count;
        var banks = modules.Select(m => m.Bank?.Trim()).Where(b => !string.IsNullOrWhiteSpace(b)).Distinct().ToList();
        var channelIds = modules
            .Select(m => ChannelPattern.Match($"{m.Locator} {m.Bank}"))
            .Where(x => x.Success).Select(x => x.Groups[1].Value.ToUpperInvariant()).Distinct().ToList();

        var detail = new List<string>
        {
            "Windows no informa si la memoria está en dual channel.",
            totalSlots is > 0
                ? $"Módulos detectados: {count}. Ranuras ocupadas: {count} de {totalSlots}."
                : $"Módulos detectados: {count}. Total de ranuras: no informado por Windows.",
            banks.Count > 0 ? $"Bancos informados: {string.Join(", ", banks)}." : "Bancos informados: ninguno."
        };

        if (count == 1)
            detail.Add("Con un solo módulo, en un PC de escritorio normalmente funciona en un solo canal; en portátiles con memoria soldada o con DDR5 puede ser distinto.");
        else
            detail.Add("Tener varios módulos no garantiza dual channel: depende de las ranuras elegidas y de la placa. El manual de la placa indica qué ranuras usar.");

        if (channelIds.Count >= 2)
            detail.Add($"Los nombres de ranura mencionan canales distintos ({string.Join(", ", channelIds)}); es una pista del fabricante, no una confirmación.");

        detail.Add("Para saberlo con certeza puedes usar una herramienta de información del hardware como CPU-Z (pestaña Memory). La app no cambia nada ni promete mejoras.");

        return ("No se puede determinar con certeza si la RAM está en dual channel", string.Join(" ", detail));
    }

    private static string DescribeModule(MemoryModuleInfo m)
    {
        string name = string.IsNullOrWhiteSpace(m.Locator) ? "Ranura no informada" : m.Locator!.Trim();
        string capacity = m.CapacityBytes is > 0 ? ByteSize.Format((long)Math.Min(m.CapacityBytes.Value, long.MaxValue)) : "capacidad no informada";
        string type = m.SmbiosType switch
        {
            24 => "DDR3",
            26 => "DDR4",
            34 => "DDR5",
            27 => "LPDDR (soldada)",
            28 => "LPDDR2 (soldada)",
            29 => "LPDDR3 (soldada)",
            30 => "LPDDR4 (soldada)",
            35 => "LPDDR5 (soldada)",
            _ => "tipo no informado"
        };
        string configured = IsValid(m.ConfiguredSpeed) ? $"{m.ConfiguredSpeed} MHz" : "no informada";
        string nominal = IsValid(m.NominalSpeed) ? $"{m.NominalSpeed} MHz" : "no informada";
        return $"{name} · {capacity} · {type} · configurada: {configured} · nominal: {nominal}";
    }

    private static MemoryHardwareData Read()
    {
        var modules = new List<MemoryModuleInfo>();
        using (var searcher = new ManagementObjectSearcher(
            "SELECT BankLabel, DeviceLocator, Capacity, Speed, ConfiguredClockSpeed, SMBIOSMemoryType FROM Win32_PhysicalMemory"))
        {
            foreach (var item in searcher.Get())
            {
                using var module = item as ManagementObject;
                if (module == null) continue;
                modules.Add(new MemoryModuleInfo(
                    module["DeviceLocator"]?.ToString(),
                    module["BankLabel"]?.ToString(),
                    ToUInt(module["Speed"]),
                    ToUInt(module["ConfiguredClockSpeed"]),
                    ToULong(module["Capacity"]),
                    (int?)ToUInt(module["SMBIOSMemoryType"])));
            }
        }

        int? slots = null;
        try
        {
            int sum = 0;
            using var arrays = new ManagementObjectSearcher("SELECT MemoryDevices FROM Win32_PhysicalMemoryArray");
            foreach (var item in arrays.Get())
            {
                using var array = item as ManagementObject;
                sum += (int)(ToUInt(array?["MemoryDevices"]) ?? 0);
            }
            if (sum > 0) slots = sum;
        }
        catch
        {
            // El total de ranuras es opcional: se muestra como «no informado».
        }

        return new MemoryHardwareData(modules, slots);
    }

    private static uint? ToUInt(object? value)
    {
        if (value == null) return null;
        try { return Convert.ToUInt32(value); } catch { return null; }
    }

    private static ulong? ToULong(object? value)
    {
        if (value == null) return null;
        try { return Convert.ToUInt64(value); } catch { return null; }
    }
}
