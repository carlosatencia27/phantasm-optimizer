using System.IO;
using System.Text.Json;

namespace PC_Optimizer.Services;

public class UserSettings
{
    // Principio 6: simulación activa por defecto. El usuario debe desactivarla explícitamente.
    public bool DryRunMode { get; set; } = true;
    public bool StartupEnabled { get; set; } = false;
    public bool ShowAdvanced { get; set; } = false;
    public bool MinimizeToTray { get; set; } = true;
}

public interface IUserSettingsService
{
    UserSettings Settings { get; }
    void Save();
    void Reload();
}

public class UserSettingsService : IUserSettingsService
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    public UserSettings Settings { get; private set; } = new();

    public UserSettingsService(string? filePath = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _filePath = Path.Combine(appData, "PC Optimizer", "settings.json");
        }
        else
        {
            _filePath = filePath;
        }
        Reload();
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(Settings, _jsonOptions);
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(_filePath, json);
        }
        catch
        {
            // No poder guardar las preferencias no debe impedir usar la app.
        }
    }

    public void Reload()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                if (!string.IsNullOrWhiteSpace(json))
                    Settings = JsonSerializer.Deserialize<UserSettings>(json) ?? new UserSettings();
            }
            else
            {
                // Primer arranque: se crea el archivo con los valores por defecto, con el modo
                // simulación ACTIVADO (principio 6: el usuario debe desactivarlo explícitamente).
                Settings = new UserSettings();
                Save();
            }
        }
        catch
        {
            // Archivo ausente o dañado: se arranca con los valores por defecto (modo simulación activado).
            Settings = new UserSettings();
        }
    }
}
