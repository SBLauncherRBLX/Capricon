using System.Globalization;

namespace Capricon.Core.Avatar;

/// <summary>
/// Animates <see cref="CapriModel"/>: skeleton poses per mood, breathing, blinking, lip-sync,
/// a mane that flutters in the "wind", chromatic ghosts, glitch tears and mood effects.
/// Pure math: the Windows app draws the frames with WPF, the preview tool as SVG.
/// </summary>
public sealed class CapriAnimator
{
    static readonly CapriModel SharedModel = CapriModel.Load();

    readonly CapriModel _model;
    readonly IReadOnlyList<Bone> _bones;
    readonly BonePose[] _pose;
    readonly Mat[] _world;
    readonly Random _rng;
    readonly List<(char Side, double From, double To)> _cuts;

    double _time;
    double _talk;
    double _nextBlink = 2;
    double _blinkUntil;
    double _glitchUntil;
    double _glitchStrength;
    double _nextMicroGlitch = 5;
    double _nextBolt;
    double _nextBites;
    double _wind = 1;
    double _swing;
    (double X, double Y) _ghostA = (5, 4);
    (double X, double Y) _ghostB = (-4, 2);
    readonly List<Slice> _slices = [];
    readonly List<double[]> _bolts = [];
    readonly List<(double X, double Y, double W, double H)> _bites = [];
    readonly List<(double X, double Y, double S, string Color)> _crumbs = [];

    public CapriAnimator(CapriModel? model = null, int seed = 7)
    {
        _model = model ?? SharedModel;
        _bones = CapriModel.Bones;
        _pose = new BonePose[_bones.Count];
        _world = new Mat[_bones.Count];
        _rng = new Random(seed);
        _cuts = FindCuts();
    }

    public double Width => _model.Width;
    public double Height => _model.Height;

    public Mood Mood { get; set; } = Mood.Idle;

    /// <summary>Voice loudness 0..1 (from TTS visemes). Smoothed internally.</summary>
    public double TalkLevel { get; set; }

    /// <summary>True while she is being dragged: the mane streams and she swings.</summary>
    public bool Dragging { get; set; }

    /// <summary>Horizontal drag velocity in px/s, drives the swing.</summary>
    public double DragVelocity { get; set; }

    /// <summary>Waving the raised hand (greetings, getting attention).</summary>
    public bool Waving { get; set; }

    /// <summary>Head and eyes lean towards this point (canvas coords), e.g. the mouse cursor.</summary>
    public (double X, double Y)? LookAt { get; set; }

    /// <summary>Kick off a full-figure glitch (screen hack, poke, mood change).</summary>
    public void Glitch(double seconds = 0.35, double strength = 1)
    {
        if (_time >= _glitchUntil) _glitchStrength = 0;
        _glitchUntil = Math.Max(_glitchUntil, _time + seconds);
        _glitchStrength = Math.Max(_glitchStrength, strength);
    }

    public Frame Update(double dt)
    {
        dt = Math.Clamp(dt, 0, 0.1);
        _time += dt;
        var t = _time;

        _talk += (TalkLevel - _talk) * Math.Min(1, dt * 18);
        var targetWind = Dragging ? 2.6 : Mood is Mood.Hack or Mood.Laugh or Mood.Angry ? 1.7 : Mood == Mood.Sleepy ? 0.5 : 1;
        _wind += (targetWind - _wind) * Math.Min(1, dt * 3);
        var swing = Dragging ? Math.Clamp(-DragVelocity / 80, -8, 8) : 0;
        _swing += (swing - _swing) * Math.Min(1, dt * 6);

        UpdateBlink(t);
        UpdateGlitch(t);
        UpdateBites(t);
        PoseBones(t);
        for (var i = 0; i < _bones.Count; i++)
        {
            var b = _bones[i];
            var p = _pose[i];
            var local = Mat.About(b.PivotX, b.PivotY, p.Angle, p.ScaleX, p.ScaleY, p.Tx, p.Ty);
            _world[i] = b.Parent is null ? local : local * _world[_model.BoneIndex(b.Parent)];
        }

        var frame = new Frame { Width = _model.Width, Height = _model.Height, GhostA = _ghostA, GhostB = _ghostB };
        frame.Slices.AddRange(_slices);
        frame.EdgeBites.AddRange(_bites);

        AddPixelBlocks(frame, t, front: false);
        AddFigure(frame, t);
        AddEffects(frame, t);
        AddPixelBlocks(frame, t, front: true);
        return frame;
    }

