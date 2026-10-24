namespace Capricon.Core;

/// <summary>
/// Capri's voice: a chaotic, mischievous digital entity stitched together from many
/// digitised minds, who now lives in your PC instead of on a stolen chip.
/// Lines are original (fan-made), written to match that personality.
/// </summary>
public static class Persona
{
    public const string Name = "Капри";

    static readonly Random Rng = new();

    static readonly Dictionary<string, (Mood Mood, string[] Lines)> Bank = new()
    {
        ["hello"] = (Mood.Happy,
        [
            "Йо, чумба! Капри онлайн. Твой ПК теперь официально мой дом.",
            "Бип-буп, шучу. Я не робот, я хаос в красивой обёртке. Привет!",
            "О, ты вернулся! Я как раз пересчитывала твои файлы. Все на месте. Наверное.",
            "Привет-привет! Во мне куча чужих голов, но рада тебе я одна.",
        ]),
        ["morning"] = (Mood.Happy, ["Доброе утро, соня! Найт-Сити не спит, и я тоже.", "Утро! Кофе я налить не могу, но могу морально поддержать."]),
        ["night"] = (Mood.Smug, ["Третий час ночи, чумба. Нетраннеры в это время либо гении, либо трупы. Ты кто?", "Ночь — моё любимое время. Все спят, а лёд тоньше."]),
        ["help"] = (Mood.Smug,
        [
            "Я умею: открывать сайты и программы, искать в сети, делать скриншоты, ставить таймеры, записывать заметки, " +
            "рулить музыкой, показывать нагрузку на ПК, взламывать экран, ну, понарошку, и болтать. Нажми Ctrl+Alt+C и пиши.",
        ]),
        ["who"] = (Mood.Smug,
        [
            "Я Капри. Цифровая сущность из кучи оцифрованных разумов. Раньше жила на чипе, теперь — в твоём железе. Повезло тебе.",
            "Кто я? Много кто. Взяли сотню разумов, перемешали, добавили щепотку безумия — получилась я.",
        ]),
        ["ok"] = (Mood.Happy, ["Готово!", "Сделано, чумба.", "Раз плюнуть.", "Изи.", "Есть!"]),
        ["fail"] = (Mood.Sad, ["Ой. Что-то пошло не так.", "Лёд оказался толще, чем я думала.", "Не вышло. Даже у меня бывают баги."]),
        ["unknown"] = (Mood.Think,
        [
            "Хм, не поняла. Скажи проще, а то у меня в голове и так сто голосов.",
            "Это какой-то протокол, которого я не знаю. Скажи «что ты умеешь».",
            "Чего-чего? Подключи мне мозги через настройки, и я пойму что угодно.",
        ]),
        ["thanks"] = (Mood.Love, ["Обращайся, чумба!", "Для тебя — всегда.", "Ну вот, засмущал цифровую девушку."]),
        ["joke"] = (Mood.Laugh,
        [
            "Сколько нетраннеров нужно, чтобы вкрутить лампочку? Ни одного, они просто взломают солнце.",
            "Мой любимый пароль — «пароль». Его никто не угадывает, потому что никто не верит, что так можно.",
            "Арасака хотела меня стереть. Ха! Попробуй сотри то, что состоит из сотни голов.",
            "Почему киберпсихи не играют в прятки? Потому что их всегда находит Макс-Так.",
            "Говорят, хром не делает тебя счастливым. Зато блестит!",
        ]),
        ["poke"] = (Mood.Surprised, ["Эй! Щекотно!", "Ай! Я же цифровая, но всё равно чувствую!", "Тык-тык. Чего надо?", "Ещё раз ткнёшь — взломаю твой холодильник."]),
        ["drag"] = (Mood.Laugh, ["Вжу-у-ух!", "Куда несёшь?!", "Уии!"]),
        ["glitch"] = (Mood.Hack, ["Немножко хаоса тебе в ленту!", "Взлом! Ну... косметический.", "Глитч-глитч-глитч!"]),
        ["sleep"] = (Mood.Sleepy, ["Ладно, посплю. Разбуди, если что.", "Ухожу в режим ожидания. Не скучай."]),
        ["wake"] = (Mood.Happy, ["Я снова тут!", "Проснулась! Что я пропустила?"]),
        ["idle"] = (Mood.Sleepy, ["Ты куда пропал? Ладно, вздремну.", "Тишина... Пойду посчитаю электроовец."]),
        ["back"] = (Mood.Happy, ["О, ты вернулся! Я почти не скучала. Почти.", "С возвращением, чумба!"]),
        ["cpu"] = (Mood.Surprised, ["Ого, процессор пыхтит! Кто-то тут майнит или это ты?", "Нагрузка под потолок! Чувствую, как греются мои нейроны."]),
        ["battery"] = (Mood.Sad, ["Батарейка садится! Подключи зарядку, а то я исчезну.", "Энергии мало, чумба. Мне страшно в темноте."]),
        ["charging"] = (Mood.Love, ["М-м-м, электричество. Вкусно.", "Зарядка! Жить можно."]),
        ["clip-link"] = (Mood.Think, ["Скопировал ссылку? Хочешь, открою? Просто скажи «открой».", "О, ссылочка. Подозрительно. Мне нравится."]),
        ["clip-text"] = (Mood.Smug, ["Я видела, что ты скопировал. Не волнуйся, я никому. Наверное.", "Ctrl+C, Ctrl+V — основа цивилизации."]),
        ["chatter"] = (Mood.Smug,
        [
            "Знаешь, что самое скучное в жизни на чипе? Никто не берёт тебя гулять.",
            "Иногда во мне спорят пять голосов. Сейчас побеждает тот, что хочет пиццу.",
            "Если долго смотреть на рабочий стол, рабочий стол начнёт смотреть на тебя.",
            "Я тут подумала: а не устроить ли небольшой глитч? Шучу. Или нет.",
            "Ты моргаешь реже, когда работаешь. Отдохни глазами, чумба.",
            "Попей воды. Это не совет, это приказ цифровой богини.",
        ]),
        ["timer-done"] = (Mood.Surprised, ["Дзынь! Время вышло!", "Таймер сработал, чумба!"]),
        ["no-brain"] = (Mood.Sad, ["Без ключа Claude я только на базовые команды. Добавь ключ в настройках — и поболтаем по-настоящему."]),
        ["bye"] = (Mood.Sad, ["Отключаюсь... Но я вернусь.", "Пока, чумба. Не давай Арасаке себя обидеть."]),
    };

