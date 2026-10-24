using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Capricon.Core.Avatar;

/// <summary>
/// The Capri character: vector shapes traced from the reference art, bound to a skeleton.
/// Every vertex carries up to three bone weights (linear blend skinning), so the flat
/// drawing bends smoothly at the joints instead of breaking into pieces, the way Live2D
/// or Spine meshes do.
/// </summary>
public sealed class CapriModel
{
    public const string Pink = "#FF2D7E";
    public const string Green = "#1DFF86";
    public const string Ink = "#0A0A12";
    public const string White = "#FFFFFF";
    public const string Red = "#FF2448";
    public const string Rim = "#8A5CFF";

    /// <summary>Hair flows around this point.</summary>
    public const double HairX = 233, HairY = 150;

    // Face landmarks of the traced head (rest pose).
    // The head is turned three-quarters: the far (left) eye is narrower and sits near the mask's edge.
    public const double EyeLX = 216.5, EyeLY = 156.5, EyeRX = 239, EyeRY = 152;
    public const double MouthX = 225.5, MouthY = 175.5, MouthTopY = 167;
    public const double FaceTilt = -8;

    /// <summary>
    /// The face area (head silhouette, mask, eyes, mouth) rides its own bone so it can be drawn
    /// bigger than in the reference: at desktop-pet size the original head is only ~45 px.
    /// </summary>
    public const double FaceX = 228, FaceY = 163, FaceRadiusX = 36, FaceRadiusY = 42;

    public static readonly IReadOnlyList<Bone> Bones =
    [
        new("anchor", null, 0, 0, 0, 0),
        new("root", null, 150, 330, 219, 195),
        new("torso", "root", 150, 330, 178, 280),
        new("chest", "torso", 178, 280, 219, 195),
        new("head", "chest", 219, 192, 232, 135),
        new("face", "head", 224, 184, 228, 140),
        new("jaw", "face", MouthX, MouthTopY, MouthX, MouthTopY + 18),
        new("armB", "chest", 238, 205, 290, 158),
        new("armB2", "armB", 290, 158, 340, 40),
        new("handB", "armB2", 340, 40, 360, 15),
        new("armF", "chest", 188, 240, 140, 268),
        new("armF2", "armF", 140, 268, 110, 275),
        new("handF", "armF2", 110, 275, 72, 265),
        new("legF", "root", 133, 330, 68, 390),
        new("legF2", "legF", 68, 390, 0, 450),
        new("legB", "root", 153, 365, 178, 420),
        new("legB2", "legB", 178, 420, 200, 480),
    ];

    public double Width { get; }
    public double Height { get; }

    /// <summary>Body layers back to front.</summary>
    public IReadOnlyList<SkinnedShape> Shapes { get; }

    /// <summary>Green mask pieces on the face, repainted when the mask changes colour.</summary>
    public IReadOnlyList<SkinnedShape> Visor { get; }

    /// <summary>The traced open grin.</summary>
    public IReadOnlyList<SkinnedShape> Mouth { get; }

    /// <summary>White spray specks on the mane.</summary>
    public SkinnedPoints Specks { get; }

    readonly Dictionary<string, int> _boneIndex;

    public int BoneIndex(string id) => _boneIndex[id];

    CapriModel(TraceData data)
    {
        Width = data.Width;
        Height = data.Height;
        _boneIndex = Bones.Select((b, i) => (b.Id, i)).ToDictionary(x => x.Id, x => x.i);

        var shapes = new List<SkinnedShape>();
        var visor = new List<SkinnedShape>();
        foreach (var layer in data.Layers)
        {
            var fill = layer.Name switch { "pink" => Pink, "green" => Green, _ => Ink };
            foreach (var rings in layer.Shapes)
            {
                var (cx, cy) = Centroid(rings[0]);
                var hair = layer.Name == "pink" && IsHair(cx, cy);
                var shape = Skin(rings, fill, hair ? BindHair : BindBody, ghost: true);
                shapes.Add(shape);
                if (layer.Name == "green" && cy < 175 && cx > 190) visor.Add(shape with { Ghost = false });
            }
        }
        Shapes = shapes;
        Visor = visor;
        // The traced "^ ^" eyes are too thin to read at pet size; the animator draws its own.
        Mouth = data.Mouth.Select(r => Skin(r, White, BindJaw, ghost: false)).ToList();

        var pts = new double[data.Specks.Length * 2];
        var radii = new double[data.Specks.Length];
        for (var i = 0; i < data.Specks.Length; i++)
            (pts[i * 2], pts[i * 2 + 1], radii[i]) = (data.Specks[i][0], data.Specks[i][1], data.Specks[i][2]);
        Specks = new SkinnedPoints(pts, radii, Bind(pts, (x, y) => IsHair(x, y) ? BindHair(x, y) : BindBody(x, y)));
    }

