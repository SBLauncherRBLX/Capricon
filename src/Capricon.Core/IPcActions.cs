namespace Capricon.Core;

/// <summary>Result of an action on the PC. <see cref="Details"/> is fed back to the AI brain.</summary>
public sealed record ActionResult(bool Ok, string Details)
{
    public static ActionResult Done(string details) => new(true, details);
    public static ActionResult Fail(string details) => new(false, details);
}

public enum MediaKey
{
    PlayPause,
    Next,
    Previous,
    VolumeUp,
    VolumeDown,
    Mute,
}

/// <summary>
/// Everything Capri can physically do on the computer. Implemented by the Windows app,
/// faked in tests. Apps and folders are whitelisted names, never arbitrary commands.
/// </summary>
public interface IPcActions
{
    static readonly string[] Apps =
        ["notepad", "calculator", "explorer", "terminal", "paint", "taskmanager", "settings", "browser", "snipping"];

    static readonly string[] Folders =
        ["desktop", "downloads", "documents", "pictures", "music", "videos"];

    Task<ActionResult> OpenUrl(string url);
    Task<ActionResult> WebSearch(string query);
    Task<ActionResult> LaunchApp(string app);
    Task<ActionResult> OpenFolder(string folder);
    Task<ActionResult> TakeScreenshot();
    Task<ActionResult> GetSystemInfo();
    Task<ActionResult> SetTimer(TimeSpan delay, string label);
    Task<ActionResult> AddNote(string text);
    Task<ActionResult> ReadNotes();
    Task<ActionResult> ReadClipboard();
    Task<ActionResult> WriteClipboard(string text);
    Task<ActionResult> GlitchScreen();
    Task<ActionResult> Media(MediaKey key);
    Task<ActionResult> ShowDesktop();
    Task<ActionResult> LockPc();
    Task<ActionResult> CursorDance();
}
