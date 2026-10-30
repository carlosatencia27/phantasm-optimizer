using System.Windows;
using System.Windows.Controls;
using PC_Optimizer.Converters;
using PC_Optimizer.Core;

namespace PC_Optimizer.Views;

/// <summary>
/// Etiqueta de riesgo ÚNICA (Seguro / Moderado / Avanzado): mismo símbolo, color, texto y tooltip en
/// todas las pantallas. Solo presenta el nivel que ya tiene cada ajuste; no lo decide.
/// </summary>
public class RiskBadge : Border
{
    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level), typeof(string), typeof(RiskBadge),
        new PropertyMetadata(null, (d, _) => ((RiskBadge)d).Rebuild()));

    public string? Level
    {
        get => (string?)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    public RiskBadge()
    {
        BorderThickness = new Thickness(1);
        VerticalAlignment = VerticalAlignment.Center;
        if (TryFindResource("RadiusChip") is CornerRadius radius) CornerRadius = radius;
        if (TryFindResource("PaddingChip") is Thickness padding) Padding = padding;
        if (TryFindResource("MarginChipItem") is Thickness margin) Margin = margin;
        Loaded += (_, _) => Rebuild();
    }

    private void Rebuild()
    {
        var level = Level;
        var converter = new RiskColorConverter();
        var fg = (System.Windows.Media.Brush)converter.Convert(level, typeof(System.Windows.Media.Brush), "fg", System.Globalization.CultureInfo.InvariantCulture);
        Background = (System.Windows.Media.Brush)converter.Convert(level, typeof(System.Windows.Media.Brush), "", System.Globalization.CultureInfo.InvariantCulture);
        BorderBrush = fg;
        ToolTip = Glossary.RiskTooltip(level);

        var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        row.Children.Add(new TextBlock
        {
            Text = Glossary.RiskGlyph(level), Foreground = fg, Margin = new Thickness(0, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = TryFindResource("FontSizeChip") is double size ? size : 12
        });
        var label = new TextBlock { Text = level ?? "Sin clasificar", Foreground = fg };
        if (TryFindResource("TextChip") is Style chip) label.Style = chip;
        label.Foreground = fg;
        row.Children.Add(label);
        Child = row;
    }
}
