using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;

namespace Capricon.Core;

/// <summary>
/// Free-form conversation through the Claude API. Claude gets Capri's persona plus a tool for
/// every <see cref="IPcActions"/> method, so "включи что-нибудь весёлое на ютубе" actually happens.
/// </summary>
public sealed class ClaudeBrain
{
    const int MaxToolRounds = 8;
    // Long chats are restarted rather than trimmed: history stays append-only, which keeps
    // prompt caching and thinking-block replay valid.
    const int MaxHistory = 60;

    readonly AnthropicClient _client;
    readonly IPcActions _pc;
    readonly List<BetaMessageParam> _history = [];
    readonly SemaphoreSlim _gate = new(1, 1);

    public ClaudeBrain(string apiKey, IPcActions pc)
        : this(new AnthropicClient { ApiKey = apiKey }, pc)
    {
    }

    public ClaudeBrain(AnthropicClient client, IPcActions pc)
    {
        _client = client;
        _pc = pc;
    }

    public string Model { get; set; } = "claude-opus-5-5";

    /// <summary>"low" keeps chat snappy and cheap; raise it in settings for smarter answers.</summary>
    public string Effort { get; set; } = "low";

    public void Reset() => _history.Clear();

    public async Task<Reply> Ask(string userText, DateTime now, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_history.Count > MaxHistory) _history.Clear();
            _history.Add(new BetaUserMessageParam { Content = $"[{now:dddd, yyyy-MM-dd HH:mm}] {userText}" });

            for (var round = 0; round < MaxToolRounds; round++)
            {
                var response = await _client.Beta.Messages.Create(new MessageCreateParams
                {
                    Model = Model,
                    MaxTokens = 16000,
                    Betas = ["server-side-fallback-2026-07-01"],
                    Fallbacks = new Default(),
                    System = Persona.SystemPrompt,
                    Tools = Tools,
                    Thinking = new BetaThinkingConfigAdaptive(),
                    OutputConfig = new BetaOutputConfig { Effort = ParseEffort(Effort) },
                    CacheControl = new BetaCacheControlEphemeral(),
                    Messages = [.. _history],
                }, ct);

                if (response.StopReason == BetaStopReason.Refusal)
                {
                    _history.Clear();
                    return new("Не-а, это даже для меня слишком. Давай что-нибудь другое.", Mood.Angry);
                }

                var assistant = new List<BetaContentBlockParam>();
                var calls = new List<BetaToolUseBlock>();
                var text = "";
                foreach (var block in response.Content)
                {
                    if (block.TryPickText(out var t))
                    {
                        assistant.Add(new BetaTextBlockParam { Text = t.Text });
                        text += t.Text;
                    }
                    else if (block.TryPickThinking(out var th))
                        assistant.Add(new BetaThinkingBlockParam { Thinking = th.Thinking, Signature = th.Signature });
                    else if (block.TryPickRedactedThinking(out var rt))
                        assistant.Add(new BetaRedactedThinkingBlockParam { Data = rt.Data });
                    else if (block.TryPickToolUse(out var tu))
                    {
                        assistant.Add(new BetaToolUseBlockParam { ID = tu.ID, Name = tu.Name, Input = tu.Input });
                        calls.Add(tu);
                    }
                }
                _history.Add(new BetaAssistantMessageParam { Content = assistant });

                if (calls.Count == 0 || response.StopReason != BetaStopReason.ToolUse)
                {
                    var (mood, spoken) = MoodTag.Split(text);
                    return new(spoken.Length > 0 ? spoken : Persona.Line("ok"), mood);
                }

                var results = new List<BetaContentBlockParam>();
                foreach (var call in calls)
                {
                    var r = await RunTool(call.Name, call.Input);
                    results.Add(new BetaToolResultBlockParam { ToolUseID = call.ID, Content = r.Details, IsError = !r.Ok });
                }
                _history.Add(new BetaUserMessageParam { Content = results });
            }

