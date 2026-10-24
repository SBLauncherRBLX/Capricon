using System.Xml.Linq;
using Capricon.Core;
using Capricon.Core.Avatar;

namespace Capricon.Tests;

public class AvatarTests
{
    [Fact]
    public void TracedModelLoadsWithSkinWeights()
    {
        var model = CapriModel.Load();
        Assert.True(model.Shapes.Count >= 15);
        Assert.NotEmpty(model.Eyes);
        Assert.NotEmpty(model.Mouth);
        Assert.NotEmpty(model.Visor);

        foreach (var shape in model.Shapes)
        foreach (var ring in shape.Bind)
        foreach (var w in ring)
            Assert.InRange(w.W0 + w.W1 + w.W2, 0.999, 1.001);
    }

    [Theory]
    [MemberData(nameof(AllMoods))]
    public void EveryMoodAnimatesToFiniteFrames(Mood mood)
    {
        var anim = new CapriAnimator { Mood = mood, TalkLevel = 0.6 };
        Frame frame = anim.Update(0);
        for (var i = 0; i < 90; i++) frame = anim.Update(1 / 30.0);

        Assert.Contains(frame.Items, i => i.Ghost);
        Assert.Contains(frame.Items, i => i.Id.StartsWith("mouth"));
        foreach (var item in frame.Items)
        {
            foreach (var ring in item.Rings ?? []) Assert.All(ring, v => Assert.True(double.IsFinite(v)));
            foreach (var v in item.Line ?? []) Assert.True(double.IsFinite(v));
        }

        // The SVG preview must be well-formed.
        XDocument.Parse(SvgRenderer.Render(frame));
    }

    [Fact]
    public void CroppedEdgesStayGluedToTheFrame()
    {
        var model = CapriModel.Load();
        var anim = new CapriAnimator { Mood = Mood.Laugh, Dragging = true, DragVelocity = 400 };
        Frame frame = anim.Update(0);
        for (var i = 0; i < 50; i++) frame = anim.Update(1 / 30.0);

        // Body shapes come first in the frame, after the background pixel blocks.
        var body = frame.Items.Where(i => i.Id.StartsWith("body")).ToList();
        for (var s = 0; s < model.Shapes.Count; s++)
        {
            var rest = model.Shapes[s].Rings[0];
            var now = body[s].Rings![0];
            for (var i = 0; i < rest.Length; i += 2)
            {
                if (rest[i + 1] > 0.5) continue; // only vertices on the top cut
                Assert.Equal(rest[i], now[i], 3);
                Assert.Equal(rest[i + 1], now[i + 1], 3);
            }
        }
    }

    public static TheoryData<Mood> AllMoods()
    {
        var data = new TheoryData<Mood>();
        foreach (var m in Enum.GetValues<Mood>()) data.Add(m);
        return data;
    }
}

public class CapriBrainTests
{
    [Fact]
    public async Task CommandsRunLocallyWithoutAKey()
    {
        var pc = new FakePc();
        var brain = new CapriBrain(pc);
        var reply = await brain.Handle("открой ютуб");
        Assert.Equal(["url:https://www.youtube.com"], pc.Calls);
        Assert.Equal(Mood.Hack, reply.Mood);
        Assert.False(brain.HasAi);
    }

    [Fact]
    public async Task FreeTextWithoutAKeyAsksForOne()
    {
        var brain = new CapriBrain(new FakePc());
        var reply = await brain.Handle("расскажи мне что-нибудь интересное про космос");
        Assert.Contains("ключ", reply.Text, StringComparison.OrdinalIgnoreCase);
    }
}
