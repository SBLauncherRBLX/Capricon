using System.Globalization;
using Capricon.Core;
using Capricon.Core.Avatar;

// Renders the real animator to SVG frames so the avatar can be checked without Windows.
//   RigPreview sheet <outDir>                      one still per mood
//   RigPreview anim  <outDir> <mood> <sec> [fps] [talk]   animation frames for one mood
var mode = args.ElementAtOrDefault(0) ?? "sheet";
var outDir = args.ElementAtOrDefault(1) ?? "preview";
Directory.CreateDirectory(outDir);

if (mode == "sheet")
{
    foreach (var mood in Enum.GetValues<Mood>())
    {
        var anim = new CapriAnimator { Mood = mood };
        Frame frame = anim.Update(0);
        for (var i = 0; i < 40; i++) frame = anim.Update(1 / 30.0);
        File.WriteAllText(Path.Combine(outDir, $"{mood}.svg"), SvgRenderer.Render(frame));
    }
}
else
{
    var mood = Enum.Parse<Mood>(args[2], ignoreCase: true);
    var seconds = double.Parse(args[3], CultureInfo.InvariantCulture);
    var fps = args.Length > 4 ? int.Parse(args[4]) : 30;
    var talk = args.Length > 5 && args[5] == "talk";
    var anim = new CapriAnimator { Mood = mood };
    var n = (int)(seconds * fps);
    for (var i = 0; i < n; i++)
    {
        var t = (double)i / fps;
        // Fake speech: syllable-like bursts.
        anim.TalkLevel = talk ? Math.Max(0, Math.Sin(t * 17) * 0.6 + Math.Sin(t * 5.3) * 0.4) : 0;
        if (mood == Mood.Happy && i == 0) anim.Waving = true;
        if (i == n / 2 && mood is Mood.Idle) anim.Glitch(0.3);
        var frame = anim.Update(1.0 / fps);
        File.WriteAllText(Path.Combine(outDir, $"f{i:000}.svg"), SvgRenderer.Render(frame));
    }
}
