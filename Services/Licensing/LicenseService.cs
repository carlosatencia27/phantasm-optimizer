using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PC_Optimizer.Services.Licensing;

public enum LicenseStatus
{
    NoLicense,
    Valid,
    Expired,
    WrongMachine,
    Corrupt,
    VersionTooOld,
    ClockRolledBack
}

/// <summary>Resultado de comprobar una licencia: estado, mensaje claro y datos para mostrar.</summary>
public sealed record LicenseCheck(LicenseStatus Status, string Message, string? Name = null, DateTime? ExpiresUtc = null,
    bool ClockWarning = false)
{
    public bool IsValid => Status == LicenseStatus.Valid;
}

/// <summary>
/// Puerta que consultan los servicios que modifican el sistema. Solo bloquea cambios nuevos:
/// «Deshacer cambios» y la restauración no la consultan nunca.
/// </summary>
public interface ILicenseGate
{
    bool CanModifySystem { get; }
    string BlockedMessage { get; }
}

/// <summary>Sin control de licencia (pruebas y componentes que no la necesitan).</summary>
public sealed class AllowAllGate : ILicenseGate
{
    public static readonly AllowAllGate Instance = new();
    public bool CanModifySystem => true;
    public string BlockedMessage => string.Empty;
}

/// <summary>Guarda la última fecha vista (para detectar relojes atrasados) y la licencia. Sustituible en pruebas.</summary>
public interface ILicenseStateStore
{
    DateTime? ReadLastSeenUtc();
    void WriteLastSeenUtc(DateTime utc);
    string? ReadLicenseText();
    void WriteLicenseText(string text);
}

[System.Reflection.Obfuscation(Exclude = false)]
public sealed class LicenseFileStore : ILicenseStateStore
{
    private readonly string _dir;
    public LicenseFileStore(string? dir = null) =>
        _dir = dir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PC Optimizer");
    private string LicensePath => Path.Combine(_dir, "license.lic");
    private string StatePath => Path.Combine(_dir, "license-state");

    public DateTime? ReadLastSeenUtc()
    {
        try
        {
            if (!File.Exists(StatePath)) return null;
            // DPAPI del usuario: editarlo a mano con el Bloc de notas lo invalida.
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(StatePath), null, DataProtectionScope.CurrentUser);
            return long.TryParse(Encoding.UTF8.GetString(plain), out var ticks) ? new DateTime(ticks, DateTimeKind.Utc) : null;
        }
        catch { return null; }
    }

    public void WriteLastSeenUtc(DateTime utc)
    {
        try
        {
            Directory.CreateDirectory(_dir);
            var data = ProtectedData.Protect(Encoding.UTF8.GetBytes(utc.Ticks.ToString(CultureInfo.InvariantCulture)), null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(StatePath, data);
        }
        catch { /* sin permiso de escritura: se sigue sin guardar la fecha */ }
    }

    public string? ReadLicenseText()
    {
        try { return File.Exists(LicensePath) ? File.ReadAllText(LicensePath) : null; }
        catch { return null; }
    }

    public void WriteLicenseText(string text)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(LicensePath, text.Trim());
    }
}

/// <summary>Comprueba la licencia sin red: firma, equipo, versión, vencimiento y reloj.</summary>
[System.Reflection.Obfuscation(Exclude = false)]
public sealed class LicenseService : ILicenseGate
{
    public static readonly TimeSpan ClockTolerance = TimeSpan.FromHours(24);

    private readonly byte[] _publicKey;
    private readonly ILicenseStateStore _store;
    private readonly IMachineIdSource _idSource;
    private readonly Func<DateTime> _utcNow;
    private readonly Version _appVersion;
    private readonly object _lock = new();
    private string? _machineId;
    private LicenseCheck? _current;