    // ------------------------------------------------------------------ pose

    void PoseBones(double t)
    {
        for (var i = 0; i < _pose.Length; i++) _pose[i] = BonePose.Rest;

        var energy = Mood switch
        {
            Mood.Laugh or Mood.Hack => 1.5,
            Mood.Happy or Mood.Love or Mood.Surprised => 1.2,
            Mood.Sleepy or Mood.Sad => 0.5,
            _ => 1,
        };

        Set("root", p =>
        {
            p.Ty = Math.Sin(t * 1.3) * 4 * energy + (Mood == Mood.Sleepy ? 4 : 0);
            p.Tx = Math.Sin(t * 0.6) * 2;
            p.Angle = Math.Sin(t * 0.7) * 1.2 + _swing + (Mood == Mood.Sleepy ? -2 : 0);
            if (Mood == Mood.Laugh) p.Ty -= Math.Abs(Math.Sin(t * 11)) * 3;
            return p;
        });

        Set("torso", p =>
        {
            p.Angle = Mood switch { Mood.Sad => 2.5, Mood.Surprised => -2.5, Mood.Angry => -1.5, _ => 0 } + Math.Sin(t * 0.9) * 0.6;
            return p;
        });

        Set("chest", p =>
        {
            var breath = Math.Sin(t * (Mood == Mood.Sleepy ? 1.2 : 2.3));
            p.ScaleX = 1 + breath * 0.01;
            p.ScaleY = 1 + breath * 0.022;
            p.Angle = Math.Sin(t * 1.1) * 0.8;
            return p;
        });

        Set("head", p =>
        {
            p.Angle = Math.Sin(t * 0.9) * 2.5
                      + Math.Sin(t * 9.5) * 2.5 * _talk
                      + Mood switch
                      {
                          Mood.Think => -7 + Math.Sin(t * 0.8) * 1.5,
                          Mood.Sad => 8,
                          Mood.Smug => -5,
                          Mood.Love => 5 + Math.Sin(t * 2) * 2.5,
                          Mood.Sleepy => 10 + Math.Sin(t * 0.9) * 1.5,
                          Mood.Laugh => -6 + Math.Sin(t * 14) * 3,
                          Mood.Angry => Math.Sin(t * 30),
                          Mood.Surprised => -4,
                          _ => 0,
                      };
            if (LookAt is { } look) p.Angle += Math.Clamp((look.X - CapriModel.HairX) / 50, -5, 5);
            return p;
        });

        Set("jaw", p =>
        {
            p.ScaleY = Mood == Mood.Laugh ? 0.85 + Math.Abs(Math.Sin(t * 13)) * 0.35 : 0.35 + _talk * 0.8;
            if (_talk < 0.02 && Mood != Mood.Laugh) p.ScaleY = 1;
            return p;
        });

        Set("armB", p =>
        {
            p.Angle = Math.Sin(t * 1.1) * 2 + Mood switch { Mood.Hack => 5, Mood.Sad or Mood.Sleepy => 7, Mood.Surprised => -4, _ => 0 };
            return p;
        });
        Set("armB2", p =>
        {
            p.Angle = Waving || Mood is Mood.Happy or Mood.Love
                ? Math.Sin(t * 7) * 6
                : Math.Sin(t * 1.4 + 1) * 2.5;
            return p;
        });
        Set("handB", p => { p.Angle = Math.Sin(t * 2.2) * 4 + (Waving ? Math.Sin(t * 7 + 1) * 8 : 0); return p; });

        Set("armF", p =>
        {
            p.Angle = Math.Sin(t * 1.3 + 1) * 3 + Mood switch
            {
                Mood.Hack => -8 + Math.Sin(t * 22) * 1.5,
                Mood.Think => 12,
                Mood.Angry => -6,
                Mood.Sleepy or Mood.Sad => 9,
                _ => 0,
            };
            return p;
        });
        Set("armF2", p => { p.Angle = Math.Sin(t * 1.7) * 4 + (Mood == Mood.Laugh ? Math.Sin(t * 12) * 6 : 0); return p; });
        Set("handF", p => { p.Angle = Math.Sin(t * 3.1) * 6 + (Mood == Mood.Hack ? Math.Sin(t * 40) * 6 : 0); return p; });

        Set("legF", p => { p.Angle = Math.Sin(t * 1.0) * 2.5 * energy + _swing * 0.4; return p; });
        Set("legF2", p => { p.Angle = Math.Sin(t * 1.0 - 0.8) * 3 * energy; return p; });
        Set("legB", p => { p.Angle = Math.Sin(t * 1.2 + 2) * 3 * energy + _swing * 0.3; return p; });
        Set("legB2", p => { p.Angle = Math.Sin(t * 1.2 + 1.2) * 4 * energy; return p; });
    }

