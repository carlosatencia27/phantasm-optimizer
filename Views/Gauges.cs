using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace PC_Optimizer.Views;

/// <summary>Anillo de progreso 0-100 dibujado a mano (sin efectos). Solo se repinta cuando cambia el valor.</summary>
public sealed class RingGauge : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(RingGauge),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(RingGauge),
        new FrameworkPropertyMetadata(Brushes.Cyan, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
        nameof(Track), typeof(Brush), typeof(RingGauge),
        new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty IsAvailableProperty = DependencyProperty.Register(
        nameof(IsAvailable), typeof(bool), typeof(RingGauge),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public Brush Track { get => (Brush)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }
    public bool IsAvailable { get => (bool)GetValue(IsAvailableProperty); set => SetValue(IsAvailableProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 8) return;
        const double thickness = 6;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        double r = size / 2 - thickness / 2;
        var trackPen = new Pen(Track, thickness);
        trackPen.Freeze();
        dc.DrawEllipse(null, trackPen, center, r, r);
        if (!IsAvailable) return;

        double pct = Math.Clamp(Value, 0, 100);
        if (pct <= 0.1) return;
        var pen = new Pen(Stroke, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        if (pct >= 99.9) { dc.DrawEllipse(null, pen, center, r, r); return; }
        double angle = pct / 100 * 2 * Math.PI;
        var start = new Point(center.X, center.Y - r);
        var end = new Point(center.X + r * Math.Sin(angle), center.Y - r * Math.Cos(angle));
        var fig = new PathFigure { StartPoint = start, IsClosed = false };
        fig.Segments.Add(new ArcSegment(end, new Size(r, r), 0, angle > Math.PI, SweepDirection.Clockwise, true));
        var geo = new PathGeometry(new[] { fig });
        geo.Freeze();
        dc.DrawGeometry(null, pen, geo);
    }
}

/// <summary>Mini-gráfica de las últimas muestras (escala fija 0-100). Sin animación.</summary>
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<double>), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.Cyan, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double>? Values { get => (IReadOnlyList<double>?)GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        var v = Values;
        if (v == null || v.Count < 2 || ActualWidth < 4 || ActualHeight < 4) return;
        double w = ActualWidth, h = ActualHeight, step = w / 59.0;
        double x0 = w - (v.Count - 1) * step; // las muestras nuevas entran por la derecha
        var fig = new PathFigure { IsClosed = false };
        for (int i = 0; i < v.Count; i++)
        {
            double y = h - Math.Clamp(v[i], 0, 100) / 100 * (h - 2) - 1;
            var p = new Point(x0 + i * step, y);
            if (i == 0) fig.StartPoint = p; else fig.Segments.Add(new LineSegment(p, true));
        }
        var geo = new PathGeometry(new[] { fig });
        geo.Freeze();
        var pen = new Pen(Stroke, 1.5) { LineJoin = PenLineJoin.Round };
        pen.Freeze();
        dc.DrawGeometry(null, pen, geo);
    }
}
