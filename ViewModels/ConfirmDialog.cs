using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using PC_Optimizer.Core;

namespace PC_Optimizer.ViewModels;

public static class ConfirmDialog
{
    public static bool IsRunningAsAdmin()
    {
        try
        {
            return new System.Security.Principal.WindowsPrincipal(
                System.Security.Principal.WindowsIdentity.GetCurrent() ?? throw new InvalidOperationException())
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    /// <summary>Cómo se pregunta al usuario (mensaje, título). Las pruebas lo sustituyen para no abrir ventanas.</summary>
    public static Func<string, string, bool> Prompt { get; set; } = (message, title) =>
        System.Windows.MessageBox.Show(message, title, System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning,
            System.Windows.MessageBoxResult.No) == System.Windows.MessageBoxResult.Yes;

    /// <summary>Texto de la confirmación previa a aplicar un ajuste; null si no hace falta preguntar.</summary>
    public static string? BuildRequestMessage(ISetting setting)
    {
        var requisitos = new List<string>();
        if (setting.RequiresAdmin) requisitos.Add("Permisos de administrador.");
        if (setting.RequiresRestart) requisitos.Add("Reiniciar el equipo para que surta efecto.");
        if (setting.RequiresConfirmation) requisitos.Add("Confirmación explícita.");

        string? warning = null;
        try { warning = setting.PreApplyWarning; } catch { }

        if (requisitos.Count == 0 && string.IsNullOrWhiteSpace(warning)) return null;

        var msg = string.Empty;
        if (!string.IsNullOrWhiteSpace(warning))
            msg += "⚠ " + warning.Trim() + Environment.NewLine + Environment.NewLine;
        if (requisitos.Count > 0)
            msg += "Antes de aplicar este ajuste se requiere:" + Environment.NewLine
                + string.Join(Environment.NewLine, requisitos.Select(r => "- " + r))
                + Environment.NewLine + Environment.NewLine;
        // Una sola pregunta al final, aunque haya advertencia y requisitos a la vez.
        return msg + "¿Desea continuar?";
    }

    public static bool Request(ISetting setting, bool isAdmin)
    {
        var msg = BuildRequestMessage(setting);
        if (msg == null) return true;
        return Prompt(msg, "Confirmar ajuste");
    }

    public static bool RequestProfile(IEnumerable<ISetting> settings)
    {
        var requisitos = settings
            .Where(s => s.RequiresConfirmation || s.RequiresRestart || s.RequiresAdmin)
            .SelectMany(s =>
            {
                var lista = new List<string>();
                if (s.RequiresAdmin) lista.Add($"{s.Name}: administrador");
                if (s.RequiresRestart) lista.Add($"{s.Name}: reinicio");
                if (s.RequiresConfirmation) lista.Add($"{s.Name}: confirmación");
                return lista;
            })
            .ToList();

        if (requisitos.Count == 0) return true;

        var msg = "Antes de aplicar este perfil se requiere:" + Environment.NewLine
            + string.Join(Environment.NewLine, requisitos.Select(r => "- " + r))
            + Environment.NewLine + Environment.NewLine
            + "¿Desea continuar?";
        return System.Windows.MessageBox.Show(msg, "Confirmar perfil", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes;
    }

    public static string RequirementsText(ISetting setting)
    {
        var requisitos = new List<string>();
        if (setting.RequiresAdmin) requisitos.Add("requiere administrador");
        if (setting.RequiresRestart) requisitos.Add("requiere reinicio");
        if (setting.RequiresConfirmation) requisitos.Add("requiere confirmación");
        return requisitos.Count > 0 ? string.Join(" | ", requisitos) : string.Empty;
    }
}
