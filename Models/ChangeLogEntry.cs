namespace PC_Optimizer.Models;

public class ChangeLogEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SettingKey { get; set; } = string.Empty;
    public string SettingName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty; // Apply, Revert, DryRun
    public string OldValue { get; set; } = string.Empty;
    public string NewValue { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; } = DateTime.Now;
    public bool DryRun { get; set; }
    public string? Notes { get; set; }

    // ── Presentación (no altera los datos del registro) ────────────────────────

    /// <summary>La limpieza de %TEMP% no se puede deshacer.</summary>
    public bool IsPermanent => SettingKey.StartsWith("Cleanup.", System.StringComparison.Ordinal);

    /// <summary>Solo se puede revertir un cambio que sigue aplicado.</summary>
    public bool CanRevert => !IsPermanent && Action == "Apply";

    /// <summary>
    /// Igual que <see cref="CanRevert"/>, pero además comprueba que el ajuste siga
    /// teniendo su copia del valor original (incluida la recuperada del disco al arrancar).
    /// Lo asigna el ViewModel; no se serializa.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool RevertAvailable { get; set; }

    public string ActionText => Action switch
    {
        "Apply"  => "Aplicado",
        "Revert" => "Revertido",
        "DryRun" => "Simulado (sin cambios)",
        _        => Action
    };

    /// <summary>Detalle legible: acción + valores implicados + fecha.</summary>
    public string Detail
    {
        get
        {
            var values = string.IsNullOrWhiteSpace(OldValue) && string.IsNullOrWhiteSpace(NewValue)
                ? string.Empty
                : $" · {OldValue} → {NewValue}";
            return $"{ActionText}{values} · {OccurredAt:yyyy-MM-dd HH:mm}";
        }
    }
}
