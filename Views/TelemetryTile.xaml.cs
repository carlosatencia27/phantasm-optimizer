using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace PC_Optimizer.Views;

public partial class TelemetryTile : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty TitleProperty = Reg<string>(nameof(Title), "", (t, _) => { });
    public static readonly DependencyProperty PercentProperty = Reg<double>(nameof(Percent), 0.0, (t, _) => t.Update());
    public static readonly DependencyProperty ValueTextProperty = Reg<string?>(nameof(ValueText), null, (t, _) => t.Update());
    public static readonly DependencyProperty DetailProperty = Reg<string?>(nameof(Detail), null, (t, _) => t.Update());
    public static readonly DependencyProperty IsAvailableProperty = Reg<bool>(nameof(IsAvailable), true, (t, _) => t.Update());
    public static readonly DependencyProperty HistoryProperty = Reg<IReadOnlyList<double>?>(nameof(History), null, (t, _) => t.Update());
    public static readonly DependencyProperty AccentProperty = Reg<Brush>(nameof(Accent), Brushes.Cyan, (t, _) => t.Update());

    private static DependencyProperty Reg<T>(string name, T? def, System.Action<TelemetryTile, object?> changed) =>
        DependencyProperty.Register(name, typeof(T), typeof(TelemetryTile),
            new PropertyMetadata(def, (d, e) => changed((TelemetryTile)d, e.NewValue)));

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public double Percent { get => (double)GetValue(PercentProperty); set => SetValue(PercentProperty, value); }
    public string? ValueText { get => (string?)GetValue(ValueTextProperty); set => SetValue(ValueTextProperty, value); }
    public string? Detail { get => (string?)GetValue(DetailProperty); set => SetValue(DetailProperty, value); }
    public bool IsAvailable { get => (bool)GetValue(IsAvailableProperty); set => SetValue(IsAvailableProperty, value); }
    public IReadOnlyList<double>? History { get => (IReadOnlyList<double>?)GetValue(HistoryProperty); set => SetValue(HistoryProperty, value); }
    public Brush Accent { get => (Brush)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }

    public TelemetryTile()
    {
        InitializeComponent();
        Loaded += (_, _) => Update();
    }

    private void Update()
    {
        if (Ring == null) return;
        Ring.Stroke = Accent;
        Spark.Stroke = Accent;
        Ring.IsAvailable = IsAvailable;
        Ring.Value = Percent;
        ValueBlock.Text = IsAvailable ? (ValueText ?? $"{Percent:0} %") : "No disponible";
        DetailBlock.Text = IsAvailable ? Detail ?? string.Empty : string.Empty;
        Spark.Values = IsAvailable ? History : null;
    }

}
