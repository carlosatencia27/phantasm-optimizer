using PC_Optimizer.Models;
using PC_Optimizer.Services;

namespace PC_Optimizer.Core;

public class StartupProgramSetting : ISetting
{
    private readonly IStartupProgramService _startupProgramService;
    private readonly StartupProgram _program;

    public StartupProgramSetting(IStartupProgramService startupProgramService, StartupProgram program)
    {
        _startupProgramService = startupProgramService;
        _program = program;
        Key = $"Startup.{program.KeyPath.GetHashCode():X8}.{program.ValueName.GetHashCode():X8}";
        Name = $"Quitar «{program.Name}» del arranque automático";
        Description = (string.IsNullOrWhiteSpace(program.Publisher)
            ? "Activado: el programa deja de ejecutarse al iniciar sesión"
            : $"Activado: el programa deja de ejecutarse al iniciar sesión. Editor: {program.Publisher}") +
            $" ({program.KeyPath}). Al revertir, vuelve al arranque automático.";
        RealisticEffect = "Desactivar un programa de inicio evita que se ejecute automáticamente al arrancar Windows. Se puede reactivar cuando lo desees.";

        // Los de HKLM requieren privilegios de administrador y tienen riesgo Moderado
        if (program.IsLocalMachine)
        {
            RiskLevel = SettingRiskLevel.Moderate;
            RequiresAdmin = true;
        }
        else
        {
            RiskLevel = SettingRiskLevel.Safe;
            RequiresAdmin = false;
        }

        IsWindowsVersionSupported = true;
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
    public bool RequiresAdmin { get; }
    public string VerificationStatus => "Observado en Windows, no documentado oficialmente";
    public string? DocumentationUrl => null;

    public string ProgramKeyPath => _program.KeyPath;
    public string ProgramValueName => _program.ValueName;
    public string ProgramValueData => _program.ValueData;
    public string ProgramPublisher => _program.Publisher;

    public object? OriginalValue { get; private set; }
    public bool HasOriginal { get; private set; }

    /// <summary>Activado = el programa deja de iniciarse con Windows (es la acción que describe el efecto realista).</summary>
    public object? ApplyValue => true;
    /// <summary>Sin copia original, desactivar el ajuste devuelve el programa al arranque automático.</summary>
    public object? RevertValue => false;

    /// <inheritdoc />
    public void RestoreBackup(object? originalValue, bool originalExisted)
    {
        if (HasOriginal) return;
        OriginalValue = originalValue;
        _approvalValueExisted = originalExisted;
        HasOriginal = true;
    }
    private bool _approvalValueExisted;

    public object? ReadCurrentState()
    {
        // El interruptor representa el ajuste aplicado: el programa no se inicia con Windows.
        return !_program.Enabled;
    }

    /// <summary>Estado con lenguaje simple, sin claves técnicas.</summary>
    public string GetFriendlyState()
        => _program.Enabled ? "Se inicia con Windows" : "No se inicia con Windows";

    public void Apply(object? newValue, bool dryRun)
    {
        if (!dryRun)
        {
            var approvalValue = _startupProgramService.GetApprovalValue(_program.KeyPath, _program.ValueName);
            _approvalValueExisted = approvalValue != null;
            OriginalValue = approvalValue?.Clone();
            // Activado = quitar el programa del arranque automático. Al desactivar el ajuste se vuelve a habilitar.
            bool disable = newValue is bool b ? b : (newValue?.ToString() != "False");
            bool succeeded = disable
                ? _startupProgramService.Disable(_program.KeyPath, _program.ValueName)
                : _startupProgramService.Enable(_program.KeyPath, _program.ValueName, _program.ValueData);
            if (!succeeded) throw new InvalidOperationException("No se pudo cambiar el programa de inicio.");
            _program.Enabled = !disable;
            HasOriginal = true;
        }
    }

    public void Revert(bool dryRun)
    {
        if (!dryRun && HasOriginal)
        {
            bool succeeded = _approvalValueExisted && OriginalValue is byte[] originalApproval
                ? _startupProgramService.SetApprovalValue(_program.KeyPath, _program.ValueName, originalApproval)
                : _startupProgramService.DeleteApprovalValue(_program.KeyPath, _program.ValueName);
            if (!succeeded) throw new InvalidOperationException("No se pudo restaurar el programa de inicio.");
            _program.Enabled = !_approvalValueExisted || OriginalValue is not byte[] raw || raw.Length == 0 || (raw[0] & 1) == 0;
            OriginalValue = null;
            HasOriginal = false;
            _approvalValueExisted = false;
        }
    }
}
