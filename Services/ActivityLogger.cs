using System;

namespace PC_Optimizer.Services;

/// <summary>Registro de operaciones inyectable para no escribir fuera de las carpetas temporales en pruebas.</summary>
public interface IActivityLogger
{
    void Write(string message);
}

public sealed class NullActivityLogger : IActivityLogger
{
    public void Write(string message) { }
}

public sealed class AppActivityLogger : IActivityLogger
{
    public void Write(string message) => App.WriteLog(message);
}
