using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace PC_Optimizer.Services;

public enum ToastKind { Info, Success, Error }

/// <summary>Programa una acción única tras un retraso (en la app: DispatcherTimer; en pruebas: simulado).</summary>
public interface IToastScheduler
{
    void Schedule(TimeSpan delay, Action action);
}

public sealed class DispatcherToastScheduler : IToastScheduler
{
    public void Schedule(TimeSpan delay, Action action)
    {
        if (delay <= TimeSpan.Zero) { action(); return; }
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = delay };
        timer.Tick += (_, _) => { timer.Stop(); action(); };
        timer.Start();
    }
}

/// <summary>Un aviso flotante. Solo informa de algo que el motor ya confirmó.</summary>
public sealed class ToastItem : INotifyPropertyChanged
{
    private bool _isClosing;
    internal ToastItem(ToastManager owner, string message, ToastKind kind, Action? undo)
    {
        Message = message; Kind = kind; Undo = undo;
        CloseCommand = new RelayCommand(_ => owner.Close(this));
        UndoCommand = new RelayCommand(_ => owner.RunUndo(this));
    }

    public string Message { get; }
    public ToastKind Kind { get; }
    public Action? Undo { get; }
    public bool HasUndo => Undo != null;
    public bool IsError => Kind == ToastKind.Error;
    public bool IsClosing { get => _isClosing; internal set { _isClosing = value; Raise(); } }
    public ICommand CloseCommand { get; }
    public ICommand UndoCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>
/// Cola de avisos: máximo 3 visibles (los demás esperan), cierre automático a los 4 s (8 s si
/// tienen «Deshacer»), los errores NO se cierran solos, y en Modo simulación el texto empieza por
/// «Simulado:». La lógica no depende de la interfaz para poder probarla.
/// </summary>
public sealed class ToastManager
{
    public static readonly TimeSpan AutoCloseDelay = TimeSpan.FromSeconds(4);
    public static readonly TimeSpan UndoDelay = TimeSpan.FromSeconds(8);

    private readonly IToastScheduler _scheduler;
    private readonly Func<TimeSpan> _exitDelay;
    private readonly Queue<ToastItem> _waiting = new();

    public int MaxVisible { get; } = 3;
    public ObservableCollection<ToastItem> Items { get; } = new();
    public event Action? Changed;

    public ToastManager(IToastScheduler? scheduler = null, Func<TimeSpan>? exitDelay = null)
    {
        _scheduler = scheduler ?? new DispatcherToastScheduler();
        _exitDelay = exitDelay ?? (() => Motion.Enabled ? TimeSpan.FromMilliseconds(250) : TimeSpan.Zero);
    }

    public ToastItem Show(string message, ToastKind kind = ToastKind.Info, Action? undo = null, bool simulated = false)
    {
        if (string.IsNullOrEmpty(message)) message = " ";
        if (simulated && !message.StartsWith("Simulado", StringComparison.Ordinal))
            message = "Simulado: " + char.ToLowerInvariant(message[0]) + message[1..];
        // En simulación no hay nada que deshacer.
        var item = new ToastItem(this, message, kind, simulated ? null : undo);
        if (Items.Count < MaxVisible) Present(item); else _waiting.Enqueue(item);
        Changed?.Invoke();
        return item;
    }

    private void Present(ToastItem item)
    {
        Items.Add(item);
        if (item.Kind == ToastKind.Error) return; // los errores se quedan hasta que el usuario los cierre
        _scheduler.Schedule(item.HasUndo ? UndoDelay : AutoCloseDelay, () => Close(item));
    }

    public void RunUndo(ToastItem item)
    {
        if (item.IsClosing || item.Undo == null) return;
        Close(item);
        item.Undo();
    }

    public void Close(ToastItem item)
    {
        if (item.IsClosing || !Items.Contains(item)) return;
        item.IsClosing = true;
        _scheduler.Schedule(_exitDelay(), () =>
        {
            Items.Remove(item);
            if (_waiting.Count > 0 && Items.Count < MaxVisible) Present(_waiting.Dequeue());
            Changed?.Invoke();
        });
    }
}
