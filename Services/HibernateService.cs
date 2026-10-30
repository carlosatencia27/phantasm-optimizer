using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace PC_Optimizer.Services;

public interface IHibernateService
{
    bool IsHibernateEnabled();
    long GetHiberfileSizeBytes();
    bool SetHibernate(bool enable);
}

public class HibernateService : IHibernateService
{
    private const string HiberfilePath = @"C:\hiberfil.sys";

    public bool IsHibernateEnabled()
    {
        try
        {
            if (File.Exists(HiberfilePath))
                return true;

            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power", writable: false);
            if (key?.GetValue("HibernateEnabled") is int i)
                return i == 1;
        }
        catch { }

        return false;
    }

    public long GetHiberfileSizeBytes()
    {
        try
        {
            if (File.Exists(HiberfilePath))
            {
                var fi = new FileInfo(HiberfilePath);
                return fi.Length;
            }
        }
        catch { }

        return -1;
    }

    public bool SetHibernate(bool enable)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powercfg",
                Arguments = enable ? "/hibernate on" : "/hibernate off",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi);
            if (proc == null || !proc.WaitForExit(5000))
            {
                try { proc?.Kill(); } catch { }
                return false;
            }
            return proc?.ExitCode == 0;
        }
        catch { return false; }
    }
}
