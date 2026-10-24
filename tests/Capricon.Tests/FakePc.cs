using Capricon.Core;

namespace Capricon.Tests;

/// <summary>Records every action instead of touching the real PC.</summary>
sealed class FakePc : IPcActions
{
    public List<string> Calls { get; } = [];

    Task<ActionResult> Log(string call, string details = "ok")
    {
        Calls.Add(call);
        return Task.FromResult(ActionResult.Done(details));
    }

    public Task<ActionResult> OpenUrl(string url) => Log($"url:{url}");
    public Task<ActionResult> WebSearch(string query) => Log($"search:{query}");
    public Task<ActionResult> LaunchApp(string app) => Log($"app:{app}");
    public Task<ActionResult> OpenFolder(string folder) => Log($"folder:{folder}");
    public Task<ActionResult> TakeScreenshot() => Log("screenshot");
    public Task<ActionResult> GetSystemInfo() => Log("sysinfo", "Процессор 12%, память 40%.");
    public Task<ActionResult> SetTimer(TimeSpan delay, string label) => Log($"timer:{delay.TotalSeconds}:{label}");
    public Task<ActionResult> AddNote(string text) => Log($"note:{text}");
    public Task<ActionResult> ReadNotes() => Log("notes", "купить молоко");
    public Task<ActionResult> ReadClipboard() => Log("clipboard", "hello");
    public Task<ActionResult> WriteClipboard(string text) => Log($"copy:{text}");
    public Task<ActionResult> GlitchScreen() => Log("glitch");
    public Task<ActionResult> Media(MediaKey key) => Log($"media:{key}");
    public Task<ActionResult> ShowDesktop() => Log("desktop");
    public Task<ActionResult> LockPc() => Log("lock");
    public Task<ActionResult> CursorDance() => Log("cursor");
}