    public static CapriModel Load()
    {
        using var stream = typeof(CapriModel).Assembly.GetManifestResourceStream("capri_trace.json")
                           ?? throw new InvalidOperationException("capri_trace.json resource missing");
        var data = JsonSerializer.Deserialize<TraceData>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? throw new InvalidOperationException("capri_trace.json is empty");
        return new CapriModel(data);
    }

    // ---------------------------------------------------------------- binding

    /// <summary>Pink shapes up top that are not the skin panels on the torso are mane.</summary>
    static bool IsHair(double x, double y) =>
        y < 300 && SegmentDistance(x, y, Bones[3]) > 34 && SegmentDistance(x, y, Bones[2]) > 30;

    Weights BindJaw(double x, double y) => Weights.Single(_boneIndex["jaw"]);

    /// <summary>
    /// The mane follows the head and flutters, except where the pink forearm runs through it:
    /// there it follows the arm.
    /// </summary>
    Weights BindHair(double x, double y)
    {
        var armDist = Math.Min(SegmentDistance(x, y, Bones[_boneIndex["armB2"]]), SegmentDistance(x, y, Bones[_boneIndex["handB"]]));
        var arm = Math.Clamp((15 - armDist) / 10, 0, 1);
        var w = Soft(x, y, ["armB2", "handB"]).Scale(arm).Add(Weights.Single(_boneIndex["head"]).Scale(1 - arm)).Normalized();
        return Pin(x, y, w with { Flutter = 1 - arm });
    }

    static readonly string[] BodyBones =
        ["torso", "chest", "head", "armB", "armB2", "armF", "armF2", "handF", "legF", "legF2", "legB", "legB2"];

    Weights BindBody(double x, double y)
    {
        // Inside the face oval everything follows the (enlarged) face; it fades out towards the neck.
        var dx = (x - FaceX) / FaceRadiusX;
        var dy = (y - FaceY) / FaceRadiusY;
        var w = Soft(x, y, BodyBones);
        // Only what already belongs to the head: the raised arm's sleeve right next to it must not grow.
        var face = Math.Clamp((1.3 - Math.Sqrt(dx * dx + dy * dy)) / 0.35, 0, 1) * w.Share(_boneIndex["head"]);
        if (face > 0) w = w.Scale(1 - face).Add(Weights.Single(_boneIndex["face"]).Scale(face)).Normalized();
        return Pin(x, y, w);
    }

    /// <summary>Nearest bones win, with a soft blend across joints.</summary>
    Weights Soft(double x, double y, string[] candidates)
    {
        var scored = candidates
            .Select(id => (Index: _boneIndex[id], D: SegmentDistance(x, y, Bones[_boneIndex[id]])))
            .OrderBy(s => s.D)
            .Take(3)
            .ToArray();
        var min = scored[0].D;
        var w = new Weights();
        foreach (var s in scored) w = w.Add(Weights.Single(s.Index).Scale(Math.Exp(-(s.D - min) / 5)));
        return w.Normalized();
    }

