namespace Capricon.Core;

/// <summary>Facial expression / animation state of the avatar.</summary>
public enum Mood
{
    Idle,
    Happy,
    Laugh,
    Smug,
    Think,
    Hack,
    Surprised,
    Sad,
    Angry,
    Sleepy,
    Love,
}

/// <summary>What Capri says and how she looks while saying it.</summary>
public sealed record Reply(string Text, Mood Mood = Mood.Idle)
{
    /// <summary>Set when the reply asks the app to do something to itself (mute, quit, ...).</summary>
    public AppSignal Signal { get; init; } = AppSignal.None;
}

public enum AppSignal
{
    None,
    Sleep,
    Wake,
    Mute,
    Unmute,
    Quit,
    ResetConversation,
    OpenSettings,
    Roam,
}

public static class MoodTag
{
    /// <summary>
    /// Splits a leading "[mood]" tag off a model reply: "[happy] Привет!" -> (Happy, "Привет!").
    /// Unknown or missing tags fall back to <paramref name="fallback"/>.
    /// </summary>
    public static (Mood Mood, string Text) Split(string raw, Mood fallback = Mood.Happy)
    {
        var text = raw.Trim();
        if (text.StartsWith('['))
        {
            var end = text.IndexOf(']');
            if (end > 1 && Enum.TryParse<Mood>(text[1..end], ignoreCase: true, out var mood))
                return (mood, text[(end + 1)..].Trim());
        }
        return (fallback, text);
    }
}
