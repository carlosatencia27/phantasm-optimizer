using System.Windows;
using System.Windows.Controls;
using PC_Optimizer.Services;

namespace PC_Optimizer.Views;

public partial class ToastHost : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty ManagerProperty = DependencyProperty.Register(
        nameof(Manager), typeof(ToastManager), typeof(ToastHost),
        new PropertyMetadata(null, (d, e) => ((ToastHost)d).List.ItemsSource = (e.NewValue as ToastManager)?.Items));

    public ToastManager? Manager { get => (ToastManager?)GetValue(ManagerProperty); set => SetValue(ManagerProperty, value); }

    public ToastHost() { InitializeComponent(); }
}