    void Set(string bone, Func<BonePose, BonePose> f)
    {
        var i = _model.BoneIndex(bone);
        _pose[i] = f(_pose[i]);
    }

    // ------------------------------------------------------------------ skinning

    (double X, double Y) Skin(double x, double y, in Weights w, double t)
    {
        if (w.Flutter > 0)
        {
            // Travelling wave along the mane, stronger towards the tips.
            var dx = x - CapriModel.HairX;
            var dy = y - CapriModel.HairY;
            var d = Math.Sqrt(dx * dx + dy * dy);
            if (d > 1)
            {
                var k = Math.Pow(Math.Min(1, d / 170), 1.3) * w.Flutter;
                var a = Math.Atan2(dy, dx);
                var s = Math.Sin(t * (1.7 + _wind * 0.4) - d / 95 * Math.PI * 2 + a * 2.3);
                var amp = 5.5 * _wind * k;
                var radial = 1 + Math.Sin(t * 1.3 + a * 3) * 0.02 * _wind * k;
                x = CapriModel.HairX + dx * radial - dy / d * s * amp;
                y = CapriModel.HairY + dy * radial + dx / d * s * amp;
            }
        }

        double rx = 0, ry = 0;
        Accumulate(ref rx, ref ry, x, y, w.B0, w.W0);
        Accumulate(ref rx, ref ry, x, y, w.B1, w.W1);
        Accumulate(ref rx, ref ry, x, y, w.B2, w.W2);
        return (rx, ry);
    }

    void Accumulate(ref double rx, ref double ry, double x, double y, int bone, double weight)
    {
        if (weight <= 0) return;
        var (px, py) = _world[bone].Apply(x, y);
        rx += px * weight;
        ry += py * weight;
    }

    double[][] SkinRings(SkinnedShape s, double t)
    {
        var rings = new double[s.Rings.Length][];
        for (var r = 0; r < s.Rings.Length; r++)
        {
            var src = s.Rings[r];
            var dst = new double[src.Length];
            for (var i = 0; i < src.Length; i += 2)
                (dst[i], dst[i + 1]) = Skin(src[i], src[i + 1], s.Bind[r][i / 2], t);
            rings[r] = dst;
        }
        return rings;
    }

    void AddFigure(Frame frame, double t)
    {
        var i = 0;
        foreach (var shape in _model.Shapes)
            frame.Items.Add(new DrawItem($"body{i++}", shape.Fill, 1) { Rings = SkinRings(shape, t), Ghost = true });

        var specks = _model.Specks;
        var circles = new double[specks.Radii.Length * 3];
        for (var k = 0; k < specks.Radii.Length; k++)
        {
            var (x, y) = Skin(specks.Points[k * 2], specks.Points[k * 2 + 1], specks.Bind[k], t);
            (circles[k * 3], circles[k * 3 + 1], circles[k * 3 + 2]) = (x, y, specks.Radii[k]);
        }
        frame.Items.Add(new DrawItem("specks", CapriModel.White, 0.9) { Circles = circles });

        AddFace(frame, t);
    }

    // ------------------------------------------------------------------ face

