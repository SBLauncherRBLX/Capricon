using System.Globalization;
using System.Text.RegularExpressions;

namespace Capricon.Core;

public enum IntentKind
{
    Greet,
    Help,
    WhoAreYou,
    Time,
    Date,
    OpenSite,
    LaunchApp,
    OpenFolder,
    Search,
    Screenshot,
    SystemInfo,
    Timer,
    AddNote,
    ReadNotes,
    Clipboard,
    Glitch,
    Media,
    ShowDesktop,
    Lock,
    CursorDance,
    Roam,
    Joke,
    Coin,
    Dice,
    Sleep,
    Wake,
    Mute,
    Unmute,
    Forget,
    Settings,
    Quit,
    Thanks,
}

/// <summary>A recognised command. <see cref="Arg"/> holds the URL, app id, query, note text, etc.</summary>
public sealed record Intent(IntentKind Kind, string Arg = "")
{
    public TimeSpan Delay { get; init; }
    public MediaKey Key { get; init; }
}

/// <summary>
/// Offline understanding of short Russian/English commands. Anything it does not
/// recognise goes to the AI brain (or gets a playful "не поняла").
/// </summary>
public static class CommandParser
{
    static readonly Dictionary<string, string> Sites = new()
    {
        ["youtube"] = "https://www.youtube.com",
        ["ютуб"] = "https://www.youtube.com",
        ["ютьюб"] = "https://www.youtube.com",
        ["google"] = "https://www.google.com",
        ["гугл"] = "https://www.google.com",
        ["vk"] = "https://vk.com",
        ["вк"] = "https://vk.com",
        ["вконтакте"] = "https://vk.com",
        ["twitch"] = "https://www.twitch.tv",
        ["твич"] = "https://www.twitch.tv",
        ["github"] = "https://github.com",
        ["гитхаб"] = "https://github.com",
        ["netflix"] = "https://www.netflix.com",
        ["нетфликс"] = "https://www.netflix.com",
        ["spotify"] = "https://open.spotify.com",
        ["спотифай"] = "https://open.spotify.com",
        ["steam"] = "https://store.steampowered.com",
        ["стим"] = "https://store.steampowered.com",
        ["telegram"] = "https://web.telegram.org",
        ["телеграм"] = "https://web.telegram.org",
        ["почту"] = "https://mail.google.com",
        ["gmail"] = "https://mail.google.com",
        ["карты"] = "https://yandex.ru/maps",
        ["яндекс"] = "https://ya.ru",
        ["reddit"] = "https://www.reddit.com",
        ["реддит"] = "https://www.reddit.com",
        ["chatgpt"] = "https://claude.ai",
        ["claude"] = "https://claude.ai",
    };

    // Longest synonyms first so "командную строку" wins over shorter matches.
    static readonly (string Word, string App)[] Apps =
    [
        ("диспетчер задач", "taskmanager"), ("task manager", "taskmanager"),
        ("командную строку", "terminal"), ("командная строка", "terminal"),
        ("терминал", "terminal"), ("консоль", "terminal"), ("cmd", "terminal"), ("terminal", "terminal"),
        ("блокнот", "notepad"), ("notepad", "notepad"),
        ("калькулятор", "calculator"), ("calculator", "calculator"), ("calc", "calculator"),
        ("проводник", "explorer"), ("explorer", "explorer"),
        ("paint", "paint"), ("паинт", "paint"), ("пэйнт", "paint"), ("рисовалку", "paint"),
        ("параметры", "settings"), ("настройки windows", "settings"), ("настройки виндовс", "settings"),
        ("браузер", "browser"), ("browser", "browser"),
        ("ножницы", "snipping"),
    ];

    static readonly (string Word, string Folder)[] Folders =
    [
        ("рабочий стол", "desktop"), ("desktop", "desktop"),
        ("загрузки", "downloads"), ("downloads", "downloads"),
        ("документы", "documents"), ("documents", "documents"),
        ("картинки", "pictures"), ("изображения", "pictures"), ("фотки", "pictures"), ("pictures", "pictures"),
        ("музыку", "music"), ("музыка", "music"), ("music", "music"),
        ("видео", "videos"), ("videos", "videos"),
    ];

