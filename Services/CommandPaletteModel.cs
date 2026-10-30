using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace PC_Optimizer.Services;

public enum PaletteKind { Screen, Setting }

/// <summary>Una entrada de la paleta de comandos: solo sirve para NAVEGAR (nunca aplica nada).</summary>
public sealed record PaletteEntry(string Title, string Subtitle, PaletteKind Kind, string Section, string? SettingKey = null);

public static class CommandPaletteModel
{
    /// <summary>Minúsculas y sin tildes, para buscar «optimizacion» y encontrar «Optimización».</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var formD = text.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var ch in formD)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }

    /// <summary>
    /// Con la búsqueda vacía lista las pantallas; si no, ordena por: el título empieza por el texto,
    /// el título lo contiene, el subtítulo lo contiene. Devuelve como máximo <paramref name="max"/> entradas.
    /// </summary>
    public static IReadOnlyList<PaletteEntry> Search(IEnumerable<PaletteEntry> all, string? query, int max = 8)
    {
        var list = all.ToList();
        var q = Normalize(query);
        if (q.Length == 0) return list.Where(e => e.Kind == PaletteKind.Screen).Take(max).ToList();

        return list
            .Select((entry, index) => (entry, index, score: Score(entry, q)))
            .Where(x => x.score >= 0)
            .OrderBy(x => x.score).ThenBy(x => x.entry.Kind).ThenBy(x => x.index)
            .Select(x => x.entry)
            .Take(max)
            .ToList();
    }

    private static int Score(PaletteEntry e, string q)
    {
        var title = Normalize(e.Title);
        if (title.StartsWith(q, StringComparison.Ordinal)) return 0;
        if (title.Contains(q, StringComparison.Ordinal)) return 1;
        if (Normalize(e.Subtitle).Contains(q, StringComparison.Ordinal)) return 2;
        return -1;
    }
}
