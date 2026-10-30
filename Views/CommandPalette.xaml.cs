using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using PC_Optimizer.Services;

namespace PC_Optimizer.Views;

public partial class CommandPalette : System.Windows.Controls.UserControl
{
    private IReadOnlyList<PaletteEntry> _all = Array.Empty<PaletteEntry>();
    private Action<PaletteEntry>? _choose;

    public CommandPalette()
    {
        InitializeComponent();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    public bool IsOpen => Visibility == Visibility.Visible;

    /// <summary>Abre la paleta con las entradas dadas; <paramref name="choose"/> solo debe navegar.</summary>
    public void Open(IReadOnlyList<PaletteEntry> all, Action<PaletteEntry> choose)
    {
        _all = all; _choose = choose;
        Box.Text = string.Empty;
        Refresh();
        Visibility = Visibility.Visible;
        Box.Focus();
    }

    public void Close()
    {
        Visibility = Visibility.Collapsed;
        _choose = null;
    }

    private void Refresh()
    {
        var found = CommandPaletteModel.Search(_all, Box.Text);
        Results.ItemsSource = found;
        if (found.Count > 0) Results.SelectedIndex = 0;
    }

    private void OnTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => Refresh();

    private void OnBackdropMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => Close();

    private void OnResultDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => ChooseSelected();

    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: Close(); e.Handled = true; break;
            case Key.Enter: ChooseSelected(); e.Handled = true; break;
            case Key.Down: Move(+1); e.Handled = true; break;
            case Key.Up: Move(-1); e.Handled = true; break;
        }
    }

    private void Move(int delta)
    {
        int count = Results.Items.Count;
        if (count == 0) return;
        Results.SelectedIndex = Math.Clamp(Results.SelectedIndex + delta, 0, count - 1);
        Results.ScrollIntoView(Results.SelectedItem);
    }

    private void ChooseSelected()
    {
        if (Results.SelectedItem is PaletteEntry entry)
        {
            var choose = _choose;
            Close();
            choose?.Invoke(entry);
        }
    }
}
