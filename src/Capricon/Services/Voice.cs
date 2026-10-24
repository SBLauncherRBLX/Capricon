using System.Globalization;
using System.Security;
using System.Speech.Synthesis;

namespace Capricon.Services;

/// <summary>
/// Capri's voice through the Windows speech engine. Visemes (mouth shapes the engine reports
/// while speaking) drive the avatar's lip-sync.
/// </summary>
public sealed class Voice : IDisposable
{
    readonly SpeechSynthesizer _synth = new();
    Prompt? _current;
    double _level;
    DateTime _speakingUntil;

    public Voice()
    {
        _synth.SetOutputToDefaultAudioDevice();
        _synth.VisemeReached += (_, e) => _level = VisemeOpenness(e.Viseme);
        _synth.SpeakProgress += (_, _) => _level = Math.Max(_level, 0.5);
        _synth.SpeakCompleted += (_, e) =>
        {
            // Cancelling the previous line also completes it; only the current line counts.
            if (e.Prompt != _current) return;
            _level = 0;
            Speaking = false;
        };
    }

    public bool Muted { get; set; }
    public int Rate { get; set; } = 1;

    /// <summary>Pitch shift in percent; Capri sits a bit higher than the stock voices.</summary>
    public int Pitch { get; set; } = 25;

    public string VoiceName { get; set; } = "";

    public bool Speaking { get; private set; }

    public IEnumerable<string> InstalledVoices =>
        _synth.GetInstalledVoices().Where(v => v.Enabled).Select(v => v.VoiceInfo.Name);

    /// <summary>Current mouth openness 0..1 for the animator; decays between visemes.</summary>
    public double Level
    {
        get
        {
            if (Muted && DateTime.Now < _speakingUntil)
            {
                // No audio: still flap the mouth for the length of the text.
                var t = DateTime.Now.TimeOfDay.TotalSeconds;
                return Math.Max(0, Math.Sin(t * 17) * 0.6 + Math.Sin(t * 5.3) * 0.4);
            }
            _level *= 0.9;
            return _level;
        }
    }

    public void Say(string text)
    {
        Stop();
        if (string.IsNullOrWhiteSpace(text)) return;
        if (Muted)
        {
            _speakingUntil = DateTime.Now.AddSeconds(Math.Clamp(text.Length * 0.06, 0.8, 8));
            return;
        }

        var voice = PickVoice(text);
        var culture = voice?.Culture.Name ?? (IsCyrillic(text) ? "ru-RU" : "en-US");
        var voiceTag = voice is null ? "" : $"<voice name=\"{SecurityElement.Escape(voice.Name)}\">";
        var ssml =
            $"<speak version=\"1.0\" xmlns=\"http://www.w3.org/2001/10/synthesis\" xml:lang=\"{culture}\">" +
            voiceTag +
            string.Create(CultureInfo.InvariantCulture, $"<prosody pitch=\"{Pitch:+0;-0}%\" rate=\"{1 + Rate * 0.08:0.00}\">") +
            SecurityElement.Escape(text) +
            "</prosody>" + (voice is null ? "" : "</voice>") + "</speak>";
        try
        {
            Speaking = true;
            _current = _synth.SpeakSsmlAsync(ssml);
        }
        catch (Exception e) when (e is FormatException or InvalidOperationException or ArgumentException)
        {
            // A voice that rejects SSML: fall back to plain text.
            Speaking = true;
            _current = _synth.SpeakAsync(text);
        }
    }

    public void Stop()
    {
        _synth.SpeakAsyncCancelAll();
        _speakingUntil = DateTime.MinValue;
        _level = 0;
        Speaking = false;
    }

    VoiceInfo? PickVoice(string text)
    {
        var voices = _synth.GetInstalledVoices().Where(v => v.Enabled).Select(v => v.VoiceInfo).ToList();
        if (VoiceName.Length > 0 && voices.FirstOrDefault(v => v.Name == VoiceName) is { } chosen) return chosen;
        var lang = IsCyrillic(text) ? "ru" : "en";
        return voices.FirstOrDefault(v => v.Culture.TwoLetterISOLanguageName == lang && v.Gender == VoiceGender.Female)
               ?? voices.FirstOrDefault(v => v.Culture.TwoLetterISOLanguageName == lang)
               ?? voices.FirstOrDefault(v => v.Gender == VoiceGender.Female);
    }

    static bool IsCyrillic(string text) => text.Any(c => c is >= 'А' and <= 'я' or 'Ё' or 'ё');

    /// <summary>SAPI viseme ids 0..21 mapped to how open the mouth is.</summary>
    static double VisemeOpenness(int viseme) => viseme switch
    {
        0 => 0.05,                       // silence
        21 => 0.1,                       // p, b, m
        18 or 19 or 20 => 0.35,          // f, v / th / t, d, n
        1 or 2 or 11 => 1.0,             // a, ah
        3 or 9 or 10 or 13 => 0.85,      // o, ow, aw
        4 or 5 or 6 or 12 => 0.7,        // e, i
        7 or 8 => 0.55,                  // u, w
        _ => 0.45,
    };

    public void Dispose() => _synth.Dispose();
}
