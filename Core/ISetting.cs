namespace PC_Optimizer.Core;

using PC_Optimizer.Models;

public interface ISetting
{
    string Key { get; }
    string Name { get; }
    string Description { get; }
    string RealisticEffect { get; }
    SettingRiskLevel RiskLevel { get; }
    bool IsWindowsVersionSupported { get; }
    bool IsReversible { get; }
    bool RequiresConfirmation { get; }
    bool RequiresRestart { get; }
    bool RequiresAdmin { get; }
    object? ReadCurrentState(); // lee el estado real del sistema

    /// <summary>
    /// Valor que el interruptor envía al activarse. Cada ajuste decide el suyo para que lo que se
    /// escribe coincida con lo que promete la descripción: nunca un «true» genérico cuando el
    /// ajuste espera otra cosa (p. ej. Game Bar escribe 0 para desactivar la captura).
    /// </summary>
    object? ApplyValue { get; }

    /// <summary>
    /// Valor que el interruptor envía al desactivarse cuando no hay copia original guardada
    /// (comportamiento predeterminado de Windows para ese ajuste). Si hay copia original, la
    /// reversión usa <see cref="Revert"/> y restaura el valor exacto.
    /// </summary>
    object? RevertValue { get; }

    /// <summary>
    /// Comprueba, leyendo el sistema justo ahora, si el valor actual ya coincide con el valor que
    /// el ajuste considera «aplicado». Es la única fuente del estado del interruptor: no depende
    /// de que exista una copia original guardada (principio 3). Nunca lanza.
    /// </summary>
    bool IsAppliedInSystem
    {
        get
        {
            try { return ReadCurrentState() is bool applied && applied; }
            catch { return false; }
        }
    }

    void Apply(object? newValue, bool dryRun);
    void Revert(bool dryRun);
    object? OriginalValue { get; }
    bool HasOriginal { get; }

    /// <summary>
    /// Repone la copia del valor original leída de disco (reversibilidad tras reiniciar la app).
    /// No toca el sistema: solo restaura el estado interno del ajuste. Los ajustes de solo
    /// lectura la ignoran.
    /// </summary>
    void RestoreBackup(object? originalValue, bool originalExisted);
    string VerificationStatus { get; } // "Documentado por Microsoft" | "Observado en Windows, no documentado oficialmente" | "No verificado"
    string? DocumentationUrl { get; }

    /// <summary>
    /// Cuándo se nota el cambio en Windows, en lenguaje simple («Se nota: al instante»,
    /// «Se nota: al reiniciar Windows»...). El valor sale de <see cref="EffectTiming"/> por clave.
    /// </summary>
    string EffectTimingText => EffectTiming.For(Key);

    /// <summary>
    /// Si el efecto se puede medir con «Antes y después» y en qué métrica, en lenguaje simple.
    /// El valor sale de <see cref="Measurability"/> por clave.
    /// </summary>
    string MeasurableText => Measurability.For(Key);

    /// <summary>
    /// Advertencia que depende del equipo (batería, varias GPU...) y se muestra con una confirmación ANTES
    /// de aplicar. null = no hace falta preguntar nada más.
    /// </summary>
    string? PreApplyWarning => null;
}