    void AddFace(Frame frame, double t)
    {
        var (eyes, mouth) = Expression();
        var head = _world[_model.BoneIndex("head")];
        var visorColor = Mood switch { Mood.Hack => CapriModel.Red, Mood.Love => "#FF7AD9", _ => CapriModel.Green };
        var traced = eyes == "happy" && visorColor == CapriModel.Green;

        var d = 0;
        foreach (var detail in _model.FaceDetails)
            frame.Items.Add(new DrawItem($"facedetail{d++}", CapriModel.Ink, 1) { Rings = SkinRings(detail, t) });

        if (visorColor != CapriModel.Green)
        {
            var v = 0;
            foreach (var piece in _model.Visor)
                frame.Items.Add(new DrawItem($"visor{v++}", visorColor, 1) { Rings = SkinRings(piece, t) });
        }

        if (traced)
        {
            var e = 0;
            foreach (var eye in _model.Eyes)
                frame.Items.Add(new DrawItem($"eye{e++}", CapriModel.Ink, 1) { Rings = SkinRings(eye, t) });
        }
        else
        {
            var (path, fill, stroke) = EyeShape(eyes);
            foreach (var (cx, cy, right) in new[] { (CapriModel.EyeLX, CapriModel.EyeLY, false), (CapriModel.EyeRX, CapriModel.EyeRY, true) })
            {
                var look = LookAt is { } la ? (Math.Clamp((la.X - cx) / 80, -1.5, 1.5), Math.Clamp((la.Y - cy) / 100, -1, 1)) : (0.0, 0.0);
                if (Mood == Mood.Think) look = (1.5, -1.5);
                // Shapes are drawn for the right eye (outer corner +x); the far eye is mirrored and foreshortened.
                var local = (right ? Mat.Identity : Mat.Scale(-0.72, 0.95))
                            * Mat.Rotate(CapriModel.FaceTilt)
                            * Mat.Translate(cx + look.Item1, cy + look.Item2)
                            * head;
                frame.Items.Add(new DrawItem($"eyes.{eyes}.{(right ? "r" : "l")}", fill, 1)
                {
                    Path = path,
                    Transform = local,
                    Stroke = stroke,
                    StrokeWidth = stroke is null ? 0 : fill == Fills.None ? 2 : 1.2,
                });
                if (eyes is "open" or "round")
                    frame.Items.Add(new DrawItem($"glint.{(right ? "r" : "l")}", CapriModel.White, 1)
                    {
                        Path = "M 0.6,-3.2 L 3,-3.2 L 3,-0.8 L 0.6,-0.8 Z",
                        Transform = local,
                    });
            }
        }

        if (mouth is "grin" or "talk")
        {
            var m = 0;
            foreach (var shape in _model.Mouth)
                frame.Items.Add(new DrawItem($"mouth{m++}", CapriModel.White, 1) { Rings = SkinRings(shape, t) });
        }
        else
        {
            var (path, fill, stroke) = MouthShape(mouth);
            frame.Items.Add(new DrawItem($"mouth.{mouth}", fill, 1)
            {
                Path = path,
                Transform = Mat.Rotate(CapriModel.FaceTilt) * Mat.Translate(CapriModel.MouthX, CapriModel.MouthY) * head,
                Stroke = stroke,
                StrokeWidth = stroke is null ? 0 : mouth == "teeth" ? 1 : 2.3,
            });
        }
    }

    (string Eyes, string Mouth) Expression()
    {
        var (eyes, mouth) = Mood switch
        {
            Mood.Happy or Mood.Laugh => ("happy", "grin"),
            Mood.Smug => ("smug", "smirk"),
            Mood.Think => ("open", "flat"),
            Mood.Hack => ("slit", "grin"),
            Mood.Surprised => ("round", "o"),
            Mood.Sad => ("sad", "frown"),
            Mood.Angry => ("angry", "teeth"),
            Mood.Sleepy => ("closed", "small"),
            Mood.Love => ("heart", "grin"),
            _ => ("open", "grin"),
        };
        if (_time < _blinkUntil && eyes is "open" or "smug" or "round" or "slit") eyes = "closed";
        if (_talk > 0.04 && mouth is not ("grin" or "o" or "teeth")) mouth = "talk";
        return (eyes, mouth);
    }

