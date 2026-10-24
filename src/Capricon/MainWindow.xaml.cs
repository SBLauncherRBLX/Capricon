using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Capricon.Core;
using Capricon.Services;

namespace Capricon;

/// <summary>
/// Capri on the desktop: the avatar window plus everything she reacts to (clicks, drags,
/// chat, hotkeys, clipboard, timers, system watchers).
/// </summary>
public partial class MainWindow : Window
{
    const int HotkeyChat = 1, HotkeyHide = 2;

    readonly AppSettings _settings;
    readonly WindowsPc _pc = new();
    readonly CapriBrain _brain;
    readonly Voice _voice = new();
    readonly Watchers _watchers;
    readonly DispatcherTimer _bubbleTimer = new();
    readonly DispatcherTimer _typeTimer = new() { Interval = TimeSpan.FromMilliseconds(22) };
    readonly Random _rng = new();

    string _bubbleFull = "";
    int _bubbleShown;
    bool _sleeping;
    bool _busy;
    CancellationTokenSource? _askCts;

    // Drag state.
    Point _pressScreen;
    Point _pressWindow;
    bool _pressed;
    bool _dragging;
    DateTime _lastMove;
    double _lastLeft;

    DateTime _lastClipboardReaction = DateTime.MinValue;
    DispatcherTimer? _roam;
    readonly DispatcherTimer _pokeTimer = new() { Interval = TimeSpan.FromMilliseconds(System.Windows.Forms.SystemInformation.DoubleClickTime) };

    public MainWindow(AppSettings settings)
    {
        _settings = settings;
        _brain = new CapriBrain(_pc);
        _watchers = new Watchers(settings);
        InitializeComponent();

        Avatar.Scale = settings.Scale;
        ApplySettings();

        _pc.ShowGlitch = GlitchOverlay.Play;
        _pc.TimerFired += label => Dispatcher.Invoke(() =>
        {
            var line = Persona.Say("timer-done");
            Say(line with { Text = label.Length > 0 ? $"{line.Text} Ты просил напомнить: {label}." : line.Text });
            TimerNotification?.Invoke(label);
        });
        _watchers.Noticed += OnNoticed;

        _bubbleTimer.Tick += (_, _) => HideBubble();
        _pokeTimer.Tick += (_, _) => { _pokeTimer.Stop(); Poke(); };
        _typeTimer.Tick += (_, _) => TypeStep();
        Avatar.BeforeFrame += BeforeFrame;

        MouseLeftButtonDown += OnPress;
        MouseMove += OnDrag;
        MouseLeftButtonUp += OnRelease;
        MouseRightButtonUp += (_, _) => ShowMenu();
        MouseWheel += OnWheel;
        Deactivated += (_, _) => { if (ChatBox.Text.Length == 0) CloseChat(); };

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    /// <summary>Lets the tray show a balloon when a timer rings.</summary>
    public event Action<string>? TimerNotification;

    /// <summary>Opens the settings window (owned by the app so the tray can open it too).</summary>
    public event Action? SettingsRequested;

    public bool Muted => _settings.Muted;

    public void ApplySettings()
    {
        _brain.Configure(_settings.ApiKey, _settings.Model, _settings.Effort);
        _voice.Muted = _settings.Muted;
        _voice.VoiceName = _settings.VoiceName;
        _voice.Rate = _settings.VoiceRate;
        _voice.Pitch = _settings.VoicePitch;
        Avatar.Scale = _settings.Scale;
        Avatar.InvalidateMeasure();
    }

    public IEnumerable<string> Voices => _voice.InstalledVoices;

    // ------------------------------------------------------------------ lifecycle

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        var area = SystemParameters.WorkArea;
        Left = _settings.Left ?? area.Right - ActualWidth - 20;
        Top = _settings.Top ?? area.Bottom - ActualHeight;
        KeepOnScreen();
        _watchers.Start();

        Avatar.Animator.Glitch(0.6, 1.2);
        Avatar.Animator.Waving = true;
        Say(Persona.Greeting(DateTime.Now));
        Delay(3, () => Avatar.Animator.Waving = false);
    }

