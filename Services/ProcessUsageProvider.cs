using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace PC_Optimizer.Services;

/// <summary>Uso de un programa: nombre, RAM y CPU. Nunca rutas ni argumentos.</summary>
public sealed record ProcessUsage(string Name, long RamBytes, double CpuPercent);

/// <summary>Los programas que más RAM y los que más CPU consumen.</summary>
public sealed record ProcessUsageSnapshot(IReadOnlyList<ProcessUsage> TopByRam, IReadOnlyList<ProcessUsage> TopByCpu)
{
    public static readonly ProcessUsageSnapshot Empty = new(Array.Empty<ProcessUsage>(), Array.Empty<ProcessUsage>());
}

/// <summary>
/// Fuente de «Qué consume tu PC». Detrás de una interfaz para que las pruebas usen datos
/// simulados y nunca lean procesos reales.
/// </summary>
public interface IProcessUsageProvider
{
    ProcessUsageSnapshot GetTop(int count);
}

/// <summary>
/// Implementación real: mide la CPU con dos muestras de <see cref="Process.TotalProcessorTime"/>
/// separadas 1 s, dividida entre el número de núcleos, y la RAM con el conjunto de trabajo.
/// Los procesos protegidos o terminados se omiten sin error. Se agrupa por nombre de programa:
/// las ventanas de una misma app se suman, como en el Administrador de tareas.
/// </summary>
public sealed class SystemProcessUsageProvider : IProcessUsageProvider
{
    public const int SampleIntervalMs = 1000;

    /// <summary>El «proceso» inactivo del sistema no es un programa: se excluye.</summary>
    private const string IdleProcessName = "Idle";

    public ProcessUsageSnapshot GetTop(int count)
    {
        if (count <= 0) return ProcessUsageSnapshot.Empty;

        try
        {
            var first = Sample();
            Thread.Sleep(SampleIntervalMs);
            var second = Sample();
            return Build(first, second, count, SampleIntervalMs / 1000.0);
        }
        catch
        {
            return ProcessUsageSnapshot.Empty;
        }
    }

    /// <summary>Construye el resultado a partir de dos muestras (público para pruebas sin esperas).</summary>
    public static ProcessUsageSnapshot Build(
        IReadOnlyDictionary<int, (string Name, double CpuSeconds, long RamBytes)> first,
        IReadOnlyDictionary<int, (string Name, double CpuSeconds, long RamBytes)> second,
        int count, double seconds)
    {
        double interval = seconds > 0 ? seconds : 1;
        int cores = Math.Max(1, Environment.ProcessorCount);

        var byName = new Dictionary<string, (long Ram, double Cpu)>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in second)
        {
            if (entry.Value.RamBytes <= 0 && entry.Value.CpuSeconds <= 0) continue;
            if (!first.TryGetValue(entry.Key, out var previous)) continue;

            double delta = entry.Value.CpuSeconds - previous.CpuSeconds;
            if (delta < 0) delta = 0;
            double percent = Math.Clamp(delta / interval / cores * 100.0, 0, 100);

            var name = string.IsNullOrWhiteSpace(entry.Value.Name) ? "(sin nombre)" : entry.Value.Name;
            if (name.Equals(IdleProcessName, StringComparison.OrdinalIgnoreCase)) continue;

            if (byName.TryGetValue(name, out var accumulated))
                byName[name] = (accumulated.Ram + Math.Max(0, entry.Value.RamBytes), accumulated.Cpu + percent);
            else
                byName[name] = (Math.Max(0, entry.Value.RamBytes), percent);
        }

        var rows = byName
            .Select(pair => new ProcessUsage(pair.Key, pair.Value.Ram, Math.Round(pair.Value.Cpu, 1)))
            .ToList();

        var topByRam = rows.OrderByDescending(r => r.RamBytes).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Take(count).ToList();
        var topByCpu = rows.OrderByDescending(r => r.CpuPercent).ThenByDescending(r => r.RamBytes)
            .Take(count).ToList();

        return new ProcessUsageSnapshot(topByRam, topByCpu);
    }

    private static Dictionary<int, (string Name, double CpuSeconds, long RamBytes)> Sample()
    {
        var data = new Dictionary<int, (string Name, double CpuSeconds, long RamBytes)>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var cpu = process.TotalProcessorTime.TotalSeconds;
                var ram = process.WorkingSet64;
                data[process.Id] = (process.ProcessName, cpu, ram);
            }
            catch
            {
                // Proceso protegido, del sistema o terminado: se ignora sin error.
            }
            finally
            {
                process.Dispose();
            }
        }
        return data;
    }
}
