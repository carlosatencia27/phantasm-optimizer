using System;

namespace PC_Optimizer.Core;

/// <summary>
/// Textos de ayuda en lenguaje simple. Una sola fuente para que la misma jerga se explique igual en
/// todas las pantallas. Solo explica qué ES algo: nunca promete un efecto que la app no pueda medir.
/// </summary>
public static class Glossary
{
    private static readonly (string Term, string Hint)[] Terms =
    {
        ("HAGS", "HAGS: deja que la tarjeta gráfica gestione su propia memoria. En algunos equipos no cambia nada."),
        ("TRIM", "TRIM: avisa al disco SSD de qué espacio ya no se usa."),
        ("DNS", "DNS: la «agenda» que traduce nombres de páginas web en direcciones."),
        ("LPDDR", "LPDDR: memoria RAM soldada a la placa, típica de portátiles; no se puede ampliar."),
        ("hiberfil.sys", "hiberfil.sys: archivo donde Windows guarda tu sesión al hibernar."),
        ("Inicio rápido", "Inicio rápido: Windows guarda parte del sistema al apagar pensado para encender más rápido; en algunos equipos no cambia nada."),
    };

    /// <summary>Frase corta para el primer término técnico que aparezca en el texto; null si no hay.</summary>
    public static string? HintFor(params string?[] texts)
    {
        foreach (var (term, hint) in Terms)
            foreach (var text in texts)
                if (text != null && text.Contains(term, StringComparison.Ordinal)) return hint;
        return null;
    }

    /// <summary>Texto único del tooltip de cada nivel de riesgo (no cambia el nivel de nadie).</summary>
    public static string RiskTooltip(string? level) => level switch
    {
        "Seguro" or "Safe" => "Riesgo seguro: bajo riesgo para Windows. Mira cada fila para saber si se puede deshacer.",
        "Moderado" or "Moderate" => "Riesgo moderado: cambia una opción de Windows; lee el aviso de la fila antes de aplicar.",
        "Avanzado" or "Advanced" => "Riesgo avanzado: requiere confirmación explícita antes de aplicarse.",
        _ => "Riesgo sin clasificar."
    };

    public static string RiskGlyph(string? level) => level switch
    {
        "Seguro" or "Safe" => "✓",
        "Moderado" or "Moderate" => "!",
        "Avanzado" or "Advanced" => "⚠",
        _ => "·"
    };
}
