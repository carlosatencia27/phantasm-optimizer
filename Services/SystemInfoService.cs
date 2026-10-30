using System;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using PC_Optimizer.Models;

namespace PC_Optimizer.Services;

public class SystemInfoService : ISystemInfoService
{
    private readonly PerformanceCounter? _cpuCounter;
    private readonly PerformanceCounter? _ramCounter;

    // Caché de datos estáticos (se leen solo una vez)
    private string? _cachedCpuName;
    private string? _cachedGpuName;

    public SystemInfoService()
    {
        try
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _ramCounter = new PerformanceCounter("Memory", "% Committed Bytes In Use");
            // Primera lectura puede ser 0; inicializamos para que la siguiente sea correcta
            _ = _cpuCounter.NextValue();
            _ = _ramCounter.NextValue();
        }
        catch
        {
            _cpuCounter = null;
            _ramCounter = null;
        }
    }

    public SystemMetrics GetCurrentMetrics()
    {
        var m = new SystemMetrics { Timestamp = DateTime.Now };

        // ── CPU ──────────────────────────────────────────────────────────────
        try { m.CpuUsagePercent = Math.Round((double)(_cpuCounter?.NextValue() ?? 0), 1); }
        catch { m.CpuUsagePercent = 0; }

        m.CpuName = GetCpuName();

        // ── RAM mediante GlobalMemoryStatusEx; no consulta WMI en el ciclo vivo ─
        try
        {
            var memory = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (GlobalMemoryStatusEx(ref memory))
            {
                m.RamTotalBytes = (long)memory.ullTotalPhys;
                m.RamUsedBytes = (long)(memory.ullTotalPhys - memory.ullAvailPhys);
            }
        }
        catch { }

        // ── Disco del sistema mediante DriveInfo; no consulta WMI en el ciclo vivo ─
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
            m.DiskTotalBytes = drive.TotalSize;
            m.DiskUsedBytes = drive.TotalSize - drive.AvailableFreeSpace;
        }
        catch { }

        // ── GPU ──────────────────────────────────────────────────────────────
        m.GpuName = GetGpuName();

        // La temperatura no se consulta en cada segundo: sin una fuente ligera queda no disponible.
        m.TemperatureCelsius = null;

        // ── Monitor: frecuencia actual vía EnumDisplaySettings (user32) ──────
        // Fuente: Microsoft Docs · EnumDisplaySettings function
        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumdisplaysettingsa
        try
        {
            var dm = new DEVMODE();
            dm.dmSize = (ushort)Marshal.SizeOf(dm);
            if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm) && dm.dmDisplayFrequency > 1)
                m.CurrentRefreshHz = dm.dmDisplayFrequency;

            // Frecuencia máxima: iterar todos los modos y tomar el máximo a la resolución actual.
            // Recorrer todos los modos cada segundo es caro y el resultado solo cambia si cambia la
            // resolución: se guarda por resolución (mismo valor, mucho menos CPU en reposo).
            var resolutionKey = (dm.dmPelsWidth, dm.dmPelsHeight);
            double? maxHz = null;
            if (_maxHzKey == resolutionKey) { m.MaxRefreshHz = _maxHzCached; goto afterModes; }
            int modeNum = 0;
            var dmMode = new DEVMODE { dmSize = (ushort)Marshal.SizeOf(dm) };
            while (EnumDisplaySettings(null, modeNum, ref dmMode))
            {
                if (dmMode.dmPelsWidth  == dm.dmPelsWidth &&
                    dmMode.dmPelsHeight == dm.dmPelsHeight &&
                    dmMode.dmDisplayFrequency > 1)
                {
                    double hz = dmMode.dmDisplayFrequency;
                    if (maxHz == null || hz > maxHz) maxHz = hz;
                }
                modeNum++;
            }
            _maxHzKey = resolutionKey; _maxHzCached = maxHz;
            m.MaxRefreshHz = maxHz;
        afterModes:;
        }
        catch { }

        // ── Red: velocidad de bytes en tiempo real vía NetworkInterface ────────
        try
        {
            var (speedText, usagePct, desc) = GetNetworkThroughput();
            m.NetworkSpeed = speedText;
            m.NetworkUsagePercent = usagePct;
            m.NetworkDescription = desc;
        }
        catch (Exception ex)
        {
            m.NetworkSpeed = "No disponible";
            m.NetworkDescription = $"Error de medición: {PC_Optimizer.Services.UserErrorText.Of(ex)}";
        }

        return m;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private (uint, uint)? _maxHzKey; private double? _maxHzCached;

    private string GetCpuName()
    {
        if (_cachedCpuName != null) return _cachedCpuName;
        try
        {
            using var s = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
            foreach (ManagementObject o in s.Get())
            {
                _cachedCpuName = Convert.ToString(o["Name"]) ?? string.Empty;
                return _cachedCpuName;
            }
        }
        catch { }
        _cachedCpuName = string.Empty;
        return _cachedCpuName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    private string GetGpuName()
    {
        if (_cachedGpuName != null) return _cachedGpuName;
        try
        {
            using var s = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
            foreach (ManagementObject o in s.Get())
            {
                _cachedGpuName = Convert.ToString(o["Name"]) ?? string.Empty;
                return _cachedGpuName;
            }
        }
        catch { }
        _cachedGpuName = string.Empty;
        return _cachedGpuName;
    }

    // ── P/Invoke: EnumDisplaySettings ────────────────────────────────────────
    // Fuente: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumdisplaysettingsa
    private const int ENUM_CURRENT_SETTINGS = -1;

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public ushort dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public uint dmFields;
        public int dmPositionX, dmPositionY;
        public uint dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        // Campos opcionales opcionales (relleno para el tamaño correcto de la estructura)
        public uint dmICMMethod, dmICMIntent, dmMediaType, dmDitherType;
        public uint dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    // ── Medición de rendimiento de red ──────────────────────────────────────
    private static long _lastBytes = 0;
    private static DateTime _lastNetTime = DateTime.MinValue;

    private static (string speedText, double usagePct, string desc) GetNetworkThroughput()
    {
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                            n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                            n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .ToList();

            if (interfaces.Count == 0)
            {
                return ("Sin conexión", 0, "No hay adaptadores de red activos");
            }

            long totalBytes = 0;
            string mainAdapterName = interfaces[0].Name;
            long linkSpeed = interfaces[0].Speed;

            foreach (var ni in interfaces)
            {
                var stats = ni.GetIPStatistics();
                totalBytes += stats.BytesReceived + stats.BytesSent;
            }

            DateTime now = DateTime.Now;
            if (_lastNetTime == DateTime.MinValue || _lastBytes == 0)
            {
                _lastNetTime = now;
                _lastBytes = totalBytes;
                string linkText = linkSpeed > 0 ? FormatLinkSpeed(linkSpeed) : "Activo";
                // Primera muestra: todavía no hay intervalo para calcular la velocidad (B/s, KB/s o MB/s)
                return ("Midiendo…", 0, $"{mainAdapterName} · {linkText}");
            }

            double seconds = (now - _lastNetTime).TotalSeconds;
            if (seconds <= 0.1)
            {
                string linkText = linkSpeed > 0 ? FormatLinkSpeed(linkSpeed) : "Activo";
                return ("Midiendo…", 0, $"{mainAdapterName} · {linkText}");
            }

            long bytesDelta = totalBytes - _lastBytes;
            _lastNetTime = now;
            _lastBytes = totalBytes;

            double bytesPerSec = bytesDelta / seconds;
            string throughputText = FormatThroughput(bytesPerSec);

            double usagePct = 0;
            if (linkSpeed > 0)
            {
                double bitsPerSec = bytesPerSec * 8;
                usagePct = Math.Min(100.0, Math.Max(0.0, (bitsPerSec / linkSpeed) * 100.0));
            }

            string adapterInfo = linkSpeed > 0 ? $"{mainAdapterName} · {FormatLinkSpeed(linkSpeed)}" : mainAdapterName;
            return (throughputText, Math.Round(usagePct, 1), adapterInfo);
        }
        catch (Exception ex)
        {
            return ("No disponible", 0, $"Error al leer interfaz: {PC_Optimizer.Services.UserErrorText.Of(ex)}");
        }
    }

    private static string FormatLinkSpeed(long bitsPerSec)
    {
        if (bitsPerSec >= 1_000_000_000) return $"{bitsPerSec / 1_000_000_000.0:0.#} Gbps";
        if (bitsPerSec >= 1_000_000)     return $"{bitsPerSec / 1_000_000.0:0.#} Mbps";
        if (bitsPerSec >= 1_000)         return $"{bitsPerSec / 1_000.0:0.#} Kbps";
        return $"{bitsPerSec} bps";
    }

    private static string FormatThroughput(double bytesPerSec)
    {
        if (bytesPerSec >= 1_048_576) return $"{bytesPerSec / 1_048_576.0:0.0} MB/s";
        if (bytesPerSec >= 1_024)     return $"{bytesPerSec / 1_024.0:0} KB/s";
        return $"{bytesPerSec:0} B/s";
    }
}