    /// <param name="publicKey">Clave pública SPKI; por defecto la incluida en el ejecutable (la privada nunca está en el repositorio).</param>
    public LicenseService(ILicenseStateStore store, IMachineIdSource idSource, Version appVersion,
        byte[]? publicKey = null, Func<DateTime>? utcNow = null)
    {
        _store = store;
        _idSource = idSource;
        _appVersion = appVersion;
        _publicKey = publicKey ?? Convert.FromBase64String(LicenseKeys.PublicKeyBase64);
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public string MachineId
    {
        get { lock (_lock) return _machineId ??= Licensing.MachineId.Compute(_idSource); }
    }

    public LicenseCheck Current
    {
        get { lock (_lock) return _current ??= Evaluate(); }
    }

    /// <summary>Se reevalúa cada vez (lectura barata y poco frecuente), así una licencia que vence con la app abierta bloquea sin reiniciar.</summary>
    public bool CanModifySystem => Refresh().IsValid;
    public string BlockedMessage => Current.Message;

    /// <summary>Vuelve a leer la licencia guardada (p. ej. al abrir la pantalla).</summary>
    public LicenseCheck Refresh()
    {
        lock (_lock) return _current = Evaluate();
    }

    /// <summary>Comprueba el texto; solo si es válido lo guarda y lo activa.</summary>
    public LicenseCheck Activate(string? text)
    {
        lock (_lock)
        {
            var check = Check(text, _utcNow(), _store.ReadLastSeenUtc());
            if (check.IsValid && text is not null)
            {
                try { _store.WriteLicenseText(text); }
                catch (Exception ex) { return new LicenseCheck(LicenseStatus.Corrupt, "La licencia es válida, pero no se pudo guardar: " + UserErrorText.Of(ex)); }
                TouchLastSeen();
                _current = check;
            }
            return check;
        }
    }

    private LicenseCheck Evaluate()
    {
        var check = Check(_store.ReadLicenseText(), _utcNow(), _store.ReadLastSeenUtc());
        if (check.Status != LicenseStatus.ClockRolledBack && !check.ClockWarning) TouchLastSeen();
        return check;
    }

    private void TouchLastSeen()
    {
        var now = _utcNow();
        var last = _store.ReadLastSeenUtc();
        // Un salto hacia adelante se registra como mucho 7 días por vez: si el reloj se adelantó por error,
        // la fecha guardada no se aleja tanto como para bloquear la licencia durante meses.
        if (last is null) _store.WriteLastSeenUtc(now);
        else if (now > last) _store.WriteLastSeenUtc(now > last.Value.AddDays(7) ? last.Value.AddDays(7) : now);
    }

    private const string CorruptText = "No se pudo comprobar esta licencia: el texto está incompleto o fue modificado. Cópialo de nuevo tal como lo recibiste.";

    private LicenseCheck Check(string? text, DateTime nowUtc, DateTime? lastSeenUtc)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new LicenseCheck(LicenseStatus.NoLicense,
                "Esta copia aún no tiene licencia. Puedes ver el estado de tu PC, usar el modo simulación y deshacer cambios anteriores, pero no aplicar ajustes nuevos.");

        if (!LicenseFormat.TryDecode(text, _publicKey, out var p))
            return new LicenseCheck(LicenseStatus.Corrupt, CorruptText);

        if (!string.Equals(Licensing.MachineId.Normalize(p.Machine), MachineId, StringComparison.Ordinal))
            return new LicenseCheck(LicenseStatus.WrongMachine,
                "Esta licencia es para otro equipo. Pide una nueva usando el ID de este PC.", p.Name);

        if (!Version.TryParse(p.MinVersion, out var min) || !TryDate(p.Issued, out var issued) ||
            (p.Expires is not null && !TryDate(p.Expires, out _)))
            return new LicenseCheck(LicenseStatus.Corrupt, CorruptText);

        if (_appVersion < min)
            return new LicenseCheck(LicenseStatus.VersionTooOld,
                $"Esta licencia exige la versión {min} de la app o una más nueva. Tú tienes la {_appVersion}. Actualiza la app.", p.Name);

        DateTime? expires = null;
        if (p.Expires is not null) { TryDate(p.Expires, out var e); expires = e.AddDays(1).AddTicks(-1); } // vale todo ese día

        bool clockBack = (lastSeenUtc is { } seen && nowUtc < seen - ClockTolerance) || nowUtc < issued - ClockTolerance;
        const string clockText = "La fecha de tu PC parece atrasada respecto a la última vez que usaste la app. Corrígela para validar la licencia; mientras tanto no se aplican ajustes nuevos.";
        const string clockWarnText = "La fecha de tu PC parece atrasada respecto a la última vez que usaste la app. Corrígela para evitar problemas con la licencia.";
        if (clockBack && expires is not null)
            return new LicenseCheck(LicenseStatus.ClockRolledBack, clockText, p.Name, expires);

        if (expires is { } exp && nowUtc > exp)
            return new LicenseCheck(LicenseStatus.Expired,
                $"Tu licencia venció el {exp:dd/MM/yyyy}. Sigues pudiendo usar la simulación y deshacer cambios. Para aplicar ajustes nuevos pide una licencia renovada.", p.Name, exp);

        var msg = expires is { } e2 ? $"Licencia de {p.Name}, válida hasta el {e2:dd/MM/yyyy}." : $"Licencia de {p.Name}, sin vencimiento.";
        if (clockBack) msg += " " + clockWarnText;
        return new LicenseCheck(LicenseStatus.Valid, msg, p.Name, expires, ClockWarning: clockBack);
    }

    private static bool TryDate(string s, out DateTime utc) =>
        DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out utc);
}
