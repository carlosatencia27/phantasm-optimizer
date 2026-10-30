using System;
using System.Threading;
using System.Threading.Tasks;

namespace PC_Optimizer.Services;

/// <summary>
/// Crea el punto de restauración sin bloquear la interfaz y con un tiempo límite, igual para el botón del
/// punto de restauración y para la Optimización guiada. Si Windows no contesta a tiempo la app deja de
/// esperar (la llamada de Windows sigue por su cuenta) y un nuevo intento avisa en vez de lanzar otra
/// llamada encima. El temporizador se cancela en cuanto Windows termina, y una excepción que llegue tarde
/// se registra en el registro local (solo tipo y HRESULT, nunca el mensaje interno).
/// </summary>
public sealed class RestorePointCoordinator
{
    /// <summary>
    /// Crear un punto suele tardar de unos segundos a medio minuto; en un disco lento o con mucha actividad
    /// puede llegar a uno o dos minutos. 120 s cubre los casos normales sin que la barra gire sin tope.
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(120);

    private Task? _pending;

    public TimeSpan Timeout { get; set; } = DefaultTimeout;

    /// <summary>true mientras una llamada anterior a Windows sigue sin terminar (incluso tras agotarse el tiempo).</summary>
    public bool IsRunning => _pending is { IsCompleted: false };

    private sealed class LateState { public volatile bool TimedOut; }

    /// <summary>
    /// Devuelve el resultado ya clasificado (creado, tiempo agotado o «todavía en curso»). Las excepciones
    /// que ocurran DENTRO del plazo se propagan para que quien llama las traduzca con
    /// <see cref="RestorePointMessages.FromException"/>.
    /// </summary>
    public async Task<RestorePointResult> CreateAsync(IRestorePointService service, string description)
    {
        if (IsRunning)
            return new RestorePointResult(RestorePointOutcome.StillRunning, RestorePointMessages.StillRunning);

        var state = new LateState();
        var work = Task.Run(() => service.Create(description));
        _pending = work;

        // Resultado tardío (después de agotarse el tiempo): se registra y no queda sin observar.
        _ = work.ContinueWith(t =>
        {
            if (!state.TimedOut) return;
            try
            {
                if (t.IsFaulted && t.Exception?.GetBaseException() is { } ex)
                {
                    UserErrorText.Logger.Write("Punto de restauración: Windows devolvió un error después del tiempo límite.");
                    _ = UserErrorText.Of(ex); // registra tipo y HRESULT, sin el mensaje interno
                }
                else if (t.IsCompletedSuccessfully)
                {
                    UserErrorText.Logger.Write($"Punto de restauración: Windows terminó después del tiempo límite ({t.Result.Outcome}).");
                }
            }
            catch
            {
                // Registrar nunca debe provocar otro error.
            }
        }, TaskScheduler.Default);

        using var timer = new CancellationTokenSource();
        var delay = Task.Delay(Timeout, timer.Token);
        var finished = await Task.WhenAny(work, delay).ConfigureAwait(true);

        if (ReferenceEquals(finished, work))
        {
            timer.Cancel();               // Windows terminó antes: el temporizador ya no hace falta
            return await work;
        }

        state.TimedOut = true;
        return RestorePointMessages.TimedOut(Timeout);
    }
}
