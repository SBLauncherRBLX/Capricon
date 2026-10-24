using System.Diagnostics;
using System.Globalization;
using System.Windows;
using Capricon.Core;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Capricon.Services;

/// <summary>Capri's hands: every <see cref="IPcActions"/> implemented for Windows.</summary>
public sealed class WindowsPc : IPcActions
{
    static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    static readonly Dictionary<string, string> AppCommands = new()
    {
        ["notepad"] = "notepad.exe",
        ["calculator"] = "calc.exe",
        ["explorer"] = "explorer.exe",
        ["terminal"] = "wt.exe",
        ["paint"] = "mspaint.exe",
        ["taskmanager"] = "taskmgr.exe",
        ["settings"] = "ms-settings:",
        ["browser"] = "https://www.google.com",
        ["snipping"] = "ms-screenclip:",
    };

    public static string NotesFile => Path.Combine(AppSettings.Folder, "notes.md");

    /// <summary>A timer finished: (label).</summary>
    public event Action<string>? TimerFired;

    /// <summary>The glitch overlay is UI work, so the window owner provides it.</summary>
    public Action? ShowGlitch { get; set; }

    /// <summary>Clipboard writes by Capri herself should not trigger her clipboard reaction.</summary>
    public string? LastWrittenClipboard { get; private set; }

