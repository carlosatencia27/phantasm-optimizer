using System;
using System.Collections.Generic;
using System.Linq;
using PC_Optimizer.Models;

namespace PC_Optimizer.Services;

/// <summary>Contador factual de la sesión, calculado del registro real de cambios del motor.</summary>
public readonly record struct SessionCounts(int Applied, int Reverted)
{
    public string Text => $"Esta sesión: {Applied} aplicados · {Reverted} revertidos";
}

public static class SessionStats
{
    /// <summary>Cuenta lo aplicado y revertido DE VERDAD desde <paramref name="since"/> (las simulaciones no cuentan).</summary>
    public static SessionCounts Compute(IEnumerable<ChangeLogEntry> log, DateTime since)
    {
        int applied = 0, reverted = 0;
        foreach (var e in log)
        {
            if (e.DryRun || e.OccurredAt < since) continue;
            if (e.Action == "Apply") applied++;
            else if (e.Action == "Revert") reverted++;
        }
        return new SessionCounts(applied, reverted);
    }
}

/// <summary>Últimas N muestras de una métrica (para la mini-gráfica).</summary>
public sealed class MetricHistory
{
    private readonly Queue<double> _values = new();
    public int Capacity { get; }
    public MetricHistory(int capacity = 60) { Capacity = capacity; }

    public void Add(double value)
    {
        _values.Enqueue(value);
        while (_values.Count > Capacity) _values.Dequeue();
    }

    public IReadOnlyList<double> Snapshot() => _values.ToArray();
    public int Count => _values.Count;
}
