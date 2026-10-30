using PC_Optimizer.Models;
using PC_Optimizer.Services;

namespace PC_Optimizer.Core;

public class GameModeSetting : ISetting
{
    // Preferencia de usuario observada; no equivale a la directiva AllowAutoGameMode.
    private const string AutoGameModeEnabled = "AutoGameModeEnabled";
    private readonly IGameRegistryService _registry;
    private object? _originalAutoGameModeEnabled;

    public GameModeSetting(IGameRegistryService registry)
    {
        _registry = registry;
        Key = "Game.GameMode";
        Name = "Activar el modo de juego de Windows";
        Description = "Activado: enciende el modo de juego para el usuario actual (AutoGameModeEnabled = 1). Al revertir, restaura el valor original. No es la directiva AllowAutoGameMode.";
        RealisticEffect = "Puede estabilizar los FPS en algunos juegos al priorizar procesos de juego. El efecto real varía por juego y hardware; no garantiza más FPS.";
        RiskLevel = SettingRiskLevel.Safe;
        IsWindowsVersionSupported = IsWindows10OrLater();
    }

    public string Key { get; }
    public string Name { get; }
    public string Description { get; }
    public string RealisticEffect { get; }
    public SettingRiskLevel RiskLevel { get; }
    public bool IsWindowsVersionSupported { get; }
    public bool IsReversible => true;
    public bool RequiresConfirmation => false;
    public bool RequiresRestart => false;
    public bool RequiresAdmin => false;
    public string VerificationStatus => "Observado en Windows, no documentado oficialmente";
    public string? DocumentationUrl => null;

    public object? OriginalValue { get; private set; }
    public bool HasOriginal { get; private set; }

    /// <summary>Activado = Modo de juego de Windows activado (AutoGameModeEnabled = 1).</summary>
    public object? ApplyValue => true;
    /// <summary>Sin copia original, desactivar el ajuste escribe AutoGameModeEnabled = 0.</summary>
    public object? RevertValue => false;

    /// <inheritdoc />
    public void RestoreBackup(object? originalValue, bool originalExisted)
    {
        if (HasOriginal) return;
        _originalAutoGameModeEnabled = originalValue;
        OriginalValue = originalValue;
        HasOriginal = true;
    }

    public object? ReadCurrentState()
    {
        // El estado principal lo determina AutoGameModeEnabled (1=activo, 0=inactivo, null=predeterminado=activo)
        var val = _registry.GetValue(AutoGameModeEnabled);
        if (val is int i)
            return (object)(i == 1);
        // Si no existe la clave, Windows asume Game Mode activo por defecto
        return (object)true;
    }

    public void Apply(object? newValue, bool dryRun)
    {
        if (!dryRun)
        {
            // Guardar estado original de ambas claves
            _originalAutoGameModeEnabled = _registry.GetValue(AutoGameModeEnabled);
            OriginalValue = _originalAutoGameModeEnabled;
            int enabled = newValue is bool b && b ? 1 : 0;
            if (!_registry.SetValue(AutoGameModeEnabled, enabled)) throw new InvalidOperationException("No se pudo cambiar el Modo de juego.");
            HasOriginal = true;
        }
    }

    public void Revert(bool dryRun)
    {
        if (!dryRun && HasOriginal)
        {
            // Restaurar exactamente el valor original (o eliminar la clave si no existía antes)
            if (_originalAutoGameModeEnabled != null)
            {
                if (!_registry.SetValue(AutoGameModeEnabled, _originalAutoGameModeEnabled))
                    throw new InvalidOperationException("No se pudo restaurar el Modo de juego.");
            }
            else
            {
                if (!_registry.DeleteValue(AutoGameModeEnabled))
                    throw new InvalidOperationException("No se pudo restaurar el Modo de juego.");
            }


            OriginalValue = null;
            HasOriginal = false;
            _originalAutoGameModeEnabled = null;
        }
    }

    private static bool IsWindows10OrLater()
    {
        try
        {
            var version = Environment.OSVersion.Version;
            return version.Major > 10 || (version.Major == 10 && version.Minor >= 0);
        }
        catch { return false; }
    }
}
