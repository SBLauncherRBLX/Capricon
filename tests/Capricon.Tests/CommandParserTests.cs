using Capricon.Core;

namespace Capricon.Tests;

public class CommandParserTests
{
    [Theory]
    [InlineData("Открой ютуб", IntentKind.OpenSite, "https://www.youtube.com")]
    [InlineData("капри, открой github.com/anthropics", IntentKind.OpenSite, "https://github.com/anthropics")]
    [InlineData("запусти блокнот", IntentKind.LaunchApp, "notepad")]
    [InlineData("открой командную строку", IntentKind.LaunchApp, "terminal")]
    [InlineData("открой диспетчер задач", IntentKind.LaunchApp, "taskmanager")]
    [InlineData("открой загрузки", IntentKind.OpenFolder, "downloads")]
    [InlineData("открой папку документы", IntentKind.OpenFolder, "documents")]
    [InlineData("найди рецепт пиццы", IntentKind.Search, "рецепт пиццы")]
    [InlineData("сделай скриншот", IntentKind.Screenshot, "")]
    [InlineData("как мой пк?", IntentKind.SystemInfo, "")]
    [InlineData("запомни купить молоко", IntentKind.AddNote, "купить молоко")]
    [InlineData("покажи заметки", IntentKind.ReadNotes, "")]
    [InlineData("взломай экран", IntentKind.Glitch, "")]
    [InlineData("который час", IntentKind.Time, "")]
    [InlineData("расскажи шутку", IntentKind.Joke, "")]
    [InlineData("Привет, Капри!", IntentKind.Greet, "")]
    [InlineData("спасибо", IntentKind.Thanks, "")]
    [InlineData("иди спать", IntentKind.Sleep, "")]
    [InlineData("спи", IntentKind.Sleep, "")]
    [InlineData("что ты умеешь?", IntentKind.Help, "")]
    [InlineData("выключись", IntentKind.Quit, "")]
    [InlineData("сверни все окна", IntentKind.ShowDesktop, "")]
    [InlineData("поиграй с курсором", IntentKind.CursorDance, "")]
    public void Recognises(string input, IntentKind kind, string arg)
    {
        var intent = CommandParser.Parse(input);
        Assert.NotNull(intent);
        Assert.Equal(kind, intent.Kind);
        Assert.Equal(arg, intent.Arg);
    }

    [Theory]
    [InlineData("поставь таймер на 5 минут", 300, "")]
    [InlineData("напомни через 10 минут выпить воды", 600, "выпить воды")]
    [InlineData("таймер 30 сек", 30, "")]
    [InlineData("напомни через 2 часа про созвон", 7200, "созвон")]
    [InlineData("через 1,5 мин напомни что чайник", 90, "чайник")]
    public void ParsesTimers(string input, double seconds, string label)
    {
        var intent = CommandParser.Parse(input);
        Assert.NotNull(intent);
        Assert.Equal(IntentKind.Timer, intent.Kind);
        Assert.Equal(seconds, intent.Delay.TotalSeconds);
        Assert.Equal(label, intent.Arg);
    }

    [Theory]
    [InlineData("громче", MediaKey.VolumeUp)]
    [InlineData("следующий трек", MediaKey.Next)]
    [InlineData("поставь на паузу", MediaKey.PlayPause)]
    public void ParsesMedia(string input, MediaKey key)
    {
        var intent = CommandParser.Parse(input);
        Assert.NotNull(intent);
        Assert.Equal(IntentKind.Media, intent.Kind);
        Assert.Equal(key, intent.Key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("что думаешь о смысле жизни")]
    [InlineData("напиши стихотворение про кота")]
    public void LeavesFreeTextForTheAi(string input) => Assert.Null(CommandParser.Parse(input));
}

public class MoodTagTests
{
    [Fact]
    public void SplitsLeadingTag() => Assert.Equal((Mood.Laugh, "Ха-ха!"), MoodTag.Split("[laugh] Ха-ха!"));

    [Fact]
    public void FallsBackWithoutTag() => Assert.Equal((Mood.Happy, "Привет"), MoodTag.Split("Привет"));

    [Fact]
    public void IgnoresUnknownTag() => Assert.Equal((Mood.Happy, "[zzz] hi"), MoodTag.Split("[zzz] hi"));
}

public class LocalBrainTests
{
    [Fact]
    public async Task RunsActionsAndAnswers()
    {
        var pc = new FakePc();
        var brain = new LocalBrain(pc, () => new DateTime(2026, 10, 24, 14, 5, 0));

        var reply = await brain.Handle(CommandParser.Parse("открой ютуб")!);
        Assert.Equal(["url:https://www.youtube.com"], pc.Calls);
        Assert.Equal(Mood.Hack, reply.Mood);

        var time = await brain.Handle(CommandParser.Parse("который час")!);
        Assert.Contains("14:05", time.Text);

        var timer = await brain.Handle(CommandParser.Parse("напомни через 3 минуты про чай")!);
        Assert.Contains("3 минуты", timer.Text);
        Assert.Contains("timer:180:чай", pc.Calls);
    }

    [Fact]
    public async Task SignalsAppStateChanges()
    {
        var brain = new LocalBrain(new FakePc());
        Assert.Equal(AppSignal.Quit, (await brain.Handle(new Intent(IntentKind.Quit))).Signal);
        Assert.Equal(AppSignal.Mute, (await brain.Handle(new Intent(IntentKind.Mute))).Signal);
    }

    [Theory]
    [InlineData(1, "1 минуту")]
    [InlineData(120, "2 минуты")]
    [InlineData(660, "11 минут")]
    [InlineData(45, "45 секунд")]
    [InlineData(3600, "1 час")]
    [InlineData(90, "1 мин 30 с")]
    public void HumanizesDurations(int seconds, string expected) =>
        Assert.Equal(expected, LocalBrain.Humanize(TimeSpan.FromSeconds(seconds == 1 ? 60 : seconds)));
}