    /// <summary>
    /// The reference picture is cropped by its frame (hair, raised hand, legs). Those cut edges
    /// stay glued to the window border so the crop never wanders into view while she moves.
    /// </summary>
    Weights Pin(double x, double y, Weights w)
    {
        var edge = Math.Min(Math.Min(x, y), Math.Min(Width - x, Height - y));
        var pin = Math.Pow(Math.Clamp(1 - (edge - 1.5) / 45, 0, 1), 1.5);
        if (pin <= 0) return w;
        return w.Scale(1 - pin).Add(Weights.Single(_boneIndex["anchor"]).Scale(pin)).Normalized() with { Flutter = w.Flutter * (1 - pin) };
    }

    SkinnedShape Skin(double[][] rings, string fill, Func<double, double, Weights> bind, bool ghost) =>
        new(fill, rings, rings.Select(r => Bind(r, bind)).ToArray(), ghost);

    static Weights[] Bind(double[] pts, Func<double, double, Weights> bind)
    {
        var w = new Weights[pts.Length / 2];
        for (var i = 0; i < w.Length; i++) w[i] = bind(pts[i * 2], pts[i * 2 + 1]);
        return w;
    }

    public static double SegmentDistance(double x, double y, Bone b)
    {
        var (dx, dy) = (b.TipX - b.PivotX, b.TipY - b.PivotY);
        var len2 = dx * dx + dy * dy;
        var t = len2 == 0 ? 0 : Math.Clamp(((x - b.PivotX) * dx + (y - b.PivotY) * dy) / len2, 0, 1);
        var (px, py) = (b.PivotX + dx * t, b.PivotY + dy * t);
        return Math.Sqrt((x - px) * (x - px) + (y - py) * (y - py));
    }

    static (double X, double Y) Centroid(double[] ring)
    {
        double sx = 0, sy = 0;
        for (var i = 0; i < ring.Length; i += 2) (sx, sy) = (sx + ring[i], sy + ring[i + 1]);
        var n = ring.Length / 2.0;
        return (sx / n, sy / n);
    }

    sealed record TraceData(
        double Width,
        double Height,
        TraceLayer[] Layers,
        double[][][] Eyes,
        double[][][] Mouth,
        double[][] Specks);

    sealed record TraceLayer(string Name, double[][][] Shapes);
}

/// <summary>Up to three bone influences per vertex, plus how much the hair wind moves it.</summary>
public readonly record struct Weights
{
    public int B0 { get; init; }
    public int B1 { get; init; }
    public int B2 { get; init; }
    public double W0 { get; init; }
    public double W1 { get; init; }
    public double W2 { get; init; }
    public double Flutter { get; init; }

    public static Weights Single(int bone) => new() { B0 = bone, W0 = 1 };

    public Weights Scale(double k) => this with { W0 = W0 * k, W1 = W1 * k, W2 = W2 * k };

    /// <summary>Merge influences, keeping the three strongest.</summary>
    public Weights Add(Weights o)
    {
        var all = new List<(int B, double W)> { (B0, W0), (B1, W1), (B2, W2), (o.B0, o.W0), (o.B1, o.W1), (o.B2, o.W2) }
            .Where(p => p.W > 0)
            .GroupBy(p => p.B)
            .Select(g => (B: g.Key, W: g.Sum(p => p.W)))
            .OrderByDescending(p => p.W)
            .Take(3)
            .ToList();
        while (all.Count < 3) all.Add((0, 0));
        return new Weights { B0 = all[0].B, W0 = all[0].W, B1 = all[1].B, W1 = all[1].W, B2 = all[2].B, W2 = all[2].W, Flutter = Math.Max(Flutter, o.Flutter) };
    }

    public double Share(int bone) =>
        (B0 == bone ? W0 : 0) + (B1 == bone ? W1 : 0) + (B2 == bone ? W2 : 0);

    public Weights Normalized()
    {
        var sum = W0 + W1 + W2;
        return sum <= 0 ? this : Scale(1 / sum);
    }
}

/// <summary>A filled shape (outer ring + holes) with skin weights per vertex.</summary>
public sealed record SkinnedShape(string Fill, double[][] Rings, Weights[][] Bind, bool Ghost);

public sealed record SkinnedPoints(double[] Points, double[] Radii, Weights[] Bind);
