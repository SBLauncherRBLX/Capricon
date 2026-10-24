namespace Capricon.Core;

/// <summary>
/// Routes what the user says: clear PC commands run locally and instantly (offline, free),
/// everything else goes to Claude when an API key is configured.
/// </summary>
public sealed class CapriBrain(IPcActions pc)
{
    readonly LocalBrain _local = new(pc);
    ClaudeBrain? _claude;

    public bool HasAi => _claude is not null;

    public void Configure(string? apiKey, string model, string effort)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _claude = null;
            return;
        }
        _claude = new ClaudeBrain(apiKey.Trim(), pc) { Model = model, Effort = effort };
    }

    public void Reset() => _claude?.Reset();

    public async Task<Reply> Handle(string text, CancellationToken ct = default)
    {
        var intent = CommandParser.Parse(text);

        // Small talk is more fun with the AI; actions stay local so they are instant.
        var chatty = intent?.Kind is IntentKind.Greet or IntentKind.Thanks or IntentKind.Joke or IntentKind.WhoAreYou;
        if (intent is not null && !(chatty && _claude is not null))
        {
            var reply = await _local.Handle(intent);
            if (reply.Signal == AppSignal.ResetConversation) Reset();
            return reply;
        }

        if (_claude is not null) return await _claude.Ask(text, DateTime.Now, ct);
        return text.Trim().Length > 25 ? Persona.Say("no-brain") : Persona.Say("unknown");
    }
}
