using System.Globalization;
using System.Text;

namespace Capricon.Core.Avatar;

/// <summary>
/// Draws a <see cref="Frame"/> as SVG. Used by the preview tool and tests; mirrors the WPF
/// renderer in the app: ghost copies, purple rim, edge dissolve and glitch slices.
/// </summary>
public static class SvgRenderer
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Render(Frame f, string? background = null)
    {
        var sb = new StringBuilder();
        sb.Append(Inv, $"""<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" width="{f.Width}" height="{f.Height}" viewBox="0 0 {f.Width} {f.Height}">""");
        sb.Append("<defs>");

        // Figure group, drawn once and reused for glitch slices.
        sb.Append("""<g id="fig">""");
        var silhouette = f.Items.Where(i => i.Ghost && i.Rings is not null).ToList();
        foreach (var (offset, color) in new[] { (f.GhostA, Frame.GhostColorA), (f.GhostB, Frame.GhostColorB) })
        {
            sb.Append(Inv, $"""<g transform="translate({offset.X:0.##},{offset.Y:0.##})" fill="{color}">""");
            foreach (var item in silhouette) sb.Append($"""<path fill-rule="evenodd" d="{Rings(item.Rings!)}"/>""");
            sb.Append("</g>");
        }
        sb.Append(Inv, $"""<g fill="none" stroke="{f.OutlineColor}" stroke-width="{f.OutlineWidth * 2:0.##}" stroke-linejoin="round">""");
        foreach (var item in silhouette) sb.Append($"""<path d="{Rings(item.Rings!)}"/>""");
        sb.Append("</g>");
        foreach (var item in f.Items) sb.Append(Shape(item));
        sb.Append("</g>");

        var bites = new StringBuilder(string.Create(Inv, $"M0,0 H{f.Width} V{f.Height} H0 Z"));
        foreach (var b in f.EdgeBites) bites.Append(Inv, $" M{b.X:0.#},{b.Y:0.#} h{b.W:0.#} v{b.H:0.#} h{-b.W:0.#} Z");
        sb.Append($"""<clipPath id="bites"><path clip-rule="evenodd" d="{bites}"/></clipPath>""");

        if (f.Slices.Count > 0)
        {
            var hole = new StringBuilder(string.Create(Inv, $"M-50,-50 H{f.Width + 50} V{f.Height + 50} H-50 Z"));
            for (var i = 0; i < f.Slices.Count; i++)
            {
                var s = f.Slices[i];
                hole.Append(Inv, $" M-50,{s.Y0:0.#} H{f.Width + 50} V{s.Y1:0.#} H-50 Z");
                sb.Append(Inv, $"""<clipPath id="s{i}"><rect x="-50" y="{s.Y0:0.#}" width="{f.Width + 100}" height="{s.Y1 - s.Y0:0.#}"/></clipPath>""");
            }
            sb.Append($"""<clipPath id="rest"><path clip-rule="evenodd" d="{hole}"/></clipPath>""");
        }
        sb.Append("</defs>");

        if (background is not null) sb.Append($"""<rect width="100%" height="100%" fill="{background}"/>""");
        sb.Append("""<g clip-path="url(#bites)">""");
        if (f.Slices.Count == 0)
        {
            sb.Append("""<use href="#fig" xlink:href="#fig"/>""");
        }
        else
        {
            sb.Append("""<use href="#fig" xlink:href="#fig" clip-path="url(#rest)"/>""");
            for (var i = 0; i < f.Slices.Count; i++)
                sb.Append(Inv, $"""<g clip-path="url(#s{i})"><use href="#fig" xlink:href="#fig" transform="translate({f.Slices[i].Dx:0.#},0)"/></g>""");
        }
        sb.Append("</g></svg>");
        return sb.ToString();
    }

    static string Rings(double[][] rings)
    {
        var d = new StringBuilder();
        foreach (var r in rings)
        {
            for (var i = 0; i < r.Length; i += 2) d.Append(Inv, $"{(i == 0 ? "M" : "L")}{r[i]:0.##},{r[i + 1]:0.##} ");
            d.Append("Z ");
        }
        return d.ToString();
    }

    static string Shape(DrawItem item)
    {
        var fill = item.Fill == Fills.None ? "none" : item.Fill;
        var stroke = item.Stroke is null || item.StrokeWidth <= 0
            ? ""
            : string.Create(Inv, $" stroke=\"{item.Stroke}\" stroke-width=\"{item.StrokeWidth:0.##}\" stroke-linejoin=\"round\" stroke-linecap=\"round\"");
        var opacity = item.Opacity < 1 ? string.Create(Inv, $" opacity=\"{item.Opacity:0.###}\"") : "";

        if (item.Rings is { } rings)
            return $"""<path fill-rule="evenodd" d="{Rings(rings)}" fill="{fill}"{stroke}{opacity}/>""";

        if (item.Line is { } line)
        {
            var d = new StringBuilder();
            for (var i = 0; i < line.Length; i += 2) d.Append(Inv, $"{(i == 0 ? "M" : "L")}{line[i]:0.##},{line[i + 1]:0.##} ");
            return $"""<path d="{d}" fill="none"{stroke}{opacity}/>""";
        }

        if (item.Circles is { } c)
        {
            var sb = new StringBuilder($"""<g fill="{fill}"{opacity}>""");
            for (var i = 0; i < c.Length; i += 3) sb.Append(Inv, $"""<circle cx="{c[i]:0.##}" cy="{c[i + 1]:0.##}" r="{c[i + 2]:0.##}"/>""");
            return sb.Append("</g>").ToString();
        }

        var m = item.Transform;
        return string.Create(Inv,
            $"""<path d="{item.Path}" transform="matrix({m.M11:0.####},{m.M12:0.####},{m.M21:0.####},{m.M22:0.####},{m.Dx:0.##},{m.Dy:0.##})" fill="{fill}"{stroke}{opacity}/>""");
    }
}
