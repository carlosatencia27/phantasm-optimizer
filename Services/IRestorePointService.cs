using System;

namespace PC_Optimizer.Services;

/// <summary>Qué pasó al pedir un punto de restauración, para poder explicarlo en lenguaje simple.</summary>
public enum RestorePointOutcome
{
    Created,
    NeedsAdmin,
    ProtectionDisabled,
    /// <summary>Windows limita la creación a uno cada 24 horas por defecto y ya hay uno reciente.</summary>
    TooSoon,
    /// <summary>Windows aceptó la solicitud pero no se pudo comprobar que el punto exista.</summary>
    NotVerified,
    NotSupported,
    /// <summary>Windows no contestó dentro del tiempo límite; puede que siga trabajando.</summary>
    TimedOut,
    /// <summary>Una llamada anterior a Windows todavía no ha terminado.</summary>
    StillRunning,
    Failed
}

public sealed record RestorePointResult(RestorePointOutcome Outcome, string Message)
{
    public bool Success => Outcome == RestorePointOutcome.Created;
}

public interface IRestorePointService
{
    bool IsSupported { get; }
    bool CreateRestorePoint(string description, out string resultMessage);

    /// <summary>
    /// Igual que <see cref="CreateRestorePoint"/> pero con el motivo clasificado. Por defecto envuelve
    /// el método anterior (los servicios simulados existentes siguen funcionando).
    /// </summary>
    RestorePointResult Create(string description)
    {
        var ok = CreateRestorePoint(description, out var message);
        return new RestorePointResult(ok ? RestorePointOutcome.Created : RestorePointOutcome.Failed, message);
    }
}
