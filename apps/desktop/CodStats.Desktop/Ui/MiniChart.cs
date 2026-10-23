using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace CodStats.Desktop.Ui;

/// <summary>A small line or bar chart drawn directly; enough for a trend, without a charting library.</summary>
public sealed class MiniChart : FrameworkElement
{
    private IReadOnlyList<double?> values = [];

    public bool Bars { get; set; }

    public Brush Stroke { get; set; } = new SolidColorBrush(Color.FromRgb(0xE8, 0xB9, 0x3A));

    public IReadOnlyList<double?> Values
    {
        get => values;
        set
        {
            values = value;
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var width = ActualWidth;
        var height = ActualHeight;
        var grid = new Pen(new SolidColorBrush(Color.FromRgb(0x34, 0x39, 0x44)), 1);
        drawingContext.DrawLine(grid, new Point(0, height - 0.5), new Point(width, height - 0.5));

        var known = values.Where(v => v is not null).Select(v => v!.Value).ToList();
        if (known.Count == 0 || width <= 0 || height <= 20)
        {
            return;
        }
        var max = Math.Max(known.Max(), Bars ? 1 : 0.5);
        var top = 14.0;
        var usable = height - top - 2;
        double X(int i) => values.Count == 1 ? width / 2 : i * (width - 1) / (values.Count - 1);
        double Y(double v) => top + usable - v / max * usable;

        var label = new FormattedText(max.ToString(Bars ? "0" : "0.00", CultureInfo.CurrentCulture), CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, new SolidColorBrush(Color.FromRgb(0x9A, 0xA1, 0xAE)),
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        drawingContext.DrawText(label, new Point(0, 0));
        drawingContext.DrawLine(grid, new Point(0, top), new Point(width, top));

        if (Bars)
        {
            var slot = width / values.Count;
            var bar = Math.Max(1, slot * 0.7);
            for (var i = 0; i < values.Count; i++)
            {
                if (values[i] is { } v)
                {
                    drawingContext.DrawRoundedRectangle(Stroke, null, new Rect(i * slot + (slot - bar) / 2, Y(v), bar, top + usable - Y(v)), 2, 2);
                }
            }
            return;
        }

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            var started = false;
            for (var i = 0; i < values.Count; i++)
            {
                if (values[i] is not { } v)
                {
                    continue;
                }
                var point = new Point(X(i), Y(v));
                if (!started)
                {
                    context.BeginFigure(point, false, false);
                    started = true;
                }
                else
                {
                    context.LineTo(point, true, true);
                }
            }
        }
        geometry.Freeze();
        drawingContext.DrawGeometry(null, new Pen(Stroke, 2) { LineJoin = PenLineJoin.Round }, geometry);
    }
}
