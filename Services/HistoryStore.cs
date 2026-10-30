using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PC_Optimizer.Models;

namespace PC_Optimizer.Services;

/// <summary>
/// Guarda el historial de cambios y las copias del valor original (respaldos) en disco,
/// para que sobrevivan al cierre de la app (Principios 2 y 7).
/// </summary>
public interface IHistoryStore
{
    IReadOnlyList<BackupEntry> LoadBackups();
    IReadOnlyList<ChangeLogEntry> LoadChangeLog();
    void Save(IReadOnlyList<BackupEntry> backups, IReadOnlyList<ChangeLogEntry> changeLog);
}

/// <summary>
/// Implementación en JSON dentro de %LOCALAPPDATA%\PC Optimizer\history.json.
/// Un archivo ausente, vacío o dañado nunca hace fallar el arranque: se empieza de cero.
/// </summary>
public class HistoryStore : IHistoryStore
{
    private readonly string _filePath;
    private readonly object _lock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public HistoryStore(string? filePath = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            filePath = Path.Combine(appData, "PC Optimizer", "history.json");
        }

        _filePath = filePath!;
    }

    /// <summary>Forma del archivo en disco (una sola lista por tipo de dato).</summary>
    private sealed class HistoryFile
    {
        public List<BackupEntry> Backups { get; set; } = new();
        public List<ChangeLogEntry> ChangeLog { get; set; } = new();
    }

    public IReadOnlyList<BackupEntry> LoadBackups() => Read().Backups;

    public IReadOnlyList<ChangeLogEntry> LoadChangeLog() => Read().ChangeLog;

    private HistoryFile Read()
    {
        try
        {
            if (!File.Exists(_filePath)) return new HistoryFile();

            string json = File.ReadAllText(_filePath);
            if (string.IsNullOrWhiteSpace(json)) return new HistoryFile();

            var data = JsonSerializer.Deserialize<HistoryFile>(json, JsonOptions);
            return data ?? new HistoryFile();
        }
        catch
        {
            // Archivo ilegible o dañado: se ignora por completo y la app arranca con el historial vacío.
            return new HistoryFile();
        }
    }

    public void Save(IReadOnlyList<BackupEntry> backups, IReadOnlyList<ChangeLogEntry> changeLog)
    {
        lock (_lock)
        {
            try
            {
                var directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                var data = new HistoryFile
                {
                    Backups = backups.ToList(),
                    ChangeLog = changeLog.ToList()
                };

                File.WriteAllText(_filePath, JsonSerializer.Serialize(data, JsonOptions));
            }
            catch
            {
                // Si no se puede escribir, la app sigue funcionando: solo se pierde el historial al cerrar.
            }
        }
    }
}