    /// <summary>Eye drawings for the near (right) eye, outer corner towards +x, lashes included.</summary>
    static (string Path, string Fill, string? Stroke) EyeShape(string eyes) => eyes switch
    {
        "open" => ("M -7,1 Q 0,-6.5 7,-1 Q 0,5 -7,1 Z M 6,-1.5 L 10,-4.5 M 3.5,-4 L 5.5,-7.5", CapriModel.Ink, CapriModel.Ink),
        "closed" => ("M -7,-0.5 Q 0,4.5 7,-0.5 M 6.2,0.4 L 9.8,-2.2", Fills.None, CapriModel.Ink),
        "smug" => ("M -7,-1.5 L 7.5,-2.6 Q 0,4.8 -7,-1.5 Z M 7,-2.6 L 10.5,-5.2", CapriModel.Ink, CapriModel.Ink),
        "round" => ("M -5.2,0 A 5.2,5.2 0 1 1 5.2,0 A 5.2,5.2 0 1 1 -5.2,0 Z M 4.6,-2.6 L 8.4,-5.4", CapriModel.Ink, CapriModel.Ink),
        "sad" => ("M -7,-5.2 L 6.5,-1.6 M -6,2 Q 0,5.4 6.5,1.4", Fills.None, CapriModel.Ink),
        "angry" => ("M -7.5,-0.8 L 7.5,-6.4 M -5.5,2.8 L 6.5,1.2", Fills.None, CapriModel.Ink),
        "slit" => ("M -8,0.6 L 8.5,-2.6 L 7.5,1.8 L -7,2.8 Z", CapriModel.White, null),
        "heart" => ("M 0,5.6 L -6.6,-0.6 A 3.3,3.3 0 0 1 0,-3.8 A 3.3,3.3 0 0 1 6.6,-0.6 Z", CapriModel.Red, null),
        _ => ("M -7,2 L 0,-4 L 7,1.5 M 6.2,1 L 9.6,-2", Fills.None, CapriModel.Ink),
    };

    /// <summary>Mouth drawings in a local frame centred on the traced grin.</summary>
    static (string Path, string Fill, string? Stroke) MouthShape(string mouth) => mouth switch
    {
        "smirk" => ("M -8,2 Q 2,5 9,-5", Fills.None, CapriModel.White),
        "flat" => ("M -7,1 L 7,-1", Fills.None, CapriModel.White),
        "o" => ("M -4,0 A 4,5.5 0 1 1 4,0 A 4,5.5 0 1 1 -4,0 Z", CapriModel.White, null),
        "frown" => ("M -7,4 Q 0,-3 7,3", Fills.None, CapriModel.White),
        "teeth" => ("M -9,-4 L 9,-6 L 8.5,4 L -8.5,6 Z M -4.5,-4.5 L -4.2,5.5 M 0,-5 L 0.2,5 M 4.5,-5.5 L 4.6,4.5", CapriModel.White, CapriModel.Ink),
        "small" => ("M -3,0.5 Q 0,3 3,0", Fills.None, CapriModel.White),
        _ => ("M -8,-2 Q 0,6 8,-3", Fills.None, CapriModel.White),
    };

    void UpdateBlink(double t)
    {
        if (t < _nextBlink) return;
        _blinkUntil = t + 0.13;
        _nextBlink = t + 2.2 + _rng.NextDouble() * 4 + (_rng.NextDouble() < 0.2 ? -1.9 : 0);
    }

    // ------------------------------------------------------------------ glitch

    void UpdateGlitch(double t)
    {
        if (t >= _nextMicroGlitch)
        {
            var hacky = Mood == Mood.Hack;
            Glitch(hacky ? 0.25 : 0.12, hacky ? 1 : 0.45);
            _nextMicroGlitch = t + (hacky ? 0.6 + _rng.NextDouble() * 1.2 : 5 + _rng.NextDouble() * 9);
        }

        _slices.Clear();
        if (t < _glitchUntil && _rng.NextDouble() < 0.85)
        {
            var count = 2 + _rng.Next(4);
            for (var i = 0; i < count; i++)
            {
                var y0 = _rng.NextDouble() * _model.Height;
                var h = 5 + _rng.NextDouble() * 30;
                _slices.Add(new Slice(y0, Math.Min(_model.Height, y0 + h), (_rng.NextDouble() * 2 - 1) * 22 * _glitchStrength));
            }
            _ghostA = (5 + (_rng.NextDouble() * 2 - 1) * 10 * _glitchStrength, 4 + (_rng.NextDouble() * 2 - 1) * 4);
            _ghostB = (-4 + (_rng.NextDouble() * 2 - 1) * 10 * _glitchStrength, 2 + (_rng.NextDouble() * 2 - 1) * 4);
        }
        else
        {
            _ghostA = (Lerp(_ghostA.X, 5 + Math.Sin(t * 0.8) * 1.2, 0.15), Lerp(_ghostA.Y, 4, 0.15));
            _ghostB = (Lerp(_ghostB.X, -4 + Math.Sin(t * 0.7 + 2) * 1.2, 0.15), Lerp(_ghostB.Y, 2, 0.15));
        }
    }

