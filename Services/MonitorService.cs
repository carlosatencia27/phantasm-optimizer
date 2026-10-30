using System;
using System.Runtime.InteropServices;
using PC_Optimizer.Services;

namespace PC_Optimizer.Services;

public interface IMonitorService
{
    double? GetCurrentRefreshRate();
    double? GetMaxRefreshRate();
    string GetStatusDescription();
}

public class MonitorService : IMonitorService
{
    private const int EnumCurrentSettings = -1;

    [DllImport("user32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public short dmOrientation;
        public short dmPaperSize;
        public short dmPaperLength;
        public short dmPaperWidth;
        public short dmScale;
        public short dmCopies;
        public short dmDefaultSource;
        public short dmPrintQuality;
        public POINTL dmPosition;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINTL
    {
        public int X;
        public int Y;
    }

    public double? GetCurrentRefreshRate()
    {
        try
        {
            var mode = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
            if (EnumDisplaySettings(null, EnumCurrentSettings, ref mode))
                return mode.dmDisplayFrequency;
        }
        catch { }
        return null;
    }

    public double? GetMaxRefreshRate()
    {
        double? max = null;
        try
        {
            var current = GetCurrentRefreshRate();
            int currentWidth = 0, currentHeight = 0, currentBits = 0;
            var currentMode = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
            if (EnumDisplaySettings(null, EnumCurrentSettings, ref currentMode))
            {
                currentWidth = currentMode.dmPelsWidth;
                currentHeight = currentMode.dmPelsHeight;
                currentBits = currentMode.dmBitsPerPel;
            }

            for (int i = 0; ; i++)
            {
                var mode = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
                if (!EnumDisplaySettings(null, i, ref mode))
                    break;

                // Preferir modos que coincidan con la resolución y profundidad de color actual
                bool matchesCurrent = currentWidth > 0 &&
                                      mode.dmPelsWidth == currentWidth &&
                                      mode.dmPelsHeight == currentHeight &&
                                      mode.dmBitsPerPel == currentBits;
                if (!matchesCurrent && current != null)
                    continue;

                var freq = mode.dmDisplayFrequency;
                if (freq > 0 && (max == null || freq > max))
                    max = freq;
            }

            // Si no se encontró ningún modo coincidente con la resolución actual,
            // hacer un segundo pasaje sobre todos los modos para no dejar "máxima no disponible"
            if (max == null && current != null)
            {
                for (int i = 0; ; i++)
                {
                    var mode = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
                    if (!EnumDisplaySettings(null, i, ref mode))
                        break;
                    var freq = mode.dmDisplayFrequency;
                    if (freq > 0 && (max == null || freq > max))
                        max = freq;
                }
            }
        }
        catch { }
        return max;
    }

    public string GetStatusDescription()
    {
        var current = GetCurrentRefreshRate();
        var max = GetMaxRefreshRate();

        if (current == null)
            return "No se pudo obtener la frecuencia de refresco del monitor.";

        if (max == null)
            return $"El monitor está funcionando a {current:0.0} Hz. La frecuencia máxima soportada no está disponible.";

        if (current >= max)
            return $"El monitor ya está configurado a su frecuencia máxima ({max:0.0} Hz).";

        return $"El monitor está funcionando a {current:0.0} Hz, pero soporta hasta {max:0.0} Hz. Puede aumentarla en Configuración de pantalla > Configuración de pantalla avanzada.";
    }
}