    void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(hwnd)!.AddHook(WndProc);
        Native.AddClipboardFormatListener(hwnd);
        Native.RegisterHotKey(hwnd, HotkeyChat, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, (uint)KeyInterop.VirtualKeyFromKey(Key.C));
        Native.RegisterHotKey(hwnd, HotkeyHide, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, (uint)KeyInterop.VirtualKeyFromKey(Key.H));
    }

    void OnClosed(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        Native.RemoveClipboardFormatListener(hwnd);
        Native.UnregisterHotKey(hwnd, HotkeyChat);
        Native.UnregisterHotKey(hwnd, HotkeyHide);
        _settings.Left = Left;
        _settings.Top = Top;
        _settings.Save();
        _voice.Dispose();
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY)
        {
            if (wParam.ToInt32() == HotkeyChat) ToggleChat();
            if (wParam.ToInt32() == HotkeyHide) ToggleVisible();
            handled = true;
        }
        else if (msg == Native.WM_CLIPBOARDUPDATE)
        {
            Dispatcher.BeginInvoke(OnClipboardChanged);
        }
        return IntPtr.Zero;
    }

    // ------------------------------------------------------------------ talking

    /// <summary>Show a reply: expression, glitch on mood change, speech bubble and voice.</summary>
    public void Say(Reply reply)
    {
        var anim = Avatar.Animator;
        if (anim.Mood != reply.Mood) anim.Glitch(0.18, 0.6);
        anim.Mood = reply.Mood;
        if (reply.Mood == Mood.Hack) anim.Glitch(0.4, 1);

        _bubbleFull = reply.Text;
        _bubbleShown = 0;
        BubbleText.Text = "";
        Bubble.Visibility = Visibility.Visible;
        _typeTimer.Start();
        _voice.Say(reply.Text);

        _bubbleTimer.Stop();
        _bubbleTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(3 + reply.Text.Length * 0.07, 4, 14));
        _bubbleTimer.Start();
    }

    void TypeStep()
    {
        // Typewriter with a glitchy "decoding" last character.
        _bubbleShown = Math.Min(_bubbleFull.Length, _bubbleShown + 2);
        var shown = _bubbleFull[.._bubbleShown];
        BubbleText.Text = _bubbleShown < _bubbleFull.Length ? shown + "▚" : shown;
        if (_bubbleShown >= _bubbleFull.Length) _typeTimer.Stop();
    }

    void HideBubble()
    {
        if (_voice.Speaking) return; // keep it while she is still talking
        _bubbleTimer.Stop();
        Bubble.Visibility = Visibility.Collapsed;
        if (!_busy) Avatar.Animator.Mood = _sleeping ? Mood.Sleepy : Mood.Idle;
    }

    async Task Ask(string text)
    {
        _askCts?.Cancel();
        _askCts = new CancellationTokenSource();
        var ct = _askCts.Token;
        _busy = true;
        Wake(silent: true);
        Avatar.Animator.Mood = Mood.Think;
        if (_brain.HasAi && CommandParser.Parse(text) is null)
        {
            _bubbleTimer.Stop();
            BubbleText.Text = "…";
            Bubble.Visibility = Visibility.Visible;
        }

        try
        {
            var reply = await _brain.Handle(text, ct);
            if (ct.IsCancellationRequested) return;
            _busy = false;
            Say(reply);
            HandleSignal(reply.Signal);
        }
        catch (OperationCanceledException)
        {
            // A newer question replaced this one.
        }
        catch (Exception ex)
        {
            _busy = false;
            Say(new Reply($"Ой, сбой в матрице: {ex.Message}", Mood.Sad));
        }
    }

    void HandleSignal(AppSignal signal)
    {
        switch (signal)
        {
            case AppSignal.Sleep:
                _sleeping = true;
                break;
            case AppSignal.Wake:
                _sleeping = false;
                break;
            case AppSignal.Mute:
                SetMuted(true);
                break;
            case AppSignal.Unmute:
                SetMuted(false);
                break;
            case AppSignal.OpenSettings:
                SettingsRequested?.Invoke();
                break;
            case AppSignal.Roam:
                Roam();
                break;
            case AppSignal.Quit:
                Delay(2.5, () => Application.Current.Shutdown());
                break;
        }
    }

    public void SetMuted(bool muted)
    {
        _settings.Muted = muted;
        _voice.Muted = muted;
        if (muted) _voice.Stop();
        _settings.Save();
    }

    void Wake(bool silent = false)
    {
        if (!_sleeping) return;
        _sleeping = false;
        if (!silent) Say(Persona.Say("wake"));
    }

    void OnNoticed(string key)
    {
        if (_busy || _voice.Speaking) return;
        switch (key)
        {
            case "back":
                if (_sleeping) { _sleeping = false; Say(Persona.Say("back")); }
                break;
            case "idle":
                Say(Persona.Say("idle"));
                _sleeping = true;
                break;
            default:
                if (!_sleeping) Say(Persona.Say(key));
                break;
        }
    }

    void OnClipboardChanged()
    {
        if (!_settings.ReactClipboard || _sleeping || _busy || _voice.Speaking) return;
        if (DateTime.Now - _lastClipboardReaction < TimeSpan.FromSeconds(45) || _rng.NextDouble() > 0.35) return;
        string text;
        try
        {
            if (!Clipboard.ContainsText()) return;
            text = Clipboard.GetText();
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return; // another app holds the clipboard
        }
        if (text == _pc.LastWrittenClipboard) return;
        _lastClipboardReaction = DateTime.Now;
        Say(Persona.Say(text.TrimStart().StartsWith("http", StringComparison.OrdinalIgnoreCase) ? "clip-link" : "clip-text"));
    }

    // ------------------------------------------------------------------ animation feed

    void BeforeFrame()
    {
        var anim = Avatar.Animator;
        anim.TalkLevel = _voice.Level;
        if (_settings.FollowCursor && PresentationSource.FromVisual(this) is not null && Native.GetCursorPos(out var p))
        {
            var local = PointFromScreen(new Point(p.X, p.Y));
            anim.LookAt = (local.X / Avatar.Scale, local.Y / Avatar.Scale);
        }
        else
        {
            anim.LookAt = null;
        }

        if (_dragging && (DateTime.Now - _lastMove).TotalMilliseconds > 120) anim.DragVelocity *= 0.8;
    }

    // ------------------------------------------------------------------ mouse

    void OnPress(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is TextBox || IsInside(Chat, e)) return;
        if (e.ClickCount == 2)
        {
            _pokeTimer.Stop();
            OpenChat();
            return;
        }
        _pressed = true;
        _dragging = false;
        _pressScreen = PointToScreen(e.GetPosition(this));
        _pressWindow = new Point(Left, Top);
        _lastLeft = Left;
        _lastMove = DateTime.Now;
        CaptureMouse();
    }

    void OnDrag(object sender, MouseEventArgs e)
    {
        if (!_pressed) return;
        var now = PointToScreen(e.GetPosition(this));
        var source = PresentationSource.FromVisual(this);
        var toDip = source?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
        var delta = toDip.Transform(new Point(now.X - _pressScreen.X, now.Y - _pressScreen.Y));
        if (!_dragging && Math.Abs(delta.X) + Math.Abs(delta.Y) < 5) return;

        if (!_dragging)
        {
            _dragging = true;
            Avatar.Animator.Dragging = true;
            Avatar.Animator.Mood = Mood.Surprised;
            if (_rng.NextDouble() < 0.5) Say(Persona.Say("drag"));
        }
        Left = _pressWindow.X + delta.X;
        Top = _pressWindow.Y + delta.Y;

        var dt = Math.Max(0.016, (DateTime.Now - _lastMove).TotalSeconds);
        Avatar.Animator.DragVelocity = (Left - _lastLeft) / dt;
        _lastLeft = Left;
        _lastMove = DateTime.Now;
    }

    void OnRelease(object sender, MouseButtonEventArgs e)
    {
        if (!_pressed) return;
        _pressed = false;
        ReleaseMouseCapture();
        if (_dragging)
        {
            _dragging = false;
            Avatar.Animator.Dragging = false;
            Avatar.Animator.DragVelocity = 0;
            Avatar.Animator.Mood = _sleeping ? Mood.Sleepy : Mood.Idle;
            KeepOnScreen();
            _settings.Left = Left;
            _settings.Top = Top;
            _settings.Save();
            return;
        }
        // Wait out a possible double-click before reacting to a single poke.
        _pokeTimer.Start();
    }

    void Poke()
    {
        if (_sleeping)
        {
            Wake();
            return;
        }
        Avatar.Animator.Glitch(0.3, 0.9);
        Say(Persona.Say("poke"));
    }

    void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        _settings.Scale = Math.Clamp(_settings.Scale + Math.Sign(e.Delta) * 0.05, 0.4, 2);
        Avatar.Scale = _settings.Scale;
        Avatar.InvalidateMeasure();
        _settings.Save();
    }

    static bool IsInside(FrameworkElement element, MouseEventArgs e)
    {
        if (element.Visibility != Visibility.Visible) return false;
        var p = e.GetPosition(element);
        return p.X >= 0 && p.Y >= 0 && p.X <= element.ActualWidth && p.Y <= element.ActualHeight;
    }

    // ------------------------------------------------------------------ chat

    public void ToggleChat()
    {
        if (Chat.Visibility == Visibility.Visible) CloseChat();
        else OpenChat();
    }

    public void OpenChat()
    {
        if (Visibility != Visibility.Visible) Show();
        Chat.Visibility = Visibility.Visible;
        Activate();
        ChatBox.Focus();
        Keyboard.Focus(ChatBox);
        Avatar.Animator.LookAt = null;
    }

    void CloseChat()
    {
        Chat.Visibility = Visibility.Collapsed;
        ChatBox.Text = "";
    }

    async void ChatBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseChat();
            return;
        }
        if (e.Key != Key.Enter) return;
        var text = ChatBox.Text.Trim();
        ChatBox.Text = "";
        if (text.Length == 0) return;
        await Ask(text);
    }

    void ChatBox_TextChanged(object sender, TextChangedEventArgs e) =>
        ChatHint.Visibility = ChatBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    // ------------------------------------------------------------------ menu & window

    void ShowMenu()
    {
        var menu = new ContextMenu();
        void Item(string header, Action action, bool? check = null)
        {
            var item = new MenuItem { Header = header, IsCheckable = check.HasValue, IsChecked = check ?? false };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        Item("Поговорить  (Ctrl+Alt+C)", OpenChat);
        var moods = new MenuItem { Header = "Эмоции" };
        foreach (var mood in Enum.GetValues<Mood>())
        {
            var m = new MenuItem { Header = mood.ToString() };
            m.Click += (_, _) => { Avatar.Animator.Mood = mood; Avatar.Animator.Glitch(0.2, 0.6); };
            moods.Items.Add(m);
        }
        menu.Items.Add(moods);
        Item("Взломать экран", () => _ = Ask("взломай экран"));
        Item("Скриншот", () => _ = Ask("скриншот"));
        Item("Погулять", Roam);
        Item("Шутку!", () => _ = Ask("расскажи шутку"));
        menu.Items.Add(new Separator());
        Item("Без голоса", () => SetMuted(!_settings.Muted), _settings.Muted);
        Item("Настройки…", () => SettingsRequested?.Invoke());
        Item("Спрятать  (Ctrl+Alt+H)", ToggleVisible);
        Item("Выход", () => Application.Current.Shutdown());
        menu.IsOpen = true;
    }

    public void ToggleVisible()
    {
        if (IsVisible) Hide();
        else
        {
            Show();
            Avatar.Animator.Glitch(0.5, 1);
        }
    }

    void KeepOnScreen()
    {
        var left = SystemParameters.VirtualScreenLeft;
        var top = SystemParameters.VirtualScreenTop;
        var right = left + SystemParameters.VirtualScreenWidth;
        var bottom = top + SystemParameters.VirtualScreenHeight;
        Left = Math.Clamp(Left, left - ActualWidth * 0.6, right - ActualWidth * 0.4);
        Top = Math.Clamp(Top, top - ActualHeight * 0.3, bottom - ActualHeight * 0.4);
    }

    /// <summary>She drifts to another spot along the bottom of the screen, mane streaming.</summary>
    void Roam()
    {
        _roam?.Stop();
        var area = SystemParameters.WorkArea;
        var from = Left;
        var to = area.Left + _rng.NextDouble() * Math.Max(1, area.Width - ActualWidth);
        var start = DateTime.Now;
        var duration = Math.Clamp(Math.Abs(to - from) / 300, 1.2, 4);
        Avatar.Animator.Dragging = true;
        _roam = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _roam.Tick += (_, _) =>
        {
            var k = Math.Min(1, (DateTime.Now - start).TotalSeconds / duration);
            var eased = k < 0.5 ? 2 * k * k : 1 - Math.Pow(-2 * k + 2, 2) / 2;
            var prev = Left;
            Left = from + (to - from) * eased;
            Avatar.Animator.DragVelocity = (Left - prev) / 0.016;
            if (k >= 1)
            {
                _roam!.Stop();
                Avatar.Animator.Dragging = false;
                Avatar.Animator.DragVelocity = 0;
                _settings.Left = Left;
                _settings.Save();
            }
        };
        _roam.Start();
    }

    void Delay(double seconds, Action action)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            action();
        };
        timer.Start();
    }
}
