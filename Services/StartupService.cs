using Microsoft.Win32;

namespace PC_Optimizer.Services;

public interface IRegistryRunKey
{
    object? GetValue(string name);
    void SetValue(string name, object? value);
    void DeleteValue(string name, bool throwOnMissingValue = false);
    bool ContainsValue(string name);
}

public class RegistryRunKey : IRegistryRunKey
{
    private readonly RegistryKey _key;

    public RegistryRunKey(RegistryKey key) => _key = key;

    public object? GetValue(string name) => _key.GetValue(name);

    public void SetValue(string name, object? value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _key.SetValue(name, value);
    }

    public void DeleteValue(string name, bool throwOnMissingValue = false) => _key.DeleteValue(name, throwOnMissingValue);

    public bool ContainsValue(string name) => _key.GetValue(name) != null;
}

public class StartupService : IStartupService
{
    private const string ValueName = "PC Optimizer";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly IRegistryRunKey _runKey;
    private readonly string _exePath;

    public StartupService(IRegistryRunKey runKey, string exePath)
    {
        _runKey = runKey;
        _exePath = exePath;
    }

    public bool IsEnabled => _runKey.ContainsValue(ValueName);

    public void Enable()
    {
        _runKey.SetValue(ValueName, $"\"{_exePath!}\"");
    }

    public void Disable()
    {
        _runKey.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