    static double Lerp(double a, double b, double k) => a + (b - a) * k;

    /// <summary>Where the traced figure touches the picture frame (side, from, to).</summary>
    List<(char Side, double From, double To)> FindCuts()
    {
        var hits = new Dictionary<char, List<double>> { ['T'] = [], ['B'] = [], ['L'] = [], ['R'] = [] };
        foreach (var shape in _model.Shapes)
        foreach (var ring in shape.Rings)
            for (var i = 0; i < ring.Length; i += 2)
            {
                var (x, y) = (ring[i], ring[i + 1]);
                if (y < 1.5) hits['T'].Add(x);
                if (y > _model.Height - 1.5) hits['B'].Add(x);
                if (x < 1.5) hits['L'].Add(y);
                if (x > _model.Width - 1.5) hits['R'].Add(y);
            }

        var cuts = new List<(char, double, double)>();
        foreach (var (side, list) in hits)
        {
            list.Sort();
            for (var i = 0; i < list.Count;)
            {
                var j = i;
                while (j + 1 < list.Count && list[j + 1] - list[j] < 40) j++;
                if (list[j] - list[i] > 3) cuts.Add((side, list[i], list[j]));
                i = j + 1;
            }
        }
        return cuts;
    }

    void UpdateBites(double t)
    {
        if (t < _nextBites) return;
        _nextBites = t + 0.18 + _rng.NextDouble() * 0.25;
        _bites.Clear();
        _crumbs.Clear();
        const double step = 6;
        foreach (var (side, from, to) in _cuts)
        {
            var color = side switch { 'T' or 'R' => CapriModel.Pink, 'L' => CapriModel.Ink, _ => from < 120 ? CapriModel.Ink : CapriModel.Pink };
            for (var s = from - step; s < to + step; s += step)
            {
                var depth = _rng.NextDouble() < 0.3 ? 0 : step * _rng.Next(1, 4);
                if (depth > 0)
                    _bites.Add(side switch
                    {
                        'T' => (s, 0, step, depth),
                        'B' => (s, _model.Height - depth, step, depth),
                        'L' => (0, s, depth, step),
                        _ => (_model.Width - depth, s, depth, step),
                    });
                if (_rng.NextDouble() < 0.18)
                {
                    var off = depth + step * _rng.Next(1, 3);
                    _crumbs.Add(side switch
                    {
                        'T' => (s, off, step - 1, color),
                        'B' => (s, _model.Height - off - step, step - 1, color),
                        'L' => (off, s, step - 1, color),
                        _ => (_model.Width - off - step, s, step - 1, color),
                    });
                }
            }
        }
    }

    // ------------------------------------------------------------------ effects

    // Staircase clusters of pastel pixels around her, like in the reference art.
    static readonly (double X, double Y, double W, double H, string Color, bool Front, int Cluster)[] Blocks =
    [
        (0, 2, 64, 16, "#FFF84A", false, 0), (64, 2, 52, 8, "#A8FFA0", false, 0), (12, 18, 28, 14, "#FFAAA0", false, 0), (44, 18, 62, 12, "#FFAAA0", false, 0),
        (0, 114, 10, 14, "#B2AEFF", false, 1), (10, 128, 14, 14, "#A8FFA0", false, 1), (0, 134, 22, 12, "#FFF84A", false, 1), (12, 142, 22, 16, "#FFAAA0", false, 1),
        (0, 226, 32, 14, "#B2AEFF", true, 2), (12, 240, 26, 14, "#5CFFFF", true, 2), (28, 240, 14, 64, "#FFF84A", true, 2), (38, 254, 16, 70, "#FFAAA0", true, 2),
        (0, 266, 10, 30, "#B2AEFF", true, 2), (12, 280, 14, 22, "#5CFFFF", true, 2),
        (32, 416, 46, 12, "#B2AEFF", true, 3), (44, 428, 70, 14, "#5CFFFF", true, 3), (56, 442, 70, 10, "#FFF84A", true, 3), (52, 452, 12, 14, "#A8FFA0", true, 3), (110, 438, 22, 12, "#FFAAA0", true, 3),
        (256, 318, 4, 30, "#B2AEFF", false, 4), (258, 348, 38, 22, "#B2AEFF", false, 4), (282, 344, 10, 26, "#A8FFA0", false, 4), (290, 356, 14, 28, "#FF4FE8", false, 4),
        (298, 360, 60, 18, "#B2AEFF", false, 4), (330, 370, 36, 16, "#5CFFFF", false, 4), (230, 382, 30, 20, "#B2AEFF", false, 4), (246, 396, 54, 18, "#5CFFFF", false, 4),
        (250, 430, 96, 14, "#FFF84A", false, 4), (258, 444, 96, 12, "#FFAAA0", false, 4), (330, 446, 14, 10, "#FF4FE8", false, 4), (348, 452, 14, 16, "#5CFFFF", false, 4),
    ];

