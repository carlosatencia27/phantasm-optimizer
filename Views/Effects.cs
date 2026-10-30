using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using PC_Optimizer.Services;
using Brush = System.Windows.Media.Brush;
using Pen = System.Windows.Media.Pen;
using Brushes = System.Windows.Media.Brushes;

namespace PC_Optimizer.Views;

/// <summary>
/// Brillo de borde (cian a magenta) al pasar el mouse o tener el foco de teclado, para las tarjetas.
/// Un adorno ligero cuyo Opacity sube de 0 a 1 en ~150 ms: sin BlurEffect ni DropShadowEffect.
/// Con movimiento reducido el cambio es instantáneo. Se crea la primera vez que se necesita.
/// </summary>
public static class CardGlow
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(CardGlow), new PropertyMetadata(false, OnEnabledChanged));
    public static void SetEnabled(DependencyObject d, bool v) => d.SetValue(EnabledProperty, v);
    public static bool GetEnabled(DependencyObject d) => (bool)d.GetValue(EnabledProperty);

    private sealed class State { public GlowAdorner? Adorner; public bool Hover, Focus; }
    private static readonly ConditionalWeakTable<FrameworkElement, State> States = new();

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe || e.NewValue is not true) return;
        fe.MouseEnter += (_, _) => Update(fe, s => s.Hover = true);
        fe.MouseLeave += (_, _) => Update(fe, s => s.Hover = false);
        fe.Unloaded += (_, _) =>
        {
            // Al descargar la tarjeta se retira su adorno, para no acumular adornos huérfanos.
            if (States.TryGetValue(fe, out var s) && s.Adorner != null)
            {
                AdornerLayer.GetAdornerLayer(fe)?.Remove(s.Adorner);
                s.Adorner = null; s.Hover = false; s.Focus = false;
            }
        };
        fe.AddHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((_, _) => Update(fe, s => s.Focus = true)), true);
        fe.AddHandler(Keyboard.LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((_, _) => Update(fe, s => s.Focus = fe.IsKeyboardFocusWithin)), true);
    }

    private static void Update(FrameworkElement fe, Action<State> change)
    {
        var state = States.GetOrCreateValue(fe);
        change(state);
        bool on = state.Hover || state.Focus;
        if (state.Adorner == null)
        {
            if (!on) return;
            var layer = AdornerLayer.GetAdornerLayer(fe);
            if (layer == null) return;
            state.Adorner = new GlowAdorner(fe);
            layer.Add(state.Adorner);
        }
        state.Adorner.SetActive(on);
    }

    private sealed class GlowAdorner : Adorner
    {
        private static readonly Pen? GlowPen = MakePen();
        public GlowAdorner(UIElement element) : base(element) { IsHitTestVisible = false; Opacity = 0; }

        private static Pen? MakePen()
        {
            var brush = System.Windows.Application.Current?.TryFindResource("BrushBrandGradient") as Brush;
            if (brush == null) return null;
            var pen = new Pen(brush, 1.5);
            if (pen.CanFreeze) pen.Freeze();
            return pen;
        }

        public void SetActive(bool on)
        {
            if (!Motion.Enabled) { BeginAnimation(OpacityProperty, null); Opacity = on ? 1 : 0; return; }
            BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 1 : 0, TimeSpan.FromMilliseconds(150)));
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (GlowPen == null) return;
            var size = AdornedElement.RenderSize;
            if (size.Width < 4 || size.Height < 4) return;
            dc.DrawRoundedRectangle(null, GlowPen, new Rect(0.75, 0.75, size.Width - 1.5, size.Height - 1.5), 4, 4);
        }
    }
}

