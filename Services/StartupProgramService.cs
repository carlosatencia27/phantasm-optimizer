using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace PC_Optimizer.Services;

public interface IStartupProgramService
{
    IReadOnlyList<StartupProgram> GetStartupPrograms();
    byte[]? GetApprovalValue(string keyPath, string valueName);
    bool SetApprovalValue(string keyPath, string valueName, byte[] value);
    bool DeleteApprovalValue(string keyPath, string valueName);
    bool Disable(string keyPath, string valueName);
    bool Enable(string keyPath, string valueName, string valueData);
}

public class StartupProgram
{
    public string KeyPath { get; set; } = string.Empty;
    public string ValueName { get; set; } = string.Empty;
    public string ValueData { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public bool IsLocalMachine => KeyPath.StartsWith(@"HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase);
}

public class StartupProgramService : IStartupProgramService
{
    private static readonly string[] RunKeys =
    {
        @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run",
        @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Run",
        @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\RunOnce",
        @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\RunOnce"
    };

    private static readonly byte[] StartupApprovedEnabled = { 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

    public IReadOnlyList<StartupProgram> GetStartupPrograms()
    {
        var programs = new List<StartupProgram>();

        foreach (var keyPath in RunKeys)
        {
            try
            {
                using var key = OpenRunKey(keyPath, writable: false);
                if (key == null) continue;

                foreach (var valueName in key.GetValueNames())
                {
                    // Excluir Seguridad de Windows (SecurityHealth)
                    if (valueName.Equals("SecurityHealth", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var valueData = key.GetValue(valueName)?.ToString() ?? string.Empty;
                    bool isEnabled = IsStartupApprovedEnabled(keyPath, valueName);

                    programs.Add(new StartupProgram
                    {
                        KeyPath = keyPath,
                        ValueName = valueName,
                        ValueData = valueData,
                        Name = valueName,
                        Publisher = GetPublisherFromFile(valueData),
                        Enabled = isEnabled
                    });
                }
            }
            catch { }
        }

        return programs;
    }

    public bool Disable(string keyPath, string valueName)
    {
        try
        {
            var approvedKey = GetStartupApprovedKeyPath(keyPath);
            if (approvedKey == null) return false;
            using var key = OpenStartupApprovedKey(approvedKey, writable: true);
            if (key == null) return false;

            var disabled = new byte[12];
            disabled[0] = 0x03; // 03 = deshabilitado en Windows Task Manager
            key.SetValue(valueName, disabled, RegistryValueKind.Binary);
            return true;
        }
        catch { return false; }
    }

    public byte[]? GetApprovalValue(string keyPath, string valueName)
    {
        try
        {
            var approvedKey = GetStartupApprovedKeyPath(keyPath);
            if (approvedKey == null) return null;
            using var key = OpenStartupApprovedKey(approvedKey, writable: false);
            return key?.GetValue(valueName) is byte[] value ? (byte[])value.Clone() : null;
        }
        catch { return null; }
    }

    public bool SetApprovalValue(string keyPath, string valueName, byte[] value)
    {
        try
        {
            var approvedKey = GetStartupApprovedKeyPath(keyPath);
            if (approvedKey == null) return false;
            using var key = OpenStartupApprovedKey(approvedKey, writable: true);
            if (key == null) return false;
            key.SetValue(valueName, value, RegistryValueKind.Binary);
            return true;
        }
        catch { return false; }
    }

    public bool DeleteApprovalValue(string keyPath, string valueName)
    {
        try
        {
            var approvedKey = GetStartupApprovedKeyPath(keyPath);
            if (approvedKey == null) return false;
            using var key = OpenStartupApprovedKey(approvedKey, writable: true);
            if (key == null) return false;
            key.DeleteValue(valueName, throwOnMissingValue: false);
            return true;
        }
        catch { return false; }
    }

    public bool Enable(string keyPath, string valueName, string valueData)
    {
        try
        {
            var approvedKey = GetStartupApprovedKeyPath(keyPath);
            if (approvedKey == null) return false;
            using var key = OpenStartupApprovedKey(approvedKey, writable: true);
            if (key == null) return false;

            key.SetValue(valueName, StartupApprovedEnabled, RegistryValueKind.Binary);
            return true;
        }
        catch { return false; }
    }

    public static bool IsStartupApprovedEnabled(string keyPath, string valueName)
    {
        try
        {
            var approvedKey = GetStartupApprovedKeyPath(keyPath);
            if (approvedKey == null) return true; // sin clave StartupApproved = activo por defecto
            using var key = OpenStartupApprovedKey(approvedKey, writable: false);
            if (key == null) return true;

            var raw = key.GetValue(valueName) as byte[];
            if (raw == null || raw.Length == 0) return true; // sin valor registrado = activo por defecto

            // Bit 0 = 0 indica habilitado (0x02, 0x06); Bit 0 = 1 indica deshabilitado (0x01, 0x03)
            return (raw[0] & 1) == 0;
        }
        catch { return true; }
    }

    private static string? GetStartupApprovedKeyPath(string runKeyPath)
    {
        if (runKeyPath.StartsWith(@"HKEY_CURRENT_USER\", StringComparison.OrdinalIgnoreCase))
            return @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        if (runKeyPath.StartsWith(@"HKEY_LOCAL_MACHINE\", StringComparison.OrdinalIgnoreCase))
            return @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        return null;
    }

    private static RegistryKey? OpenRunKey(string keyPath, bool writable = false)
    {
        try
        {
            var ok = RegexHive(keyPath, out var hive, out var subKey);
            if (!ok) return null;
            return RegistryKey.OpenBaseKey(hive, RegistryView.Default).OpenSubKey(subKey, writable);
        }
        catch { return null; }
    }

    private static RegistryKey? OpenStartupApprovedKey(string keyPath, bool writable = false)
    {
        try
        {
            var ok = RegexHive(keyPath, out var hive, out var subKey);
            if (!ok) return null;
            return RegistryKey.OpenBaseKey(hive, RegistryView.Default).OpenSubKey(subKey, writable);
        }
        catch { return null; }
    }

    private static bool RegexHive(string keyPath, out RegistryHive hive, out string subKey)
    {
        hive = RegistryHive.CurrentUser;
        subKey = keyPath;

        if (keyPath.StartsWith(@"HKEY_CURRENT_USER\", StringComparison.OrdinalIgnoreCase))
        {
            hive = RegistryHive.CurrentUser;
            subKey = keyPath.Substring(@"HKEY_CURRENT_USER\".Length);
            return true;
        }
        if (keyPath.StartsWith(@"HKEY_LOCAL_MACHINE\", StringComparison.OrdinalIgnoreCase))
        {
            hive = RegistryHive.LocalMachine;
            subKey = keyPath.Substring(@"HKEY_LOCAL_MACHINE\".Length);
            return true;
        }
        return false;
    }

    private static string GetPublisherFromFile(string valueData)
    {
        try
        {
            var path = valueData.Trim().Trim('"');
            // Si tiene argumentos, tomar solo la ruta del .exe
            if (path.Contains(".exe\"", StringComparison.OrdinalIgnoreCase))
            {
                var idx = path.IndexOf(".exe\"", StringComparison.OrdinalIgnoreCase);
                path = path.Substring(0, idx + 4);
            }
            else if (path.Contains(".exe ", StringComparison.OrdinalIgnoreCase))
            {
                var idx = path.IndexOf(".exe ", StringComparison.OrdinalIgnoreCase);
                path = path.Substring(0, idx + 4);
            }

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return string.Empty;

            var info = FileVersionInfo.GetVersionInfo(path);
            return info.CompanyName ?? info.ProductName ?? string.Empty;
        }
        catch { return string.Empty; }
    }
}