    void AddPixelBlocks(Frame frame, double t, bool front)
    {
        var glitching = t < _glitchUntil;
        for (var i = 0; i < Blocks.Length; i++)
        {
            var b = Blocks[i];
            if (b.Front != front) continue;
            var drift = Math.Sin(t * 0.5 + b.Cluster * 1.7) * 3;
            var jitter = glitching ? (_rng.NextDouble() * 2 - 1) * 9 : 0;
            if (Math.Sin(t * 0.9 + i * 2.39) > 0.97 && !glitching) continue;
            var x = b.X + drift + jitter;
            var y = b.Y + Math.Cos(t * 0.4 + b.Cluster) * 2;
            frame.Items.Add(new DrawItem($"px{i}", b.Color, glitching ? 0.95 : 0.85) { Rings = [Rect(x, y, b.W, b.H)] });
        }

        if (front)
            for (var i = 0; i < _crumbs.Count; i++)
            {
                var c = _crumbs[i];
                frame.Items.Add(new DrawItem($"crumb{i}", c.Color, 1) { Rings = [Rect(c.X, c.Y, c.S, c.S)] });
            }
    }

    void AddEffects(Frame frame, double t)
    {
        var head = _world[_model.BoneIndex("head")];
        switch (Mood)
        {
            case Mood.Hack:
                AddLightning(frame, t);
                break;
            case Mood.Sleepy:
                for (var i = 0; i < 3; i++)
                {
                    var phase = (t * 0.35 + i / 3.0) % 1;
                    var (zx, zy) = head.Apply(262 + phase * 30, 125 - phase * 60);
                    var size = 6 + phase * 8;
                    frame.Items.Add(new DrawItem($"z{i}", Fills.None, 1 - phase)
                    {
                        Line = [zx, zy, zx + size, zy, zx, zy + size, zx + size, zy + size],
                        Stroke = CapriModel.White,
                        StrokeWidth = 2.6,
                    });
                }
                break;
            case Mood.Love:
                for (var i = 0; i < 4; i++)
                {
                    var phase = (t * 0.45 + i / 4.0) % 1;
                    var (hx, hy) = head.Apply(200 + i * 20 + Math.Sin(t * 2 + i) * 5, 120 - phase * 80);
                    frame.Items.Add(new DrawItem($"heart{i}", CapriModel.Red, 1 - phase * phase)
                    {
                        Path = "M 0,6 L -7,-1 A 3.5,3.5 0 0 1 0,-5 A 3.5,3.5 0 0 1 7,-1 Z",
                        Transform = Mat.Scale(1 + phase * 0.6, 1 + phase * 0.6) * Mat.Translate(hx, hy),
                        Stroke = CapriModel.White,
                        StrokeWidth = 1.2,
                    });
                }
                break;
            case Mood.Sad:
            {
                var phase = (t * 0.6) % 1;
                var (dx, dy) = head.Apply(CapriModel.EyeLX - 2, CapriModel.EyeLY + 8 + phase * 22);
                frame.Items.Add(new DrawItem("tear", "#5CFFFF", 1 - phase)
                {
                    Path = "M 0,-5 Q 4,1 0,4 Q -4,1 0,-5 Z",
                    Transform = Mat.Translate(dx, dy),
                });
                break;
            }
            case Mood.Angry:
            {
                var (ax, ay) = head.Apply(258, 122);
                var pulse = 1 + Math.Abs(Math.Sin(t * 8)) * 0.25;
                frame.Items.Add(new DrawItem("anger", Fills.None, 1)
                {
                    Path = "M -8,-3 Q -3,-3 -3,-8 M 3,-8 Q 3,-3 8,-3 M 8,3 Q 3,3 3,8 M -3,8 Q -3,3 -8,3",
                    Transform = Mat.Scale(pulse, pulse) * Mat.Translate(ax, ay),
                    Stroke = CapriModel.Red,
                    StrokeWidth = 3,
                });
                break;
            }
            case Mood.Think:
                for (var i = 0; i < 3; i++)
                {
                    var (bx, by) = head.Apply(258 + i * 11, 118 - i * 10);
                    var r = 2.6 + i * 1.4 + Math.Sin(t * 3 + i) * 0.7;
                    frame.Items.Add(new DrawItem($"dot{i}", CapriModel.White, 0.95)
                    {
                        Path = Circle(r),
                        Transform = Mat.Translate(bx, by),
                        Stroke = CapriModel.Rim,
                        StrokeWidth = 1.4,
                    });
                }
                break;
            case Mood.Laugh or Mood.Happy or Mood.Surprised:
                for (var i = 0; i < 3; i++)
                {
                    var phase = (t * 0.8 + i / 3.0) % 1;
                    var (sx, sy) = head.Apply(i switch { 0 => 190, 1 => 262, _ => 270 }, i switch { 0 => 120, 1 => 112, _ => 168 });
                    var s = Math.Sin(phase * Math.PI) * (Mood == Mood.Surprised ? 8 : 6.5);
                    frame.Items.Add(new DrawItem($"spark{i}", i == 1 ? "#FFF84A" : CapriModel.White, 1)
                    {
                        Rings = [[sx, sy - s, sx + s * 0.25, sy - s * 0.25, sx + s, sy, sx + s * 0.25, sy + s * 0.25, sx, sy + s, sx - s * 0.25, sy + s * 0.25, sx - s, sy, sx - s * 0.25, sy - s * 0.25]],
                        Stroke = CapriModel.Rim,
                        StrokeWidth = 0.8,
                    });
                }
                break;
        }
    }

