using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;

namespace PC_Optimizer.Services;

public enum DeviceKind { Unknown, Desktop, Laptop }

/// <summary>
/// Qué tipo de equipo es y su estado de energía. <see cref="OnAcPower"/> es null cuando Windows no lo
/// informa; <see cref="BatteryPercent"/> es null si no hay batería o no se pudo leer.
/// </summary>
public sealed record DeviceInfo(DeviceKind Kind, bool HasBattery, bool? OnAcPower, int? BatteryPercent, string Summary)
{
    public static DeviceInfo Unknown => new(DeviceKind.Unknown, false, null, null, "Tipo de equipo: no se puede determinar");
}

/// <summary>Solo lectura; detrás de una interfaz para que las pruebas usen datos simulados.</summary>
public interface IDeviceInfoService
{
    DeviceInfo Get();
}

/// <summary>
/// Combina la batería con el tipo de chasis que declara el equipo (SMBIOS) para no confundir un PC de
/// escritorio con SAI con un portátil.
/// </summary>
public static class DeviceClassifier
{
    // Tipos de chasis SMBIOS (Win32_SystemEnclosure.ChassisTypes).
    private static readonly Dictionary<int, string> LaptopChassis = new()
    {
        [8] = "Portátil (Portable)", [9] = "Portátil (Laptop)", [10] = "Portátil (Notebook)", [11] = "De mano (Hand Held)",
        [14] = "Subportátil (Sub Notebook)", [30] = "Tableta (Tablet)", [31] = "Convertible", [32] = "Desmontable (Detachable)"
    };

    private static readonly Dictionary<int, string> DesktopChassis = new()
    {
        [3] = "Escritorio (Desktop)", [4] = "Escritorio compacto (Low Profile)", [5] = "Pizza Box", [6] = "Mini torre",
        [7] = "Torre", [13] = "Todo en uno (All in One)", [15] = "Escritorio de poco espacio", [16] = "Lunch Box",
        [23] = "Servidor en rack", [34] = "PC integrado", [35] = "Mini PC", [36] = "PC en stick"
    };

    public static DeviceInfo Classify(bool hasBattery, bool? onAcPower, int? batteryPercent, IReadOnlyList<int>? chassisTypes)
    {
        chassisTypes ??= Array.Empty<int>();
        var laptop = chassisTypes.Where(LaptopChassis.ContainsKey).Select(c => LaptopChassis[c]).FirstOrDefault();
        var desktop = chassisTypes.Where(DesktopChassis.ContainsKey).Select(c => DesktopChassis[c]).FirstOrDefault();

        DeviceKind kind;
        string chassisText;
        if (laptop != null)
        {
            kind = DeviceKind.Laptop;
            chassisText = laptop;
        }
        else if (desktop != null)
        {
            kind = DeviceKind.Desktop;
            chassisText = desktop;
        }
        else if (hasBattery)
        {
            // Chasis no informado o genérico: una batería sugiere portátil, pero no es seguro.
            kind = DeviceKind.Laptop;
            chassisText = "chasis no informado; se supone portátil por la batería";
        }
        else
        {
            return new DeviceInfo(DeviceKind.Unknown, false, onAcPower, null,
                "Tipo de equipo: no se puede determinar (Windows no informa el chasis y no hay batería)");
        }

        return new DeviceInfo(kind, hasBattery, onAcPower, batteryPercent, Describe(kind, chassisText, hasBattery, onAcPower, batteryPercent));
    }

    private static string Describe(DeviceKind kind, string chassis, bool hasBattery, bool? onAc, int? percent)
    {
        var type = kind == DeviceKind.Laptop ? "Portátil" : "Equipo de escritorio";
        string battery;
        if (!hasBattery) battery = "sin batería";
        else
        {
            var level = percent.HasValue ? $" ({percent} %)" : string.Empty;
            battery = onAc switch
            {
                true => $"con batería, conectado a la corriente{level}",
                false => $"funcionando con batería{level}",
                _ => $"con batería, estado de la corriente no informado{level}"
            };
            if (kind == DeviceKind.Desktop) battery = "batería detectada (posible SAI; el chasis declarado es de escritorio): " + battery;
        }
        return $"{type} · {battery} · chasis: {chassis}";
    }
}

