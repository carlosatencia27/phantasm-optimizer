using System;
using System.Management;
using Microsoft.Win32;

namespace PC_Optimizer.Services;

/// <summary>
/// Textos y clasificación de los resultados de «crear punto de restauración». Son funciones puras
/// para poder probarlas sin tocar Windows. Nunca muestran mensajes internos de error.
/// </summary>
public static class RestorePointMessages
{
    /// <summary>ERROR_SERVICE_DISABLED: el servicio de protección del sistema está desactivado.</summary>
    public const int ServiceDisabledCode = 1058;

    /// <summary>Por defecto Windows crea como máximo un punto cada 1440 minutos (24 horas).</summary>
    public const int DefaultFrequencyMinutes = 1440;

    public const string NeedsAdmin =
        "No se pudo crear el punto de restauración: faltan permisos de administrador. " +
        "Pulsa «Reiniciar como administrador» (en la barra inferior) y vuelve a intentarlo. La app no lo hace sola.";

    public const string ProtectionDisabled =
        "No se pudo crear el punto de restauración: la protección del sistema está desactivada en Windows. " +
        "La app no la activa por ti. Para activarla: Inicio > escribe «Crear un punto de restauración» > " +
        "selecciona la unidad C: > Configurar > «Activar protección del sistema» > Aceptar.";

    public const string NotSupported = "Este Windows no permite crear puntos de restauración desde la app.";

    public const string StillRunning =
        "Windows todavía está procesando el punto de restauración anterior. Espera unos minutos o compruébalo en " +
        "Inicio > «Recuperar» > «Abrir Restaurar sistema» (sin restaurar nada) antes de volver a intentarlo.";

    /// <summary>La app dejó de esperar: no se sabe si Windows lo terminará, así que se explica cómo comprobarlo.</summary>
    public static RestorePointResult TimedOut(TimeSpan waited)
    {
        var seconds = (int)Math.Round(waited.TotalSeconds);
        var text = seconds >= 60 && seconds % 60 == 0
            ? (seconds == 60 ? "1 minuto" : $"{seconds / 60} minutos")
            : $"{seconds} segundos";
        return new(RestorePointOutcome.TimedOut,
            $"Windows tardó más de {text} en crear el punto de restauración, así que la app dejó de esperar. " +
            "Windows puede terminarlo por su cuenta: antes de repetirlo, compruébalo en Inicio > «Recuperar» > " +
            "«Abrir Restaurar sistema» (sin restaurar nada).");
    }

    public static RestorePointResult Created() =>
        new(RestorePointOutcome.Created, "Punto de restauración creado y comprobado.");

    public static RestorePointResult TooSoon(DateTime lastPoint, int frequencyMinutes)
    {
        var hours = frequencyMinutes == 60 ? "1 hora" : frequencyMinutes % 60 == 0 ? $"{frequencyMinutes / 60} horas" : $"{frequencyMinutes} minutos";
        return new(RestorePointOutcome.TooSoon,
            $"No se creó un punto nuevo: por defecto Windows crea como máximo uno cada {hours}, y ya hay uno de {lastPoint:dd/MM/yyyy HH:mm}. " +
            "Ese punto restaura el estado de esa hora (los cambios posteriores no quedan incluidos). No es un error: puedes continuar o crear otro más tarde.");
    }

    public static RestorePointResult NotVerified() =>
        new(RestorePointOutcome.NotVerified,
            "Windows aceptó la solicitud, pero la app no pudo comprobar que el punto exista. " +
            "Para verlo: Inicio > «Recuperar» > «Abrir Restaurar sistema» (sin restaurar nada). " +
            "Si no aparece, revisa que la protección del sistema esté activada.");

    public static RestorePointResult Failed(int code) =>
        new(RestorePointOutcome.Failed, $"No se pudo crear el punto de restauración (código de Windows {code}).");

