using System.Windows;
using System.Windows.Media;
using Capricon.Core.Avatar;

namespace Capricon;

/// <summary>
/// Draws <see cref="CapriAnimator"/> frames with WPF. Mirrors Core's SvgRenderer: lavender and
/// salmon ghost copies, a purple rim, pixel-bitten frame edges and glitch slices.
/// </summary>
public sealed class AvatarView : FrameworkElement
{
    readonly Dictionary<string, Brush> _brushes = [];
    readonly Dictionary<(string, double), Pen> _pens = [];
    readonly Dictionary<string, Geometry> _paths = [];
    TimeSpan _last = TimeSpan.MinValue;
    Frame? _frame;

    public AvatarView()
    {
        IsHitTestVisible = true;
        CompositionTarget.Rendering += OnRendering;
        Unloaded += (_, _) => CompositionTarget.Rendering -= OnRendering;
    }

    public CapriAnimator Animator { get; } = new();

    /// <summary>Called every frame before drawing so the owner can feed voice level, cursor, etc.</summary>
    public event Action? BeforeFrame;

    public double Scale { get; set; } = 1;

    protected override Size MeasureOverride(Size availableSize) => new(Animator.Width * Scale, Animator.Height * Scale);

    void OnRendering(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        // Rendering fires at the monitor rate (and sometimes twice per frame); ~40 fps is
        // plenty for this art style and keeps the transparent window cheap.
        if (_last != TimeSpan.MinValue && (now - _last).TotalSeconds < 1 / 42.0) return;
        var dt = _last == TimeSpan.MinValue ? 0 : (now - _last).TotalSeconds;
        _last = now;
        BeforeFrame?.Invoke();
        _frame = Animator.Update(dt);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_frame is not { } f) return;
        dc.PushTransform(new ScaleTransform(Scale, Scale));

        var frameClip = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, f.Width, f.Height)), Bites(f));
        dc.PushClip(frameClip);
        if (f.Slices.Count == 0)
        {
            DrawFigure(dc, f);
        }
        else
        {
            var wide = new Rect(-60, -60, f.Width + 120, f.Height + 120);
            var tears = new GeometryGroup();
            foreach (var s in f.Slices) tears.Children.Add(new RectangleGeometry(new Rect(-60, s.Y0, f.Width + 120, s.Y1 - s.Y0)));
            dc.PushClip(new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(wide), tears));
            DrawFigure(dc, f);
            dc.Pop();
            foreach (var s in f.Slices)
            {
                dc.PushClip(new RectangleGeometry(new Rect(-60, s.Y0, f.Width + 120, s.Y1 - s.Y0)));
                dc.PushTransform(new TranslateTransform(s.Dx, 0));
                DrawFigure(dc, f);
                dc.Pop();
                dc.Pop();
            }
        }
        dc.Pop();
        dc.Pop();
    }

    static Geometry Bites(Frame f)
    {
        var g = new GeometryGroup();
        foreach (var b in f.EdgeBites) g.Children.Add(new RectangleGeometry(new Rect(b.X, b.Y, b.W, b.H)));
        return g;
    }

    void DrawFigure(DrawingContext dc, Frame f)
    {
        var silhouette = new List<Geometry>();
        foreach (var item in f.Items)
            if (item.Ghost && item.Rings is { } rings)
                silhouette.Add(RingsGeometry(rings));

        foreach (var (offset, color) in new[] { (f.GhostA, Frame.GhostColorA), (f.GhostB, Frame.GhostColorB) })
        {
            dc.PushTransform(new TranslateTransform(offset.X, offset.Y));
            foreach (var g in silhouette) dc.DrawGeometry(Brush(color), null, g);
            dc.Pop();
        }
        var rim = Pen(f.OutlineColor, f.OutlineWidth * 2);
        foreach (var g in silhouette) dc.DrawGeometry(null, rim, g);

        foreach (var item in f.Items) DrawItem(dc, item);
    }

    void DrawItem(DrawingContext dc, DrawItem item)
    {
        var fill = item.Fill == Fills.None ? null : Brush(item.Fill);
        var pen = item.Stroke is null || item.StrokeWidth <= 0 ? null : Pen(item.Stroke, item.StrokeWidth);
        if (item.Opacity < 1) dc.PushOpacity(item.Opacity);

        if (item.Rings is { } rings)
        {
            dc.DrawGeometry(fill, pen, RingsGeometry(rings));
        }
        else if (item.Line is { } line)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(line[0], line[1]), false, false);
                for (var i = 2; i < line.Length; i += 2) ctx.LineTo(new Point(line[i], line[i + 1]), true, true);
            }
            g.Freeze();
            dc.DrawGeometry(null, pen, g);
        }
        else if (item.Circles is { } c)
        {
            for (var i = 0; i < c.Length; i += 3) dc.DrawEllipse(fill, null, new Point(c[i], c[i + 1]), c[i + 2], c[i + 2]);
        }
        else if (item.Path is { } path)
        {
            var m = item.Transform;
            dc.PushTransform(new MatrixTransform(m.M11, m.M12, m.M21, m.M22, m.Dx, m.Dy));
            dc.DrawGeometry(fill, pen, PathGeometry(path));
            dc.Pop();
        }

        if (item.Opacity < 1) dc.Pop();
    }

    static Geometry RingsGeometry(double[][] rings)
    {
        var g = new StreamGeometry { FillRule = FillRule.EvenOdd };
        using (var ctx = g.Open())
        {
            foreach (var r in rings)
            {
                if (r.Length < 6) continue;
                ctx.BeginFigure(new Point(r[0], r[1]), true, true);
                var pts = new Point[r.Length / 2 - 1];
                for (var i = 2; i < r.Length; i += 2) pts[i / 2 - 1] = new Point(r[i], r[i + 1]);
                ctx.PolyLineTo(pts, true, true);
            }
        }
        g.Freeze();
        return g;
    }

    Geometry PathGeometry(string path)
    {
        if (!_paths.TryGetValue(path, out var g))
        {
            g = Geometry.Parse(path);
            g.Freeze();
            _paths[path] = g;
        }
        return g;
    }

    Brush Brush(string color)
    {
        if (!_brushes.TryGetValue(color, out var b))
        {
            b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            b.Freeze();
            _brushes[color] = b;
        }
        return b;
    }

    Pen Pen(string color, double width)
    {
        if (!_pens.TryGetValue((color, width), out var p))
        {
            p = new Pen(Brush(color), width) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            p.Freeze();
            _pens[(color, width)] = p;
        }
        return p;
    }
}
