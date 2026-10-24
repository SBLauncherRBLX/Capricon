using System.Globalization;

namespace Capricon.Core;

/// <summary>Turns a parsed <see cref="Intent"/> into an action on the PC plus a reply. Works offline.</summary>
public sealed class LocalBrain(IPcActions pc, Func<DateTime>? clock = null)
{
    static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
    readonly Random _rng = new();
    readonly Func<DateTime> _now = clock ?? (() => DateTime.Now);

    public async Task<Reply> Handle(Intent intent)
    {
        switch (intent.Kind)
        {
            case IntentKind.Greet: return Persona.Greeting(_now());
            case IntentKind.Help: return Persona.Say("help");
            case IntentKind.WhoAreYou: return Persona.Say("who");
            case IntentKind.Thanks: return Persona.Say("thanks");
            case IntentKind.Joke: return Persona.Say("joke");

            case IntentKind.Time:
                return new($"Сейчас {_now():HH:mm}. Время — тоже просто данные, чумба.", Mood.Smug);
            case IntentKind.Date:
                return new($"Сегодня {_now().ToString("dddd, d MMMM yyyy", Ru)}.", Mood.Happy);
            case IntentKind.Coin:
                return new(_rng.Next(2) == 0 ? "Орёл! Подкинула честно. Почти." : "Решка! Судьба сказала своё.", Mood.Smug);
            case IntentKind.Dice:
                return new($"Выпало {_rng.Next(1, 7)}! Кости не врут. В отличие от меня.", Mood.Laugh);

            case IntentKind.OpenSite: return await Do(pc.OpenUrl(intent.Arg), "Открываю! Лови вкладку.", Mood.Hack);
            case IntentKind.Search: return await Do(pc.WebSearch(intent.Arg), $"Ищу «{intent.Arg}». Сеть мне всё расскажет.", Mood.Hack);
            case IntentKind.LaunchApp: return await Do(pc.LaunchApp(intent.Arg), "Запускаю. Смотри и учись.", Mood.Hack);
            case IntentKind.OpenFolder: return await Do(pc.OpenFolder(intent.Arg), "Папочка открыта!", Mood.Happy);
            case IntentKind.Glitch: return await Do(pc.GlitchScreen(), Persona.Line("glitch"), Mood.Hack);
            case IntentKind.ShowDesktop: return await Do(pc.ShowDesktop(), "Вжух, и все окна спрятались!", Mood.Laugh);
            case IntentKind.Lock: return await Do(pc.LockPc(), "Запираю двери. Никого не впускай, кроме меня.", Mood.Smug);
            case IntentKind.CursorDance: return await Do(pc.CursorDance(), "Курсор теперь мой! Потанцуем!", Mood.Laugh);

            case IntentKind.Media:
                return await Do(pc.Media(intent.Key), intent.Key switch
                {
                    MediaKey.Next => "Следующий трек! Надеюсь, там что-то громкое.",
                    MediaKey.Previous => "Назад! Эта песня была хороша.",
                    MediaKey.VolumeUp => "Громче! Пусть соседи тоже слушают.",
                    MediaKey.VolumeDown => "Тише-тише.",
                    MediaKey.Mute => "Звук выключен.",
                    _ => "Плей-пауза!",
                }, Mood.Happy);

            case IntentKind.Screenshot:
            {
                var r = await pc.TakeScreenshot();
                return r.Ok ? new("Щёлк! Скриншот сохранила, папку открыла.", Mood.Happy) : Fail(r);
            }
            case IntentKind.SystemInfo:
            {
                var r = await pc.GetSystemInfo();
                return r.Ok ? new(r.Details, Mood.Think) : Fail(r);
            }
            case IntentKind.Clipboard:
            {
                var r = await pc.ReadClipboard();
                if (!r.Ok) return Fail(r);
                var text = r.Details.Length > 140 ? r.Details[..140] + "…" : r.Details;
                return new(text.Length == 0 ? "В буфере пусто. Скучно." : $"В буфере: «{text}».", Mood.Smug);
            }
            case IntentKind.Timer:
            {
                if (intent.Delay <= TimeSpan.Zero) return new("Таймер на ноль? Уже сработал!", Mood.Laugh);
                var r = await pc.SetTimer(intent.Delay, intent.Arg);
                var about = intent.Arg.Length > 0 ? $" про «{intent.Arg}»" : "";
                return r.Ok ? new($"Поставила таймер на {Humanize(intent.Delay)}{about}. Не забуду, у меня сто памятей.", Mood.Happy) : Fail(r);
            }
            case IntentKind.AddNote:
            {
                var r = await pc.AddNote(intent.Arg);
                return r.Ok ? new("Записала. Теперь это знаем мы оба. И пара голосов у меня в голове.", Mood.Happy) : Fail(r);
            }
            case IntentKind.ReadNotes:
            {
                var r = await pc.ReadNotes();
                return r.Ok ? new(r.Details, Mood.Think) : Fail(r);
            }

            case IntentKind.Sleep: return Persona.Say("sleep") with { Signal = AppSignal.Sleep };
            case IntentKind.Wake: return Persona.Say("wake") with { Signal = AppSignal.Wake };
            case IntentKind.Mute: return new("Ладно, буду молча. Но пузыри с текстом остаются!", Mood.Sad) { Signal = AppSignal.Mute };
            case IntentKind.Unmute: return new("Голос включён! Ты по мне скучал.", Mood.Happy) { Signal = AppSignal.Unmute };
            case IntentKind.Forget: return new("Стёрла разговор. Ну, кто ты такой?", Mood.Smug) { Signal = AppSignal.ResetConversation };
            case IntentKind.Settings: return new("Покопайся в моих настройках. Осторожно, там щекотно.", Mood.Smug) { Signal = AppSignal.OpenSettings };
            case IntentKind.Roam: return new("Пойду разомнусь!", Mood.Laugh) { Signal = AppSignal.Roam };
            case IntentKind.Quit: return Persona.Say("bye") with { Signal = AppSignal.Quit };
        }
        return Persona.Say("unknown");
    }

    static async Task<Reply> Do(Task<ActionResult> action, string success, Mood mood)
    {
        var r = await action;
        return r.Ok ? new(success, mood) : Fail(r);
    }

    static Reply Fail(ActionResult r) => new($"{Persona.Line("fail")} {r.Details}", Mood.Sad);

    public static string Humanize(TimeSpan t)
    {
        if (t.TotalHours >= 1 && t.Minutes == 0 && t.Seconds == 0) return Plural((int)t.TotalHours, "час", "часа", "часов");
        if (t.TotalMinutes >= 1 && t.Seconds == 0) return Plural((int)t.TotalMinutes, "минуту", "минуты", "минут");
        if (t.TotalMinutes < 1) return Plural((int)t.TotalSeconds, "секунду", "секунды", "секунд");
        return $"{(int)t.TotalMinutes} мин {t.Seconds} с";
    }

    static string Plural(int n, string one, string few, string many)
    {
        var mod100 = n % 100;
        var mod10 = n % 10;
        var word = mod100 is >= 11 and <= 14 ? many : mod10 == 1 ? one : mod10 is >= 2 and <= 4 ? few : many;
        return $"{n} {word}";
    }
}
