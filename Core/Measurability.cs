namespace PC_Optimizer.Core;

/// <summary>
/// «¿Se puede medir?» cada ajuste con la app, en lenguaje simple (Bloque 3 de la Etapa 13).
/// Se concentra aquí para que la fila del Panel y las pruebas usen el mismo texto honesto.
/// </summary>
public static class Measurability
{
    public const string NotMeasurable = "No medible por la app: no hay una medida directa de su efecto.";

    public static string For(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return NotMeasurable;

        return key switch
        {
            "Network.Dns"               => "Medible en Antes y después: tiempo de DNS",
            "Power.Hibernate"           => "Medible en Antes y después: espacio libre",
            "System.FastStartup"        => "Medible en Antes y después: último arranque (tras apagar y encender)",
            "System.MouseAcceleration"  => "No medible por la app: cambia la sensación, no el rendimiento",
            "System.Transparency"       => "No medible por la app: cambia la sensación, no el rendimiento",
            "System.MenuShowDelay"      => "No medible por la app: cambia la sensación, no el rendimiento",
            "Game.GameBar"              => "Depende de tu juego: compruébalo con un contador de FPS",
            "Game.GameMode"             => "Depende de tu juego: compruébalo con un contador de FPS",
            "Game.WindowedOptimization" => "Depende de tu juego: compruébalo con un contador de FPS",
            "Game.Hags"                 => "Depende de tu juego: compruébalo con un contador de FPS",
            "Power.ActivePlan"          => "No medible por la app: depende de tu equipo",
            "Monitor.RefreshRate"       => "No aplica: solo lectura",
            _ when key.StartsWith("Startup.", System.StringComparison.Ordinal)
                                        => "Medible en Antes y después: programas de inicio (el arranque se comprueba tras reiniciar)",
            _                           => NotMeasurable
        };
    }
}
