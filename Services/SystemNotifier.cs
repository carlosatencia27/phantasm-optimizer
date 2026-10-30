using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace PC_Optimizer.Services;

/// <summary>
/// Avisa a Windows de que un ajuste cambió para que lo aplique con su mecanismo estándar.
/// Siempre en segundo plano, con tiempo máximo, y detrás de esta interfaz para que las pruebas
/// no envíen notificaciones reales.
/// </summary>
public interface ISystemNotifier
{
    /// <summary>Aplica el retardo de menús y avisa a las ventanas (SystemParametersInfo + WM_SETTINGCHANGE).</summary>
    void NotifyMenuShowDelay(int milliseconds);

    /// <summary>Avisa del cambio de tema/transparencias (WM_SETTINGCHANGE «ImmersiveColorSet»).</summary>
    void NotifyTransparencyChanged();
}

/// <summary>No hace nada: valor por defecto del motor cuando no se inyecta un notificador.</summary>
public sealed class NullSystemNotifier : ISystemNotifier
{
    public void NotifyMenuShowDelay(int milliseconds) { }
    public void NotifyTransparencyChanged() { }
}

/// <summary>
/// Implementación real. Usa SendMessageTimeout con SMTO_ABORTIFHUNG y un límite de 1,5 s para
/// no colgar la ventana si alguna ventana de Windows no responde.
/// </summary>
public sealed class SystemNotifier : ISystemNotifier
{
    private const uint SPI_SETMENUSHOWDELAY = 0x006B;
    private const uint SPIF_SENDCHANGE = 0x02;
    private const uint WM_SETTINGCHANGE = 0x001A;
    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private static readonly IntPtr HwndBroadcast = new(0xFFFF);
    private const uint TimeoutMilliseconds = 1500;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, string lParam,
        uint flags, uint timeout, out IntPtr result);

    public void NotifyMenuShowDelay(int milliseconds) => RunInBackground(() =>
    {
        // Sin SPIF_UPDATEINIFILE a propósito: el valor ya lo escribió el ajuste (y revertirlo puede
        // significar que la clave no existía). Solo se aplica y se anuncia.
        SystemParametersInfo(SPI_SETMENUSHOWDELAY, (uint)Math.Max(0, milliseconds), IntPtr.Zero, SPIF_SENDCHANGE);
    });

    public void NotifyTransparencyChanged() => RunInBackground(() => Broadcast("ImmersiveColorSet"));

    private static void Broadcast(string area) =>
        SendMessageTimeout(HwndBroadcast, WM_SETTINGCHANGE, IntPtr.Zero, area,
            SMTO_ABORTIFHUNG, TimeoutMilliseconds, out _);

    private static void RunInBackground(Action action)
    {
        _ = Task.Run(() =>
        {
            try { action(); }
            catch { /* una notificación no debe romper un cambio ya aplicado */ }
        });
    }
}
