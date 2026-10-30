namespace PC_Optimizer.Models;

public class BackupEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SettingKey { get; set; } = string.Empty;
    public string SettingName { get; set; } = string.Empty;
    public string OriginalValue { get; set; } = string.Empty;
    public string CurrentValue { get; set; } = string.Empty;

    /// <summary>
    /// Tipo real del valor original (AssemblyQualifiedName). Permite reconstruirlo al
    /// reabrir la app para que «Revertir» funcione después de reiniciar.
    /// </summary>
    public string? OriginalValueType { get; set; }

    /// <summary>Valor original serializado en JSON (nulo cuando la clave no existía).</summary>
    public string? OriginalValueJson { get; set; }

    /// <summary>true si el valor original existía en el sistema (false = había que borrar la clave).</summary>
    public bool OriginalValueExisted { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public bool IsReverted { get; set; }
    public DateTime? RevertedAt { get; set; }
}
