namespace Capricon.Core.Avatar;

/// <summary>2D affine matrix [M11 M12; M21 M22; Dx Dy], same layout as WPF's Matrix and SVG's matrix().</summary>
public readonly record struct Mat(double M11, double M12, double M21, double M22, double Dx, double Dy)
{
    public static readonly Mat Identity = new(1, 0, 0, 1, 0, 0);

    public static Mat Translate(double x, double y) => new(1, 0, 0, 1, x, y);

    public static Mat Scale(double sx, double sy) => new(sx, 0, 0, sy, 0, 0);

    public static Mat Rotate(double degrees)
    {
        var (s, c) = Math.SinCos(degrees * Math.PI / 180);
        return new(c, s, -s, c, 0, 0);
    }

    /// <summary>Apply <paramref name="a"/> first, then <paramref name="b"/> (WPF/row-vector convention).</summary>
    public static Mat operator *(Mat a, Mat b) => new(
        a.M11 * b.M11 + a.M12 * b.M21,
        a.M11 * b.M12 + a.M12 * b.M22,
        a.M21 * b.M11 + a.M22 * b.M21,
        a.M21 * b.M12 + a.M22 * b.M22,
        a.Dx * b.M11 + a.Dy * b.M21 + b.Dx,
        a.Dx * b.M12 + a.Dy * b.M22 + b.Dy);

    public (double X, double Y) Apply(double x, double y) => (x * M11 + y * M21 + Dx, x * M12 + y * M22 + Dy);

    /// <summary>Rotate/scale around a pivot point, then move.</summary>
    public static Mat About(double px, double py, double degrees, double sx = 1, double sy = 1, double tx = 0, double ty = 0) =>
        Translate(-px, -py) * Scale(sx, sy) * Rotate(degrees) * Translate(px + tx, py + ty);
}

/// <summary>A joint: rotates around its pivot; the segment pivot→tip is what skin vertices bind to.</summary>
public sealed record Bone(string Id, string? Parent, double PivotX, double PivotY, double TipX, double TipY);

/// <summary>Per-bone pose for one frame.</summary>
public struct BonePose
{
    public double Angle;
    public double ScaleX;
    public double ScaleY;
    public double Tx;
    public double Ty;

    public static BonePose Rest => new() { ScaleX = 1, ScaleY = 1 };
}

/// <summary>Fill kinds understood by every renderer besides plain "#RRGGBB" colours.</summary>
public static class Fills
{
    public const string None = "none";
}

/// <summary>A renderer-neutral draw call in canvas coordinates.</summary>
public sealed record DrawItem(string Id, string Fill, double Opacity)
{
    /// <summary>Closed rings (outer + holes, even-odd) already in world coordinates.</summary>
    public double[][]? Rings { get; init; }

    /// <summary>Open polyline in world coordinates (lightning, "Z" letters).</summary>
    public double[]? Line { get; init; }

    /// <summary>Path mini-language shape, placed by <see cref="Transform"/>.</summary>
    public string? Path { get; init; }

    public Mat Transform { get; init; } = Mat.Identity;

    /// <summary>Circles as x,y,r triples in world coordinates (hair specks).</summary>
    public double[]? Circles { get; init; }

    public string? Stroke { get; init; }
    public double StrokeWidth { get; init; }

    /// <summary>Part of the silhouette: gets the purple rim and the lavender/salmon ghost copies.</summary>
    public bool Ghost { get; init; }
}

/// <summary>Horizontal band of the frame shifted sideways — the classic glitch tear.</summary>
public readonly record struct Slice(double Y0, double Y1, double Dx);

public sealed class Frame
{
    public required double Width { get; init; }
    public required double Height { get; init; }

    /// <summary>Back to front: pixel blocks behind her, the figure, then effects.</summary>
    public List<DrawItem> Items { get; } = [];

    /// <summary>Offsets of the lavender and salmon ghost copies.</summary>
    public (double X, double Y) GhostA { get; set; }
    public (double X, double Y) GhostB { get; set; }

    public List<Slice> Slices { get; } = [];

    /// <summary>
    /// Where the figure is cut by the picture frame, these teeth chew the edge into pixels so
    /// she looks like she's materialising out of the window border. Rects as x,y,w,h quads
    /// that are subtracted from the figure.
    /// </summary>
    public List<(double X, double Y, double W, double H)> EdgeBites { get; } = [];

    public string OutlineColor { get; init; } = "#8A5CFF";
    public double OutlineWidth { get; init; } = 1.6;

    public const string GhostColorA = "#B9B4FF";
    public const string GhostColorB = "#FFAAA0";
}