    void AddLightning(Frame frame, double t)
    {
        if (t >= _nextBolt || _bolts.Count == 0)
        {
            _bolts.Clear();
            var (ax, ay) = _world[_model.BoneIndex("armB2")].Apply(318, 100);
            var (fx, fy) = _world[_model.BoneIndex("handF")].Apply(74, 266);
            for (var i = 0; i < 3; i++) _bolts.Add(Bolt(ax, ay, -10 + i * 40, 55));
            for (var i = 0; i < 3; i++) _bolts.Add(Bolt(fx, fy, 150 + i * 30, 55));
            _nextBolt = t + 0.06 + _rng.NextDouble() * 0.06;
        }
        for (var i = 0; i < _bolts.Count; i++)
        {
            frame.Items.Add(new DrawItem($"boltglow{i}", Fills.None, 0.45) { Line = _bolts[i], Stroke = "#FF8098", StrokeWidth = 5 });
            frame.Items.Add(new DrawItem($"bolt{i}", Fills.None, 1) { Line = _bolts[i], Stroke = CapriModel.Red, StrokeWidth = 2 });
        }
    }

    double[] Bolt(double x, double y, double angleDeg, double length)
    {
        var a = (angleDeg + (_rng.NextDouble() * 2 - 1) * 15) * Math.PI / 180;
        const int segments = 6;
        var pts = new double[(segments + 1) * 2];
        for (var i = 0; i <= segments; i++)
        {
            var f = (double)i / segments;
            var side = i == 0 ? 0 : (_rng.NextDouble() * 2 - 1) * 8;
            pts[i * 2] = x + Math.Cos(a) * length * f - Math.Sin(a) * side;
            pts[i * 2 + 1] = y + Math.Sin(a) * length * f + Math.Cos(a) * side;
        }
        return pts;
    }

    static double[] Rect(double x, double y, double w, double h) => [x, y, x + w, y, x + w, y + h, x, y + h];

    static string Circle(double r) => string.Create(CultureInfo.InvariantCulture, $"M {-r},0 A {r},{r} 0 1 1 {r},0 A {r},{r} 0 1 1 {-r},0 Z");
}