    const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    static readonly Regex OpenVerb = new(@"^(?:открой|запусти|включи|покажи|зайди на|перейди на|open|launch|start|run)\s+(?:мне\s+)?(?:сайт\s+|папку\s+|приложение\s+|программу\s+)?(.+)$", Opt);
    static readonly Regex Search = new(@"^(?:найди|загугли|погугли|поищи|ищи|search(?: for)?|google)\s+(?:в интернете\s+|мне\s+)?(.+)$", Opt);
    static readonly Regex Domain = new(@"^[a-z0-9-]+(\.[a-z0-9-]+)+(/\S*)?$", Opt);
    static readonly Regex TimerRx = new(@"(\d+(?:[.,]\d+)?)\s*(с|сек\w*|s|sec\w*|м|мин\w*|m|min\w*|ч|час\w*|h|hour\w*)\b", Opt);
    static readonly Regex TimerWord = new(@"^(?:поставь\s+)?(?:таймер|напомни|напоминание|разбуди|timer|remind)|через\s+\d", Opt);
    static readonly Regex TimerFiller = new(@"\b(?:поставь|таймер|напомни|напоминание|разбуди|мне|меня|через|на|timer|remind|me|in|for)\b", Opt);
    static readonly Regex TimerLabel = new(@"^(?:про|о|об|что|чтобы|to|about)\s+", Opt);
    static readonly Regex Note = new(@"^(?:запиши|запомни|заметка|note|remember)\s*[:\-—,]?\s+(.+)$", Opt);

