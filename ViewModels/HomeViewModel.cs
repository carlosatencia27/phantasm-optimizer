using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace PC_Optimizer.ViewModels;

/// <summary>Un paso del camino de primer uso. Solo navega: nada se aplica desde esta pantalla.</summary>
public sealed class HomeStep : INotifyPropertyChanged
{
    private bool _isDone;
    private bool _isNext;
    public int Number { get; init; }
    public string Title { get; init; } = "";
    public string Text { get; init; } = "";
    public string ButtonText { get; init; } = "";
    public ICommand GoCommand { get; init; } = null!;
    public bool IsDone { get => _isDone; set { _isDone = value; Raise(); Raise(nameof(StatusText)); } }
    public bool IsNext { get => _isNext; set { _isNext = value; Raise(); Raise(nameof(IsNotNext)); } }
    public bool IsNotNext => !_isNext;
    public string StatusText => _isDone ? "Hecho" : "Pendiente";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>
/// Pantalla de inicio: tres pasos (analizar, revisar y aplicar lo recomendado, ver el resultado).
/// Cada botón solo abre la pantalla correspondiente; el punto de restauración y las confirmaciones
/// siguen ocurriendo allí, con la decisión del usuario.
/// </summary>
public sealed class HomeViewModel : INotifyPropertyChanged
{
    private readonly GuidedOptimizationViewModel _guided;
    public ObservableCollection<HomeStep> Steps { get; } = new();

    public PanelViewModel? Panel { get; }
    private PC_Optimizer.Services.SessionCounts _session;
    /// <summary>Lo aplicado y revertido DE VERDAD en esta sesión (del registro del motor).</summary>
    public PC_Optimizer.Services.SessionCounts Session { get => _session; set { _session = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Session))); } }
    public event PropertyChangedEventHandler? PropertyChanged;

    public HomeViewModel(Action<string?> navigate, GuidedOptimizationViewModel guided, PanelViewModel? panel = null)
    {
        _guided = guided;
        Panel = panel;
        Steps.Add(new HomeStep
        {
            Number = 1, Title = "Analizar mi PC",
            Text = "Busca qué se podría mejorar. Solo lee datos de tu equipo: no cambia nada.",
            ButtonText = "Analizar mi PC", GoCommand = new RelayCommand(_ => navigate("Guided"))
        });
        Steps.Add(new HomeStep
        {
            Number = 2, Title = "Revisar y aplicar lo recomendado",
            Text = "Tú marcas qué aplicar y confirmas antes de que se cambie nada. Se intenta crear un punto de restauración (en Modo simulación no se crea nada).",
            ButtonText = "Revisar lo recomendado", GoCommand = new RelayCommand(_ => navigate("Guided"))
        });
        Steps.Add(new HomeStep
        {
            Number = 3, Title = "Ver el resultado",
            Text = "Compara cómo estaba tu PC antes y después (solo muestra diferencias medidas). Los ajustes se pueden deshacer; la limpieza de archivos, no.",
            ButtonText = "Ver resultados", GoCommand = new RelayCommand(_ => navigate("Measurement"))
        });
        Refresh();
    }

    /// <summary>Recalcula qué pasos están hechos (se llama cada vez que se muestra la pantalla).</summary>
    public void Refresh()
    {
        bool analyzed = _guided.Recommendations.Count > 0;
        bool applied = _guided.Report != null;
        Steps[0].IsDone = analyzed;
        Steps[1].IsDone = applied;
        Steps[2].IsDone = false; // no hay forma fiable de saber si ya lo viste
        Steps[0].IsNext = !analyzed;
        Steps[1].IsNext = analyzed && !applied;
        Steps[2].IsNext = applied;
    }
}
