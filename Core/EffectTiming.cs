namespace PC_Optimizer.Core;

/// <summary>
/// «Cuándo se nota» cada ajuste en Windows, en lenguaje simple (Bloque 2 de la Etapa 12).
/// Se concentra aquí para que los ajustes, el Panel y las pruebas usen el mismo texto.
/// </summary>
public static class EffectTiming
{
    public const string NotConfirmed = "Se nota: no confirmado";

    /// <summary>Texto para el ajuste con esa clave. Nunca devuelve null.</summary>
    public static string For(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return NotConfirmed;

        return key switch
        {
            "Game.GameMode"             => "Se nota: al iniciar el siguiente juego",
            "Game.GameBar"              => "Se nota: al instante",
            "Game.WindowedOptimization" => "Se nota: al reiniciar el juego",
            "Game.Hags"                 => "Se nota: al reiniciar Windows",
            "Network.Dns"               => "Se nota: al instante (se vació la caché DNS)",
            "Power.ActivePlan"          => "Se nota: al instante",
            "Power.Hibernate"           => "Se nota: al instante",
            "Monitor.RefreshRate"       => "Se nota: no aplica (solo lectura)",
            "System.MenuShowDelay"      => "Se nota: al instante; si no cambia, al cerrar sesión",
            "System.FastStartup"        => "Se nota: en el próximo apagado",
            "System.MouseAcceleration"  => "Se nota: al instante",
            "System.Transparency"       => "Se nota: puede requerir cerrar sesión",
            _ when key.StartsWith("Startup.", System.StringComparison.Ordinal)
                                        => "Se nota: en el próximo inicio de sesión",
            _                           => NotConfirmed
        };
    }

    /// <summary>Explicación para el tooltip de la fila.</summary>
    public static string Tooltip(string key) => $"{For(key)}. Es el momento en que Windows aplica el cambio; " +
        "la app no reinicia el Explorador ni cierra la sesión.";
}
