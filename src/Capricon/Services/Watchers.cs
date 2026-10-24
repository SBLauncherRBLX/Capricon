using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Capricon.Services;

/// <summary>What Capri notices on her own: you leaving, the CPU melting, the battery dying.</summary>
public sealed class Watchers
{
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };
    readonly AppSettings _settings;
    readonly Random _rng = new();

    bool _away;
    int _hotSamples;
    DateTime _lastCpuWarning = DateTime.MinValue;
    bool _batteryWarned;
    bool? _onAc;
    DateTime _nextChatter;

    public Watchers(AppSettings settings)
    {
        _settings = settings;
        _nextChatter = DateTime.Now.AddMinutes(8 + _rng.Next(8));
        _timer.Tick += (_, _) => Tick();
        Native.CpuLoad();
    }

    /// <summary>Reaction key from <see cref="Core.Persona"/>: "idle", "back", "cpu", "battery", "charging", "chatter".</summary>
    public event Action<string>? Noticed;

    public void Start() => _timer.Start();

    void Tick()
    {
        var idle = Native.IdleTime();
        if (_settings.ReactIdle)
        {
            if (!_away && idle > TimeSpan.FromMinutes(5)) { _away = true; Noticed?.Invoke("idle"); }
            else if (_away && idle < TimeSpan.FromSeconds(10)) { _away = false; Noticed?.Invoke("back"); }
        }

        if (_settings.ReactSystem)
        {
            _hotSamples = Native.CpuLoad() > 90 ? _hotSamples + 1 : 0;
            if (_hotSamples >= 3 && DateTime.Now - _lastCpuWarning > TimeSpan.FromMinutes(10))
            {
                _lastCpuWarning = DateTime.Now;
                Noticed?.Invoke("cpu");
            }

            var power = Forms.SystemInformation.PowerStatus;
            if (!power.BatteryChargeStatus.HasFlag(Forms.BatteryChargeStatus.NoSystemBattery))
            {
                var onAc = power.PowerLineStatus == Forms.PowerLineStatus.Online;
                if (_onAc == false && onAc) { _batteryWarned = false; Noticed?.Invoke("charging"); }
                _onAc = onAc;
                if (!onAc && power.BatteryLifePercent < 0.2f && !_batteryWarned)
                {
                    _batteryWarned = true;
                    Noticed?.Invoke("battery");
                }
            }
        }

        if (_settings.Chatter && !_away && DateTime.Now >= _nextChatter)
        {
            _nextChatter = DateTime.Now.AddMinutes(10 + _rng.Next(15));
            Noticed?.Invoke("chatter");
        }
    }
}
