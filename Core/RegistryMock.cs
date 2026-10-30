using Microsoft.Win32;

namespace PC_Optimizer.Core;

/// <summary>
/// Registro simulado para pruebas automáticas. No toca el registro real de Windows.
/// </summary>
public class RegistryMock
{
    private readonly Dictionary<string, object?> _store = new();
    private readonly object _lock = new();

    public void SetValue(string key, object? value)
    {
        lock (_lock) { _store[key] = value; }
    }

    public object? GetValue(string key, object? defaultValue = null)
    {
        lock (_lock)
        {
            return _store.TryGetValue(key, out var v) ? v : defaultValue;
        }
    }

    public bool ContainsKey(string key)
    {
        lock (_lock) { return _store.ContainsKey(key); }
    }

    public void Remove(string key)
    {
        lock (_lock) { _store.Remove(key); }
    }

    public void RemoveValue(string valueName)
    {
        lock (_lock) { _store.Remove(valueName); }
    }

    public void Clear() => _store.Clear();
}
