using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace PC_Optimizer.Converters;

/// <summary>Convierte bool isAdmin a color del indicador.</summary>
public class AdminStatusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isAdmin = value is bool b && b;
        return new SolidColorBrush(isAdmin
            ? Color.FromRgb(0x6F, 0xDC, 0x8C)  // verde éxito
            : Color.FromRgb(0xFF, 0x70, 0x70)); // rojo peligro
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Convierte bool isAdmin a texto descriptivo.</summary>
public class AdminTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isAdmin = value is bool b && b;
        return isAdmin ? "Administrador activo" : "Sin permisos de administrador";
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Invierte un bool.</summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : true;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : false;
}

/// <summary>bool false → Visible, true → Collapsed (para botón Reiniciar como admin).</summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isAdmin = value is bool b && b;
        return isAdmin ? Visibility.Collapsed : Visibility.Visible;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>bool → Visible / Collapsed directo.</summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool v = value is bool b && b;
        return v ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Calcula el ancho del indicador de una barra de progreso custom.</summary>
public class ProgressWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 3) return 0.0;
        double val = values[0] is double d ? d : 0;
        double max = values[1] is double m && m > 0 ? m : 100;
        double width = values[2] is double w ? w : 0;
        return Math.Max(0, Math.Min(width, width * val / max));
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Convierte un valor de porcentaje (0-100) al color de la barra de progreso.</summary>
public class PercentToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double pct = value is double d ? d : (value is float f ? f : 0);
        if (pct < 60) return new SolidColorBrush(Color.FromRgb(0x22, 0xD3, 0xEE)); // éxito (menta)
        if (pct < 80) return new SolidColorBrush(Color.FromRgb(0xF5, 0xB9, 0x42)); // aviso
        return new SolidColorBrush(Color.FromRgb(0xFF, 0x70, 0x70));               // peligro
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Null o string vacío → Collapsed, cualquier valor → Visible.</summary>
public class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null) return Visibility.Collapsed;
        if (value is string s && string.IsNullOrWhiteSpace(s)) return Visibility.Collapsed;
        return Visibility.Visible;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Convierte RiskLevel string a color del chip.</summary>
public class RiskColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string? risk = value?.ToString();
        bool isFg = parameter?.ToString() == "fg";
        return risk switch
        {
            "Seguro" or "Safe" => isFg
                ? new SolidColorBrush(Color.FromRgb(0x22, 0xD3, 0xEE))
                : new SolidColorBrush(Color.FromRgb(0x0B, 0x2A, 0x33)),
            "Moderado" or "Moderate" => isFg
                ? new SolidColorBrush(Color.FromRgb(0xF5, 0xB9, 0x42))
                : new SolidColorBrush(Color.FromRgb(0x2A, 0x21, 0x10)),
            "Avanzado" or "Advanced" => isFg
                ? new SolidColorBrush(Color.FromRgb(0xFF, 0x70, 0x70))
                : new SolidColorBrush(Color.FromRgb(0x2C, 0x14, 0x16)),
            _ => isFg
                ? new SolidColorBrush(Color.FromRgb(0x89, 0x90, 0x98))
                : new SolidColorBrush(Color.FromRgb(0x22, 0x27, 0x2E))
        };
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Compara el valor con el parámetro (texto) y devuelve bool. Para marcar la sección activa.</summary>
public class StringEqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Falso → Visible, verdadero → Collapsed (para estados vacíos).</summary>
public class InverseBoolToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (value is bool b && b) ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