    /// <summary>Clasifica la respuesta de Windows con los datos que se pudieron leer.</summary>
    public static RestorePointResult Classify(int returnValue, bool pointFound, DateTime? latestPoint,
        DateTime now, int frequencyMinutes)
    {
        if (returnValue == ServiceDisabledCode)
            return new(RestorePointOutcome.ProtectionDisabled, ProtectionDisabled);

        if (returnValue != 0)
            return Failed(returnValue);

        if (pointFound)
            return Created();

        // Windows devuelve 0 aunque no cree el punto cuando aplica el límite de frecuencia.
        if (frequencyMinutes > 0 && latestPoint.HasValue && now - latestPoint.Value < TimeSpan.FromMinutes(frequencyMinutes)
            && now >= latestPoint.Value)
            return TooSoon(latestPoint.Value, frequencyMinutes);

        return NotVerified();
    }

    /// <summary>Traduce una excepción a un mensaje claro, sin copiar el mensaje interno.</summary>
    public static RestorePointResult FromException(Exception ex)
    {
        if (ex is UnauthorizedAccessException ||
            (ex is ManagementException me && me.ErrorCode == ManagementStatus.AccessDenied))
            return new(RestorePointOutcome.NeedsAdmin, NeedsAdmin);

        return new(RestorePointOutcome.Failed,
            $"No se pudo crear el punto de restauración: {UserErrorText.Of(ex)}.");
    }
}

public class RestorePointService : IRestorePointService
{
    public bool IsSupported => Environment.OSVersion.Platform == PlatformID.Win32NT;

    public bool CreateRestorePoint(string description, out string resultMessage)
    {
        var result = Create(description);
        resultMessage = result.Message;
        return result.Success;
    }

    public RestorePointResult Create(string description)
    {
        try
        {
            if (IsProtectionDisabledByPolicy())
                return new RestorePointResult(RestorePointOutcome.ProtectionDisabled, RestorePointMessages.ProtectionDisabled);

            using var wmiClass = new ManagementClass("root\\default", "SystemRestore", null);
            var inParams = wmiClass.GetMethodParameters("CreateRestorePoint");
            inParams["Description"] = description;
            inParams["EventType"] = 102;
            inParams["RestorePointType"] = 0;

            var outParams = wmiClass.InvokeMethod("CreateRestorePoint", inParams, null);
            int returnValue = Convert.ToInt32(outParams["ReturnValue"]);

            if (returnValue != 0)
                return RestorePointMessages.Classify(returnValue, false, null, DateTime.Now, 0);

            var (found, latest) = ReadPoints(description);
            return RestorePointMessages.Classify(0, found, latest, DateTime.Now, ReadFrequencyMinutes());
        }
        catch (Exception ex)
        {
            return RestorePointMessages.FromException(ex);
        }
    }

    /// <summary>Directiva «Desactivar Restaurar sistema» (DisableSR = 1), solo lectura.</summary>
    private static bool IsProtectionDisabledByPolicy()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore");
            return key?.GetValue("DisableSR") is int value && value == 1;
        }
        catch { return false; }
    }

    /// <summary>Minutos mínimos entre puntos (SystemRestorePointCreationFrequency); 1440 si no está definido.</summary>
    private static int ReadFrequencyMinutes()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore");
            return key?.GetValue("SystemRestorePointCreationFrequency") is int value && value >= 0
                ? value : RestorePointMessages.DefaultFrequencyMinutes;
        }
        catch { return RestorePointMessages.DefaultFrequencyMinutes; }
    }

    /// <summary>Busca el punto por su descripción y devuelve la fecha del más reciente (solo lectura).</summary>
    private static (bool Found, DateTime? Latest) ReadPoints(string description)
    {
        bool found = false;
        DateTime? latest = null;
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\default", "SELECT Description, CreationTime FROM SystemRestore");
            foreach (ManagementObject point in searcher.Get())
            {
                if (string.Equals(Convert.ToString(point["Description"]), description, StringComparison.Ordinal))
                    found = true;

                var raw = Convert.ToString(point["CreationTime"]);
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    var when = ManagementDateTimeConverter.ToDateTime(raw);
                    if (!latest.HasValue || when > latest.Value) latest = when;
                }
            }
        }
        catch
        {
            // Si no se puede leer la lista, se informa «no comprobado» en lugar de inventar un resultado.
        }
        return (found, latest);
    }
}
