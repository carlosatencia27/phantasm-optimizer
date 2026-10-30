using Microsoft.Win32;

namespace PC_Optimizer.Services;

public interface IGameRegistryService
{
    string SubKeyPath { get; }
    object? GetValue(string valueName);
    bool SetValue(string valueName, object? value);
    bool TryGetValue(string valueName, out object? result);
    bool DeleteValue(string valueName);
}

public class GameRegistryService : IGameRegistryService
{
    private readonly RegistryKey _rootKey;

    public GameRegistryService(string subKeyPath)
        : this(Registry.CurrentUser, subKeyPath)
    {
    }

    public GameRegistryService(RegistryKey rootKey, string subKeyPath)
    {
        SubKeyPath = subKeyPath;
        _rootKey = rootKey;
    }

    public string SubKeyPath { get; }

    public object? GetValue(string valueName)
    {
        try
        {
            using var key = _rootKey.OpenSubKey(SubKeyPath, writable: false);
            return key?.GetValue(valueName);
        }
        catch { return null; }
    }

    public bool SetValue(string valueName, object? value)
    {
        try
        {
            using var key = _rootKey.OpenSubKey(SubKeyPath, writable: true)
                ?? _rootKey.CreateSubKey(SubKeyPath, RegistryKeyPermissionCheck.ReadWriteSubTree);
            if (key == null) return false;
            key.SetValue(valueName, value!);
            return true;
        }
        catch { return false; }
    }

    public bool TryGetValue(string valueName, out object? result)
    {
        result = GetValue(valueName);
        return result != null;
    }

    public bool DeleteValue(string valueName)
    {
        try
        {
            using var key = _rootKey.OpenSubKey(SubKeyPath, writable: true);
            if (key == null) return false;
            key.DeleteValue(valueName, throwOnMissingValue: false);
            return true;
        }
        catch { return false; }
    }
}
