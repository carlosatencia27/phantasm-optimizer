using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PC_Optimizer.Services;

public interface IMouseService
{
    (string? speed, string? threshold1, string? threshold2) GetMouseSettings();
    bool SetMouseSettings(string? speed, string? threshold1, string? threshold2);
}

public class MouseService : IMouseService
{
    private const string SubKey = @"Control Panel\Mouse";
    private const uint SPI_SETMOUSE = 0x0004;
    private const uint SPIF_UPDATEINIFILE = 0x01;
    private const uint SPIF_SENDCHANGE = 0x02;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, int[] pvParam, uint fWinIni);

    public (string? speed, string? threshold1, string? threshold2) GetMouseSettings()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(SubKey, writable: false);
            var speed = key?.GetValue("MouseSpeed")?.ToString();
            var t1 = key?.GetValue("MouseThreshold1")?.ToString();
            var t2 = key?.GetValue("MouseThreshold2")?.ToString();
            return (speed, t1, t2);
        }
        catch
        {
            return (null, null, null);
        }
    }

    public bool SetMouseSettings(string? speed, string? threshold1, string? threshold2)
    {
        try
        {
            int s = int.TryParse(speed, out var ispeed) ? ispeed : 0;
            int t1 = int.TryParse(threshold1, out var it1) ? it1 : 0;
            int t2 = int.TryParse(threshold2, out var it2) ? it2 : 0;

            // SPI_SETMOUSE espera { umbral 1, umbral 2, aceleración }.
            int[] mouseParams = new int[] { t1, t2, s };
            bool applied = SystemParametersInfo(SPI_SETMOUSE, 0, mouseParams, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
            if (!applied) return false;

            // SPI_SETMOUSE también persiste valores. Reaplica la presencia exacta solicitada,
            // incluyendo la eliminación de aquellos valores que originalmente no existían.
            using var key = Registry.CurrentUser.OpenSubKey(SubKey, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(SubKey);
            if (speed == null) key.DeleteValue("MouseSpeed", false); else key.SetValue("MouseSpeed", speed);
            if (threshold1 == null) key.DeleteValue("MouseThreshold1", false); else key.SetValue("MouseThreshold1", threshold1);
            if (threshold2 == null) key.DeleteValue("MouseThreshold2", false); else key.SetValue("MouseThreshold2", threshold2);
            return true;
        }
        catch { return false; }
    }
}