    public static Intent? Parse(string input)
    {
        var text = Normalize(input);
        if (text.Length == 0) return null;

        if (Has(text, "что ты умеешь", "помощь", "команды", "help", "что умеешь")) return new(IntentKind.Help);
        if (Has(text, "кто ты", "расскажи о себе", "who are you", "ты кто")) return new(IntentKind.WhoAreYou);
        if (Has(text, "настройки капри", "твои настройки", "settings")) return new(IntentKind.Settings);
        if (Has(text, "выключись", "закройся", "выход", "завершись", "quit", "exit")) return new(IntentKind.Quit);
        if (Has(text, "забудь", "сбрось разговор", "начнем сначала", "reset")) return new(IntentKind.Forget);
        if (Has(text, "замолчи", "без голоса", "выключи голос", "не говори", "mute yourself")) return new(IntentKind.Mute);
        if (Has(text, "включи голос", "говори вслух", "скажи вслух", "unmute")) return new(IntentKind.Unmute);
        if (Has(text, "проснись", "wake up", "подъем")) return new(IntentKind.Wake);
        if (Has(text, "иди спать", "поспи", "отдохни", "go to sleep") || Word(text, "спи")) return new(IntentKind.Sleep);

        // Notes go before "open"/"search": "запомни найти билеты" is a note, not a search.
        if (Note.Match(text) is { Success: true } note) return new(IntentKind.AddNote, note.Groups[1].Value.Trim());
        if (Has(text, "заметки", "что я просил запомнить", "my notes", "read notes")) return new(IntentKind.ReadNotes);

        if (TimerWord.IsMatch(text) && TimerRx.Match(text) is { Success: true } t)
        {
            var delay = ToDelay(t.Groups[1].Value, t.Groups[2].Value);
            var rest = TimerFiller.Replace(text.Remove(t.Index, t.Length), " ");
            var label = TimerLabel.Replace(Regex.Replace(rest, @"\s+", " ").Trim(), "");
            return new(IntentKind.Timer, label.Trim()) { Delay = delay };
        }

        if (Has(text, "скриншот", "скрин", "сфоткай экран", "screenshot", "снимок экрана")) return new(IntentKind.Screenshot);
        if (Has(text, "покажи рабочий стол", "сверни все", "сверни окна", "show desktop")) return new(IntentKind.ShowDesktop);
        if (Has(text, "заблокируй", "lock pc", "lock the")) return new(IntentKind.Lock);

        if (MediaFor(text) is { } key) return new(IntentKind.Media) { Key = key };

        if (OpenVerb.Match(text) is { Success: true } open)
        {
            var target = open.Groups[1].Value.Trim().TrimEnd('.', '!', '?');
            foreach (var (word, folder) in Folders)
                if (target == word || target.StartsWith(word + " ") || target == "папку " + word)
                    return new(IntentKind.OpenFolder, folder);
            foreach (var (word, app) in Apps)
                if (target.Contains(word)) return new(IntentKind.LaunchApp, app);
            foreach (var (word, url) in Sites)
                if (target == word || target.StartsWith(word + " ")) return new(IntentKind.OpenSite, url);
            if (Domain.IsMatch(target))
                return new(IntentKind.OpenSite, target.StartsWith("http") ? target : "https://" + target);
        }

        if (Search.Match(text) is { Success: true } s) return new(IntentKind.Search, s.Groups[1].Value.Trim());

        if (Has(text, "глитч", "взломай", "хакни", "сломай экран", "hack", "glitch", "устрой хаос")) return new(IntentKind.Glitch);
        if (Has(text, "курсор", "мышк", "cursor")) return new(IntentKind.CursorDance);
        if (Has(text, "погуляй", "пробегись", "побегай", "прогуляйся", "walk around")) return new(IntentKind.Roam);
        if (Has(text, "нагрузк", "как мой пк", "как мой комп", "состояние пк", "системы", "система", "статус", "процессор", "оперативк", "system info", "cpu")) return new(IntentKind.SystemInfo);
        if (Has(text, "буфер", "что я скопировал", "clipboard")) return new(IntentKind.Clipboard);
        if (Has(text, "который час", "сколько времени", "время", "what time")) return new(IntentKind.Time);
        if (Has(text, "какое число", "какое сегодня", "какой сегодня день", "дата", "what day", "date")) return new(IntentKind.Date);
        if (Has(text, "шутк", "анекдот", "рассмеши", "пошути", "joke")) return new(IntentKind.Joke);
        if (Has(text, "монетк", "орел или решка", "flip a coin")) return new(IntentKind.Coin);
        if (Has(text, "кубик", "кости", "dice", "roll")) return new(IntentKind.Dice);
        if (Has(text, "спасибо", "благодарю", "thanks", "thank you", "спс")) return new(IntentKind.Thanks);
        if (Regex.IsMatch(text, @"^(привет|хай|хей|здравствуй|здарова|салют|йо|yo|hi|hello|hey)\b", Opt)) return new(IntentKind.Greet);

        return null;
    }

    static MediaKey? MediaFor(string text)
    {
        if (Has(text, "следующий трек", "следующую песню", "next track", "дальше трек")) return MediaKey.Next;
        if (Has(text, "предыдущий трек", "прошлый трек", "previous track")) return MediaKey.Previous;
        if (Has(text, "громче", "прибавь звук", "volume up")) return MediaKey.VolumeUp;
        if (Has(text, "тише", "убавь звук", "volume down")) return MediaKey.VolumeDown;
        if (Has(text, "выключи звук", "без звука", "mute sound")) return MediaKey.Mute;
        if (Has(text, "пауза", "поставь на паузу", "продолжи музыку", "play pause", "play/pause")) return MediaKey.PlayPause;
        return null;
    }

    static TimeSpan ToDelay(string number, string unit)
    {
        var value = double.Parse(number.Replace(',', '.'), CultureInfo.InvariantCulture);
        return unit[0] switch
        {
            'с' or 's' => TimeSpan.FromSeconds(value),
            'ч' or 'h' => TimeSpan.FromHours(value),
            _ => TimeSpan.FromMinutes(value),
        };
    }

    static string Normalize(string input) =>
        Regex.Replace(input.Trim().ToLowerInvariant().Replace('ё', 'е'), @"^(капри|capri)[,!.\s]*", "", Opt)
            .Trim()
            .TrimEnd('!', '?', '.');

    static bool Has(string text, params string[] needles) => needles.Any(text.Contains);

    static bool Word(string text, string word) => Regex.IsMatch(text, $@"\b{word}\b", Opt);
}
