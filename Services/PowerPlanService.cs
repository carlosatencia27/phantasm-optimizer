using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace PC_Optimizer.Services;

public interface IPowerPlanService
{
    string? GetActivePlanGuid();
    string? GetActivePlanName();
    IReadOnlyList<PowerPlan> GetAvailablePlans();
    bool SetActivePlan(string planGuid);
}

public class PowerPlan
{
    public string Guid { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }

    public string DisplayName => IsActive ? $"{Name} (Activo)" : Name;
    public override string ToString() => DisplayName;
}

public class PowerPlanService : IPowerPlanService
{
    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr pActivePolicyGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerEnumerate(IntPtr rootPowerKey, IntPtr schemeGuid, IntPtr subGroupOfPowerSettingsGuid, uint accessFlags, uint index, byte[] buffer, ref uint bufferSize);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadFriendlyName(IntPtr rootPowerKey, ref Guid schemeGuid, IntPtr subGroupOfPowerSettingsGuid, IntPtr powerSettingGuid, byte[] buffer, ref uint bufferSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr hMem);

    private const uint AccessScheme = 16;

    private static readonly Dictionary<string, string> KnownPlanNames = new(StringComparer.OrdinalIgnoreCase)
    {
        { "381b4222-f694-41f0-9685-ff5bb260df2e", "Equilibrado" },
        { "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", "Alto rendimiento" },
        { "a1841308-3541-4fab-bc81-f71556f20b4a", "Economizador" },
        { "e9a42b02-d5df-448d-aa00-03f14749eb61", "Máximo rendimiento" }
    };

    public string? GetActivePlanGuid()
    {
        try
        {
            if (PowerGetActiveScheme(IntPtr.Zero, out IntPtr pGuid) == 0 && pGuid != IntPtr.Zero)
            {
                var guid = Marshal.PtrToStructure<Guid>(pGuid);
                LocalFree(pGuid);
                return guid.ToString();
            }
        }
        catch { }

        // Fallback vía comando sin analizar nombres
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powercfg",
                Arguments = "/getactivescheme",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            var output = process?.StandardOutput.ReadToEnd();
            process?.WaitForExit();
            if (!string.IsNullOrEmpty(output))
            {
                var match = System.Text.RegularExpressions.Regex.Match(output, @"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})");
                if (match.Success) return match.Groups[1].Value;
            }
        }
        catch { }

        return null;
    }

    public string? GetActivePlanName()
    {
        var guid = GetActivePlanGuid();
        if (string.IsNullOrEmpty(guid)) return null;

        if (KnownPlanNames.TryGetValue(guid, out var known))
            return known;

        var plans = GetAvailablePlans();
        var found = plans.FirstOrDefault(p => p.Guid.Equals(guid, StringComparison.OrdinalIgnoreCase));
        if (found != null && !string.IsNullOrEmpty(found.Name))
            return found.Name;

        return guid;
    }

    public IReadOnlyList<PowerPlan> GetAvailablePlans()
    {
        var plans = new List<PowerPlan>();
        var activeGuid = GetActivePlanGuid();

        // 1. Intentar enumeración nativa Win32
        try
        {
            uint index = 0;
            var buffer = new byte[16];
            uint bufferSize = (uint)buffer.Length;

            while (PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, AccessScheme, index, buffer, ref bufferSize) == 0)
            {
                var guid = new Guid(buffer);
                var guidStr = guid.ToString();

                string name = ReadPlanFriendlyName(guid) ?? (KnownPlanNames.TryGetValue(guidStr, out var known) ? known : $"Plan ({guidStr})");

                plans.Add(new PowerPlan
                {
                    Guid = guidStr,
                    Name = name,
                    IsActive = !string.IsNullOrEmpty(activeGuid) && guidStr.Equals(activeGuid, StringComparison.OrdinalIgnoreCase)
                });

                index++;
                bufferSize = (uint)buffer.Length;
            }
        }
        catch { }

        // 2. Si la API no devolvió planes, parsear powercfg /list buscando exclusivamente los GUIDs
        if (plans.Count == 0)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powercfg",
                    Arguments = "/list",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(psi);
                var output = process?.StandardOutput.ReadToEnd();
                process?.WaitForExit();

                if (!string.IsNullOrEmpty(output))
                {
                    var matches = System.Text.RegularExpressions.Regex.Matches(output, @"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})");
                    foreach (System.Text.RegularExpressions.Match m in matches)
                    {
                        var g = m.Value;
                        if (plans.Exists(p => p.Guid.Equals(g, StringComparison.OrdinalIgnoreCase))) continue;

                        string name = KnownPlanNames.TryGetValue(g, out var kn) ? kn : $"Plan {g[..8]}";
                        // Extraer paréntesis si existe en esa línea
                        var line = output.Split('\n').FirstOrDefault(l => l.Contains(g));
                        if (line != null)
                        {
                            var parenMatch = System.Text.RegularExpressions.Regex.Match(line, @"\(([^)]+)\)");
                            if (parenMatch.Success) name = parenMatch.Groups[1].Value.Trim();
                        }

                        plans.Add(new PowerPlan
                        {
                            Guid = g,
                            Name = name,
                            IsActive = !string.IsNullOrEmpty(activeGuid) && g.Equals(activeGuid, StringComparison.OrdinalIgnoreCase)
                        });
                    }
                }
            }
            catch { }
        }

        // Si aún no hay planes, incluir al menos los estándares conocidos
        if (plans.Count == 0)
        {
            foreach (var kv in KnownPlanNames)
            {
                plans.Add(new PowerPlan
                {
                    Guid = kv.Key,
                    Name = kv.Value,
                    IsActive = !string.IsNullOrEmpty(activeGuid) && kv.Key.Equals(activeGuid, StringComparison.OrdinalIgnoreCase)
                });
            }
        }

        return plans;
    }

    private static string? ReadPlanFriendlyName(Guid schemeGuid)
    {
        try
        {
            uint bufferSize = 0;
            // Primera llamada para obtener el tamaño necesario
            _ = PowerReadFriendlyName(IntPtr.Zero, ref schemeGuid, IntPtr.Zero, IntPtr.Zero, Array.Empty<byte>(), ref bufferSize);
            if (bufferSize > 0)
            {
                var nameBuffer = new byte[bufferSize];
                if (PowerReadFriendlyName(IntPtr.Zero, ref schemeGuid, IntPtr.Zero, IntPtr.Zero, nameBuffer, ref bufferSize) == 0)
                {
                    var name = Encoding.Unicode.GetString(nameBuffer).TrimEnd('\0').Trim();
                    if (!string.IsNullOrEmpty(name)) return name;
                }
            }
        }
        catch { }

        return null;
    }

    public bool SetActivePlan(string planGuid)
    {
        if (Guid.TryParse(planGuid, out var guid))
        {
            try
            {
                if (PowerSetActiveScheme(IntPtr.Zero, ref guid) == 0)
                    return true;
            }
            catch { }
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powercfg",
                Arguments = $"/setactive {planGuid}",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            process?.WaitForExit();
            return process?.ExitCode == 0;
        }
        catch { return false; }
    }
}
