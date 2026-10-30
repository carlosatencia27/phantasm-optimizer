using Microsoft.Win32;

namespace PC_Optimizer.Services;

public interface IHagsService
{
    bool IsSupported();
    bool IsEnabled();
    int? GetRawMode();
    bool SetRawMode(int value, out string errorMessage);
    bool DeleteRawMode(out string errorMessage);
    string GetStatusDescription();
}

public class HagsService : IHagsService
{
    // Clave verificada: HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\HwSchMode
    // REG_DWORD: 2 = HAGS activado, 1 = HAGS desactivado, ausente = predeterminado del sistema
    // Fuente: documentación de controladores WDDM, Microsoft Support, NinjaOne
    private const string HagsKey   = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
    private const string HagsValue = "HwSchMode";
    private const int    HagsEnabled  = 2;

    public bool IsSupported()
    {
        try
        {
            // HAGS disponible desde Windows 10 build 19041 (2004) si el driver WDDM lo soporta.
            if (Environment.OSVersion.Version.Build < 19041)
                return false;

            // La clave GraphicsDrivers y el valor HwSchMode deben existir para confirmar soporte
            using var key = Registry.LocalMachine.OpenSubKey(HagsKey, writable: false);
            if (key == null) return false;

            var value = key.GetValue(HagsValue);
            return value is int;
        }
        catch { return false; }
    }

    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(HagsKey, writable: false);
            if (key == null) return false;

            var value = key.GetValue(HagsValue);
            // Si el valor no existe, el sistema usa el predeterminado del driver
            return value is int i && i == HagsEnabled;
        }
        catch { return false; }
    }

    public int? GetRawMode()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(HagsKey, writable: false);
            return key?.GetValue(HagsValue) as int?;
        }
        catch
        {
            return null;
        }
    }

    public bool SetRawMode(int value, out string errorMessage)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(HagsKey, writable: true);
            if (key == null)
            {
                errorMessage = "No se pudo abrir la configuración de HAGS para escritura.";
                return false;
            }

            key.SetValue(HagsValue, value, RegistryValueKind.DWord);
            errorMessage = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = PC_Optimizer.Services.UserErrorText.Of(ex);
            return false;
        }
    }

    public bool DeleteRawMode(out string errorMessage)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(HagsKey, writable: true);
            if (key == null)
            {
                errorMessage = "No se pudo abrir la configuración de HAGS para escritura.";
                return false;
            }

            key.DeleteValue(HagsValue, throwOnMissingValue: false);
            errorMessage = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = PC_Optimizer.Services.UserErrorText.Of(ex);
            return false;
        }
    }

    public string GetStatusDescription()
    {
        if (!IsSupported())
            return "HAGS no es compatible con este sistema (requiere Windows 10 2004+ y driver WDDM compatible).";
        if (IsEnabled())
            return "HAGS está habilitado (HwSchMode=2).";
        return "HAGS está disponible pero deshabilitado. Se puede activar desde aquí o en Configuración > Sistema > Pantalla > Gráficos > Configuración predeterminada de gráficos.";
    }
}
