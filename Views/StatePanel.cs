using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PC_Optimizer.Views;

/// <summary>
/// Estado único para "cargando", "vacío" y "error": mismo aspecto y misma estructura (título,
/// frase de explicación y, si hace falta, un botón «Reintentar») en todas las pantallas.
/// Kind: Loading | Empty | Error.
/// </summary>
public class StatePanel : Border
{
    public static readonly DependencyProperty KindProperty = Reg<string>(nameof(Kind), "Empty");
    public static readonly DependencyProperty TitleProperty = Reg<string>(nameof(Title), "");
    public static readonly DependencyProperty MessageProperty = Reg<string>(nameof(Message), "");
    public static readonly DependencyProperty ActionTextProperty = Reg<string>(nameof(ActionText), "");
    public static readonly DependencyProperty ActionCommandProperty = Reg<ICommand?>(nameof(ActionCommand), null);

    private static DependencyProperty Reg<T>(string name, T? def) => DependencyProperty.Register(
        name, typeof(T), typeof(StatePanel),
        new PropertyMetadata(def, (d, _) => ((StatePanel)d).Rebuild()));

    public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Message { get => (string)GetValue(MessageProperty); set => SetValue(MessageProperty, value); }
    public string ActionText { get => (string)GetValue(ActionTextProperty); set => SetValue(ActionTextProperty, value); }
    public ICommand? ActionCommand { get => (ICommand?)GetValue(ActionCommandProperty); set => SetValue(ActionCommandProperty, value); }

    public StatePanel()
    {
        Loaded += (_, _) => Rebuild();
    }

    private void Rebuild()
    {
        if (TryFindResource("EmptyState") is Style style) Style = style;
        var stack = new StackPanel();
        bool isError = Kind == "Error";
        var title = new TextBlock { Text = Title, TextWrapping = TextWrapping.Wrap };
        if (TryFindResource("TextSubtitle") is Style ts) title.Style = ts;
        if (isError && TryFindResource("BrushDanger") is System.Windows.Media.Brush danger) title.Foreground = danger;
        stack.Children.Add(title);

        if (Kind == "Loading")
        {
            var bar = new System.Windows.Controls.ProgressBar { IsIndeterminate = true, Height = 4, Margin = new Thickness(0, 10, 0, 0) };
            stack.Children.Add(bar);
        }
        if (!string.IsNullOrWhiteSpace(Message))
        {
            var msg = new TextBlock { Text = Message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
            if (TryFindResource("TextCaption") is Style tc) msg.Style = tc;
            stack.Children.Add(msg);
        }
        if (!string.IsNullOrWhiteSpace(ActionText) && ActionCommand != null)
        {
            var button = new System.Windows.Controls.Button { Content = ActionText, Command = ActionCommand, HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
            if (TryFindResource("ButtonGhost") is Style bs) button.Style = bs;
            stack.Children.Add(button);
        }
        Child = stack;
    }
}