public sealed class SystemDeviceInfoService : IDeviceInfoService
{
    private readonly Func<(bool HasBattery, bool? OnAc, int? Percent)> _power;
    private readonly Func<IReadOnlyList<int>> _chassis;

    public SystemDeviceInfoService(Func<(bool, bool?, int?)>? power = null, Func<IReadOnlyList<int>>? chassis = null)
    {
        _power = power ?? ReadPower;
        _chassis = chassis ?? ReadChassis;
    }

    public DeviceInfo Get()
    {
        try
        {
            var (hasBattery, onAc, percent) = _power();
            IReadOnlyList<int> chassis;
            try { chassis = _chassis(); } catch { chassis = Array.Empty<int>(); }
            return DeviceClassifier.Classify(hasBattery, onAc, percent, chassis);
        }
        catch
        {
            return DeviceInfo.Unknown;
        }
    }

    private static (bool, bool?, int?) ReadPower()
    {
        var status = System.Windows.Forms.SystemInformation.PowerStatus;
        var hasBattery = !status.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.NoSystemBattery)
            && status.BatteryChargeStatus != System.Windows.Forms.BatteryChargeStatus.Unknown;
        bool? onAc = status.PowerLineStatus switch
        {
            System.Windows.Forms.PowerLineStatus.Online => true,
            System.Windows.Forms.PowerLineStatus.Offline => false,
            _ => null
        };
        int? percent = hasBattery && status.BatteryLifePercent is >= 0f and <= 1f ? (int)Math.Round(status.BatteryLifePercent * 100) : null;
        return (hasBattery, onAc, percent);
    }

    private static IReadOnlyList<int> ReadChassis()
    {
        var result = new List<int>();
        using var searcher = new ManagementObjectSearcher("SELECT ChassisTypes FROM Win32_SystemEnclosure");
        foreach (var item in searcher.Get())
        {
            using var enclosure = item as ManagementObject;
            if (enclosure?["ChassisTypes"] is Array types)
                foreach (var t in types) result.Add(Convert.ToInt32(t));
        }
        return result;
    }
}

/// <summary>Una tarjeta gráfica detectada (solo lectura).</summary>
public sealed record GpuInfo(string Name, string? Vendor);

public interface IGpuInfoService
{
    IReadOnlyList<GpuInfo> GetGpus();
}

/// <summary>Lee las GPU con WMI (Win32_VideoController) y descarta adaptadores virtuales o de pantalla remota.</summary>
public sealed class SystemGpuInfoService : IGpuInfoService
{
    private static readonly string[] VirtualMarkers =
    {
        "microsoft basic", "remote display", "hyper-v", "virtual", "vmware", "virtualbox", "citrix", "parsec", "mirage",
        "meta virtual", "indirect display"
    };

    private readonly Func<IReadOnlyList<GpuInfo>> _reader;

    public SystemGpuInfoService(Func<IReadOnlyList<GpuInfo>>? reader = null) => _reader = reader ?? Read;

    public IReadOnlyList<GpuInfo> GetGpus()
    {
        try { return Filter(_reader()); }
        catch { return Array.Empty<GpuInfo>(); }
    }

    public static IReadOnlyList<GpuInfo> Filter(IReadOnlyList<GpuInfo>? gpus)
        => (gpus ?? Array.Empty<GpuInfo>())
            .Where(g => !string.IsNullOrWhiteSpace(g.Name)
                && !VirtualMarkers.Any(m => g.Name.Contains(m, StringComparison.OrdinalIgnoreCase)))
            .GroupBy(g => g.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

    private static IReadOnlyList<GpuInfo> Read()
    {
        var result = new List<GpuInfo>();
        using var searcher = new ManagementObjectSearcher("SELECT Name, AdapterCompatibility FROM Win32_VideoController");
        foreach (var item in searcher.Get())
        {
            using var controller = item as ManagementObject;
            var name = controller?["Name"]?.ToString();
            if (!string.IsNullOrWhiteSpace(name))
                result.Add(new GpuInfo(name!, controller?["AdapterCompatibility"]?.ToString()));
        }
        return result;
    }
}