            _history.Clear();
            return new("Я запуталась в собственных щупальцах. Давай ещё раз?", Mood.Sad);
        }
        catch (AnthropicUnauthorizedException)
        {
            _history.Clear();
            return new("Ключ API не подходит. Проверь его в настройках, чумба.", Mood.Sad);
        }
        catch (AnthropicRateLimitException)
        {
            _history.Clear();
            return new("Слишком много болтаем — сервер просит паузу. Подожди минутку.", Mood.Sleepy);
        }
        catch (AnthropicIOException)
        {
            _history.Clear();
            return new("Нет связи с сетью. Я отрезана от облака!", Mood.Sad);
        }
        catch (AnthropicApiException e)
        {
            _history.Clear();
            return new($"Сервер ругается: {e.Message}", Mood.Angry);
        }
        finally
        {
            _gate.Release();
        }
    }

    async Task<ActionResult> RunTool(string name, IReadOnlyDictionary<string, JsonElement> input)
    {
        string Str(string key) =>
            input.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

        try
        {
            return name switch
            {
                "open_url" => await _pc.OpenUrl(Str("url")),
                "web_search" => await _pc.WebSearch(Str("query")),
                "launch_app" => await _pc.LaunchApp(Str("app")),
                "open_folder" => await _pc.OpenFolder(Str("folder")),
                "take_screenshot" => await _pc.TakeScreenshot(),
                "system_info" => await _pc.GetSystemInfo(),
                "set_timer" => input.TryGetValue("seconds", out var s) && s.TryGetDouble(out var sec) && sec > 0
                    ? await _pc.SetTimer(TimeSpan.FromSeconds(sec), Str("label"))
                    : ActionResult.Fail("seconds must be a positive number"),
                "add_note" => await _pc.AddNote(Str("text")),
                "read_notes" => await _pc.ReadNotes(),
                "read_clipboard" => await _pc.ReadClipboard(),
                "write_clipboard" => await _pc.WriteClipboard(Str("text")),
                "glitch_screen" => await _pc.GlitchScreen(),
                "media_key" => Enum.TryParse<MediaKey>(Str("key"), ignoreCase: true, out var key)
                    ? await _pc.Media(key)
                    : ActionResult.Fail("unknown media key"),
                "show_desktop" => await _pc.ShowDesktop(),
                "lock_pc" => await _pc.LockPc(),
                "cursor_dance" => await _pc.CursorDance(),
                _ => ActionResult.Fail($"unknown tool {name}"),
            };
        }
        catch (Exception e)
        {
            return ActionResult.Fail(e.Message);
        }
    }

    static Anthropic.Models.Beta.Messages.Effort ParseEffort(string effort) => effort switch
    {
        "medium" => Anthropic.Models.Beta.Messages.Effort.Medium,
        "high" => Anthropic.Models.Beta.Messages.Effort.High,
        _ => Anthropic.Models.Beta.Messages.Effort.Low,
    };

    static BetaTool Tool(string name, string description, object? properties = null, params string[] required) => new()
    {
        Name = name,
        Description = description,
        InputSchema = new()
        {
            Properties = properties is null
                ? new Dictionary<string, JsonElement>()
                : JsonSerializer.SerializeToElement(properties).EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone()),
            Required = required,
        },
    };

    static object Text(string description) => new { type = "string", description };

    static readonly BetaToolUnion[] Tools =
    [
        Tool("open_url", "Open a web page in the user's default browser.", new { url = Text("Full http(s) URL") }, "url"),
        Tool("web_search", "Open a web search for the query in the browser.", new { query = Text("Search query") }, "query"),
        Tool("launch_app", "Launch a built-in program.",
            new { app = new { type = "string", @enum = IPcActions.Apps } }, "app"),
        Tool("open_folder", "Open one of the user's standard folders in Explorer.",
            new { folder = new { type = "string", @enum = IPcActions.Folders } }, "folder"),
        Tool("take_screenshot", "Capture the whole screen to a PNG in Pictures/Capricon and show it."),
        Tool("system_info", "Get CPU load, RAM usage, battery and uptime."),
        Tool("set_timer", "Start a countdown; Capri announces it when done.",
            new { seconds = new { type = "number", description = "Delay in seconds" }, label = Text("What to remind about, may be empty") },
            "seconds", "label"),
        Tool("add_note", "Append a line to the user's notes file.", new { text = Text("Note text") }, "text"),
        Tool("read_notes", "Read the user's latest notes."),
        Tool("read_clipboard", "Read the text currently in the clipboard."),
        Tool("write_clipboard", "Put text into the clipboard.", new { text = Text("Text to copy") }, "text"),
        Tool("glitch_screen", "Play a harmless 2-second glitch-art animation over the whole screen."),
        Tool("media_key", "Press a media key.",
            new { key = new { type = "string", @enum = Enum.GetNames<MediaKey>() } }, "key"),
        Tool("show_desktop", "Minimise all windows (Win+D)."),
        Tool("lock_pc", "Lock the Windows session. Only when the user explicitly asks."),
        Tool("cursor_dance", "Playfully spin the mouse cursor in a circle for two seconds."),
    ];
}
