using System;
using System.Management;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace PC_Optimizer.Services.Licensing;

/// <summary>Origen de los dos datos que forman el ID de equipo (solo lectura; sustituible en pruebas).</summary>
public interface IMachineIdSource
{
    string? ReadMachineGuid();
    string? ReadSmbiosUuid();
}

[System.Reflection.Obfuscation(Exclude = false)]
public sealed class WindowsMachineIdSource : IMachineIdSource
{
    public string? ReadMachineGuid()
    {
        try
        {
            using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            return key?.GetValue("MachineGuid") as string;
        }
        catch { return null; }
    }

    public string? ReadSmbiosUuid()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT UUID FROM Win32_ComputerSystemProduct");
            foreach (var o in searcher.Get())
                using (o) return o["UUID"] as string;
        }
        catch { }
        return null;
    }
}

[System.Reflection.Obfuscation(Exclude = false)]
public static class MachineId
{
    /// <summary>SHA-256("PhantasmOptimizer|v1|" + MachineGuid + "|" + UUID SMBIOS); 20 hex en grupos de 4.</summary>
    public static string Compute(IMachineIdSource source)
    {
        var guid = (source.ReadMachineGuid() ?? string.Empty).Trim().ToLowerInvariant();
        var uuid = (source.ReadSmbiosUuid() ?? string.Empty).Trim().ToLowerInvariant();
        if (IsJunkUuid(uuid)) uuid = "-";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"PhantasmOptimizer|v1|{guid}|{uuid}"));
        var hex = Convert.ToHexString(hash)[..20];
        return string.Join("-", hex[..4], hex[4..8], hex[8..12], hex[12..16], hex[16..20]);
    }

    /// <summary>Valores que algunos fabricantes dejan sin rellenar y no identifican nada.</summary>
    public static bool IsJunkUuid(string uuid)
    {
        if (string.IsNullOrWhiteSpace(uuid)) return true;
        var compact = uuid.Replace("-", "").Replace("{", "").Replace("}", "");
        if (compact.Trim('0').Length == 0 || compact.Trim('f').Length == 0) return true;
        return uuid.Contains("default") || uuid.Contains("filled") || uuid.Contains("none") || uuid.Contains("unknown");
    }

    public static string Normalize(string? id) => (id ?? string.Empty).Trim().ToUpperInvariant();
}
