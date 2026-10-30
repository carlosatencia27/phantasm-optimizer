using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace PC_Optimizer.Services;

public class AdminService : IAdminService
{
    [DllImport("shell32.dll")]
    private static extern bool IsUserAnAdmin();

    public bool IsRunningAsAdmin => IsUserAnAdmin();

    public bool IsElevationSupported => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public void RequestElevation()
    {
        if (IsRunningAsAdmin)
            return;

        try
        {
            var exeName = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exeName))
                return;

            var startInfo = new ProcessStartInfo
            {
                UseShellExecute = true,
                WorkingDirectory = Environment.CurrentDirectory,
                FileName = exeName,
                Verb = "runas"
            };

            Process.Start(startInfo);
            global::System.Windows.Application.Current?.Shutdown();
        }
        catch
        {
            // El usuario denegó la elevación o no se pudo elevar
        }
    }
}
