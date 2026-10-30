using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Security;

namespace PC_Optimizer.Services;

/// <summary>Un arranque medido por Windows: fecha del evento y duración en segundos.</summary>
public sealed record BootHistoryEntry(DateTime When, double Seconds);

/// <summary>
/// Últimos arranques leídos del registro de eventos. Entries vacío + Problem con el motivo
/// cuando no se pudo leer: nunca se inventa un valor.
/// </summary>
public sealed record BootHistoryResult(IReadOnlyList<BootHistoryEntry> Entries, string? Problem)
{
    public static BootHistoryResult Empty(string problem) => new(Array.Empty<BootHistoryEntry>(), problem);
}

/// <summary>
/// Solo lectura. Detrás de una interfaz para que las pruebas usen datos simulados y nunca
/// consulten el registro de eventos de Windows.
/// </summary>
public interface IBootHistoryService
{
    BootHistoryResult GetLastBoots(int count);
}

/// <summary>
/// Lee los últimos eventos 100 (BootTime) de
/// Microsoft-Windows-Diagnostics-Performance/Operational, en orden inverso y sin escribir nada.
/// </summary>
public sealed class SystemBootHistoryService : IBootHistoryService
{
    public const string LogName = SystemMeasurementProvider.BootLogName;
    private const int BootTimeEventId = 100;

    private readonly Func<int, BootHistoryResult> _reader;

    public SystemBootHistoryService(Func<int, BootHistoryResult>? reader = null)
        => _reader = reader ?? Read;

    public BootHistoryResult GetLastBoots(int count)
    {
        try { return _reader(Math.Clamp(count, 1, 20)); }
        catch (UnauthorizedAccessException) { return BootHistoryResult.Empty(NoPermission); }
        catch (SecurityException) { return BootHistoryResult.Empty(NoPermission); }
        catch (Exception ex) { return BootHistoryResult.Empty($"No disponible: {PC_Optimizer.Services.UserErrorText.Of(ex)}"); }
    }

    /// <summary>El registro se pudo leer pero Windows no guardó ningún evento 100 (tiempo de arranque).</summary>
    public const string NoEventsRecorded =
        "No disponible: Windows no ha guardado ningún tiempo de arranque (evento 100) en este equipo, así que no hay dato que mostrar. " +
        "Esto depende de Windows, no de la app, y la app no lo inventa.";

    public const string NoPermission = "No disponible: puede requerir administrador para leer el registro de eventos de Windows.";

    private static BootHistoryResult Read(int count)
    {
        var entries = new List<BootHistoryEntry>();
        try
        {
            var query = new EventLogQuery(LogName, PathType.LogName, $"*[System[(EventID={BootTimeEventId})]]")
            {
                ReverseDirection = true
            };

            using var reader = new EventLogReader(query);
            while (entries.Count < count)
            {
                using var record = reader.ReadEvent();
                if (record == null) break;
                var seconds = ReadSeconds(record);
                if (!seconds.HasValue) continue;
                entries.Add(new BootHistoryEntry(record.TimeCreated?.ToLocalTime() ?? DateTime.MinValue, seconds.Value));
            }
        }
        catch (UnauthorizedAccessException) { return BootHistoryResult.Empty(NoPermission); }
        catch (SecurityException) { return BootHistoryResult.Empty(NoPermission); }
        catch (EventLogNotFoundException)
        {
            return BootHistoryResult.Empty("No disponible: este equipo no tiene el registro de rendimiento de arranque de Windows (Diagnostics-Performance), así que Windows no guarda tiempos de arranque.");
        }
        catch (EventLogException ex) when (ex.HResult == unchecked((int)0x80070005))
        {
            return BootHistoryResult.Empty(NoPermission);
        }
        catch (Exception ex) { return BootHistoryResult.Empty($"No disponible: {PC_Optimizer.Services.UserErrorText.Of(ex)}"); }

        return entries.Count == 0
            ? BootHistoryResult.Empty(NoEventsRecorded)
            : new BootHistoryResult(entries, null);
    }

    private static double? ReadSeconds(EventRecord record)
    {
        if (record.Properties.Count == 0) return null;
        var value = record.Properties[0].Value;
        return value switch
        {
            uint u => u / 1000.0,
            int i when i > 0 => i / 1000.0,
            long l when l > 0 => l / 1000.0,
            _ => null
        };
    }
}