/// <summary>
/// Cifras que cuentan hacia arriba UNA sola vez (~600 ms) con el valor MEDIDO. El texto final es
/// siempre exactamente el original; con movimiento reducido se muestra directo.
/// </summary>
public static class CountUp
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(CountUp), new PropertyMetadata(null, OnTextChanged));
    public static void SetText(DependencyObject d, string? v) => d.SetValue(TextProperty, v);
    public static string? GetText(DependencyObject d) => (string?)d.GetValue(TextProperty);

    private static readonly DependencyProperty ProgressProperty = DependencyProperty.RegisterAttached(
        "Progress", typeof(double), typeof(CountUp), new PropertyMetadata(1.0, OnProgress));
    private static readonly ConditionalWeakTable<TextBlock, object> Played = new();
    private static readonly Regex NumberRx = new(@"^(?<pre>\D*?)(?<sign>[-+−]?)(?<int>\d+)(?<sep>[.,])?(?<dec>\d+)?(?<suf>.*)$", RegexOptions.Compiled | RegexOptions.Singleline);

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock tb) return;
        var text = e.NewValue as string;
        tb.Text = text;
        if (text == null || !Motion.Enabled || Played.TryGetValue(tb, out _) || Interpolate(text, 0.5) == text) return;
        Played.Add(tb, new object());
        var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(600)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        anim.Completed += (_, _) => { tb.BeginAnimation(ProgressProperty, null); tb.Text = GetText(tb); };
        tb.BeginAnimation(ProgressProperty, anim);
    }

    private static void OnProgress(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBlock tb && GetText(tb) is { } text) tb.Text = Interpolate(text, (double)e.NewValue);
    }

    /// <summary>Texto con su primera cifra escalada por <paramref name="progress"/> (0-1), con los mismos decimales y signo.</summary>
    public static string Interpolate(string text, double progress)
    {
        var m = NumberRx.Match(text);
        if (!m.Success) return text;
        string number = m.Groups["int"].Value + (m.Groups["sep"].Success && m.Groups["dec"].Success ? "." + m.Groups["dec"].Value : string.Empty);
        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return text;
        int decimals = m.Groups["dec"].Success ? m.Groups["dec"].Length : 0;
        string shown = (value * Math.Clamp(progress, 0, 1)).ToString("F" + decimals, CultureInfo.InvariantCulture);
        if (m.Groups["sep"].Success && m.Groups["dec"].Success) shown = shown.Replace('.', m.Groups["sep"].Value[0]);
        // Si la cifra original no cambia con el progreso (p. ej. 0), no hay nada que animar.
        return m.Groups["pre"].Value + m.Groups["sign"].Value + shown + m.Groups["suf"].Value;
    }
}

/// <summary>
/// Barra de escaneo: una franja que recorre la pista MIENTRAS IsRunning es verdadero y se detiene al
/// terminar o fallar. Con movimiento reducido muestra una barra fija en lugar de animarla.
/// </summary>
public sealed class ScanBar : FrameworkElement
{
    public static readonly DependencyProperty IsRunningProperty = DependencyProperty.Register(
        nameof(IsRunning), typeof(bool), typeof(ScanBar),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((ScanBar)d).Restart()));
    private static readonly DependencyProperty PhaseProperty = DependencyProperty.Register(
        nameof(Phase), typeof(double), typeof(ScanBar), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public bool IsRunning { get => (bool)GetValue(IsRunningProperty); set => SetValue(IsRunningProperty, value); }
    public double Phase { get => (double)GetValue(PhaseProperty); set => SetValue(PhaseProperty, value); }

    private void Restart()
    {
        BeginAnimation(PhaseProperty, null);
        if (!IsRunning || !Motion.Enabled) return;
        BeginAnimation(PhaseProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(1100)) { RepeatBehavior = RepeatBehavior.Forever });
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 4 || h <= 0) return;
        var track = System.Windows.Application.Current?.TryFindResource("BrushBorder") as Brush ?? Brushes.DimGray;
        dc.DrawRoundedRectangle(track, null, new Rect(0, 0, w, h), h / 2, h / 2);
        if (!IsRunning) return;
        var fill = System.Windows.Application.Current?.TryFindResource("BrushBrandGradient") as Brush ?? Brushes.Cyan;
        if (!Motion.Enabled) { dc.DrawRoundedRectangle(fill, null, new Rect(0, 0, w, h), h / 2, h / 2); return; }
        double barW = Math.Max(40, w * 0.28);
        double x = -barW + Phase * (w + barW);
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h)));
        dc.DrawRoundedRectangle(fill, null, new Rect(x, 0, barW, h), h / 2, h / 2);
        dc.Pop();
    }
}