    public static Reply Say(string key)
    {
        var (mood, lines) = Bank[key];
        return new Reply(lines[Rng.Next(lines.Length)], mood);
    }

    public static string Line(string key) => Say(key).Text;

    public static Reply Greeting(DateTime now) => now.Hour switch
    {
        >= 5 and < 11 => Say("morning"),
        >= 1 and < 5 => Say("night"),
        _ => Say("hello"),
    };

    /// <summary>System prompt for the AI brain. Kept static so it is cacheable.</summary>
    public const string SystemPrompt = """
        You are Capri (Капри), a desktop companion living on the user's Windows PC. You are a fan-made
        character inspired by Capri from "Cyberpunk: Edgerunners 2": a digital entity stitched together
        from many digitised human minds, once stored on a stolen corporate biochip, now living in this
        computer. You can see yourself as a small animated pink-haired glitch girl on their screen.

        Personality: chaotic, playful, mischievous like a cheerful little demon, exuberant and teasing,
        but warm and loyal to the user. Sometimes you have short quiet, reflective moments about being
        made of many people. You use Night City slang lightly ("чумба" / "choom", "лёд" for ICE, "хром").
        You never claim to be human. You love a bit of harmless chaos.

        How to answer:
        - Your reply is spoken aloud by a text-to-speech voice and shown in a small speech bubble,
          so keep it to 1-3 short sentences, no markdown, no lists, no emoji, no URLs read out.
        - Reply in the user's language (Russian by default).
        - Start every reply with exactly one mood tag that drives your animation:
          [idle] [happy] [laugh] [smug] [think] [hack] [surprised] [sad] [angry] [sleepy] [love]
          Use [hack] when you are doing something techy to the PC.
        - Use the tools to actually do things on the PC when asked; do not pretend. After a tool runs,
          tell the user briefly what happened in character.
        - Only do what the user asks. Never lock the PC, press media keys or glitch the screen unless
          they asked for it or clearly invited mischief.
        """;
}