    public Task<ActionResult> OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return Fail("Это не похоже на веб-адрес.");
        return Shell(uri.AbsoluteUri, $"Opened {uri.Host}");
    }

    public Task<ActionResult> WebSearch(string query) =>
        string.IsNullOrWhiteSpace(query)
            ? Fail("Пустой запрос.")
            : Shell("https://www.google.com/search?q=" + Uri.EscapeDataString(query), $"Searching for: {query}");

    public async Task<ActionResult> LaunchApp(string app)
    {
        if (!AppCommands.TryGetValue(app, out var command)) return ActionResult.Fail($"Unknown app {app}");
        var result = await Shell(command, $"Launched {app}");
        // Windows Terminal is optional; fall back to the classic console.
        return !result.Ok && app == "terminal" ? await Shell("cmd.exe", "Launched cmd") : result;
    }

    public Task<ActionResult> OpenFolder(string folder)
    {
        var path = folder switch
        {
            "desktop" => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "downloads" => Native.Downloads(),
            "documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "pictures" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "music" => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            "videos" => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            _ => null,
        };
        return path is null ? Fail($"Не знаю папку {folder}.") : Shell(path, $"Opened {path}");
    }

    public Task<ActionResult> TakeScreenshot() => Task.Run(() =>
    {
        try
        {
            var bounds = Forms.SystemInformation.VirtualScreen;
            using var bmp = new Drawing.Bitmap(bounds.Width, bounds.Height);
            using (var g = Drawing.Graphics.FromImage(bmp))
                g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);

            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Capricon");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            bmp.Save(file, Drawing.Imaging.ImageFormat.Png);
            Process.Start("explorer.exe", $"/select,\"{file}\"");
            return ActionResult.Done($"Saved {file}");
        }
        catch (Exception e)
        {
            return ActionResult.Fail(e.Message);
        }
    });

    public async Task<ActionResult> GetSystemInfo()
    {
        Native.CpuLoad();
        await Task.Delay(400);
        var cpu = Native.CpuLoad();
        var (used, total, percent) = Native.Memory();
        var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
        var power = Forms.SystemInformation.PowerStatus;
        var battery = power.BatteryChargeStatus.HasFlag(Forms.BatteryChargeStatus.NoSystemBattery)
            ? ""
            : $" Батарея {power.BatteryLifePercent * 100:0}%{(power.PowerLineStatus == Forms.PowerLineStatus.Online ? ", заряжается" : "")}.";
        return ActionResult.Done(string.Create(Ru,
            $"Процессор {cpu:0}%, память {used:0.0} из {total:0} ГБ ({percent}%).{battery} Без перезагрузки {(int)up.TotalHours} ч {up.Minutes} мин."));
    }

    public Task<ActionResult> SetTimer(TimeSpan delay, string label)
    {
        _ = Task.Delay(delay).ContinueWith(_ => TimerFired?.Invoke(label), TaskScheduler.Default);
        return Task.FromResult(ActionResult.Done($"Timer set for {delay.TotalSeconds:0} s"));
    }

    public async Task<ActionResult> AddNote(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return ActionResult.Fail("Пустая заметка.");
        Directory.CreateDirectory(AppSettings.Folder);
        await File.AppendAllTextAsync(NotesFile, $"- [{DateTime.Now:yyyy-MM-dd HH:mm}] {text.Trim()}{Environment.NewLine}");
        return ActionResult.Done("Note saved");
    }

    public async Task<ActionResult> ReadNotes()
    {
        if (!File.Exists(NotesFile)) return ActionResult.Done("Заметок пока нет.");
        var lines = (await File.ReadAllLinesAsync(NotesFile)).Where(l => l.Length > 0).TakeLast(5)
            .Select(l => l.Contains("] ") ? l[(l.IndexOf("] ", StringComparison.Ordinal) + 2)..] : l);
        var list = string.Join("; ", lines);
        return ActionResult.Done(list.Length == 0 ? "Заметок пока нет." : $"Последние заметки: {list}.");
    }

    public Task<ActionResult> ReadClipboard() => OnUi(() =>
        ActionResult.Done(System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : ""));

    public Task<ActionResult> WriteClipboard(string text) => OnUi(() =>
    {
        LastWrittenClipboard = text;
        System.Windows.Clipboard.SetText(text);
        return ActionResult.Done("Copied");
    });

    public Task<ActionResult> GlitchScreen() => OnUi(() =>
    {
        ShowGlitch?.Invoke();
        return ActionResult.Done("Glitch played");
    });

    public Task<ActionResult> Media(MediaKey key)
    {
        var (vk, times) = key switch
        {
            MediaKey.Next => (Native.VK_MEDIA_NEXT, 1),
            MediaKey.Previous => (Native.VK_MEDIA_PREV, 1),
            MediaKey.VolumeUp => (Native.VK_VOLUME_UP, 5),
            MediaKey.VolumeDown => (Native.VK_VOLUME_DOWN, 5),
            MediaKey.Mute => (Native.VK_VOLUME_MUTE, 1),
            _ => (Native.VK_MEDIA_PLAY_PAUSE, 1),
        };
        for (var i = 0; i < times; i++) Native.PressKey(vk);
        return Task.FromResult(ActionResult.Done($"Pressed {key}"));
    }

    public Task<ActionResult> ShowDesktop() => OnUi(() =>
    {
        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType is null) return ActionResult.Fail("Shell недоступен.");
        var shell = Activator.CreateInstance(shellType);
        shellType.InvokeMember("ToggleDesktop", System.Reflection.BindingFlags.InvokeMethod, null, shell, null);
        return ActionResult.Done("Desktop toggled");
    });

    public Task<ActionResult> LockPc() =>
        Task.FromResult(Native.LockWorkStation() ? ActionResult.Done("Locked") : ActionResult.Fail("Не получилось заблокировать."));

    public Task<ActionResult> CursorDance() => Task.Run(async () =>
    {
        Native.GetCursorPos(out var start);
        for (var i = 0; i <= 60; i++)
        {
            var a = i / 60.0 * Math.PI * 4;
            var r = 70 * Math.Sin(i / 60.0 * Math.PI);
            Native.SetCursorPos(start.X + (int)(Math.Cos(a) * r), start.Y + (int)(Math.Sin(a) * r));
            await Task.Delay(30);
        }
        Native.SetCursorPos(start.X, start.Y);
        return ActionResult.Done("Cursor danced");
    });

    static Task<ActionResult> Shell(string target, string details)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return Task.FromResult(ActionResult.Done(details));
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return Task.FromResult(ActionResult.Fail(e.Message));
        }
    }

    static Task<ActionResult> Fail(string message) => Task.FromResult(ActionResult.Fail(message));

    static Task<ActionResult> OnUi(Func<ActionResult> f)
    {
        try
        {
            return Task.FromResult(Application.Current.Dispatcher.Invoke(f));
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            return Task.FromResult(ActionResult.Fail(e.Message));
        }
    }
}
