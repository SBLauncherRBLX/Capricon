using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Rectangle = System.Windows.Shapes.Rectangle;
using System.Windows.Threading;

namespace Capricon;

/// <summary>
/// A two-second "hack" over the whole desktop: pastel pixel bursts, tear bands and scanlines.
/// Purely visual and click-through, so nothing underneath is touched.
/// </summary>
public sealed class GlitchOverlay : Window
{
    static readonly string[] Palette = ["#FF2D7E", "#1DFF86", "#5CFFFF", "#FFF84A", "#B2AEFF", "#FFAAA0", "#FF4FE8", "#0A0A12"];

    readonly Canvas _canvas = new();
    readonly Random _rng = new();
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    int _tick;

    public GlitchOverlay()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        Content = _canvas;
        _timer.Tick += (_, _) => Step();
        SourceInitialized += (_, _) => MakeClickThrough();
        Loaded += (_, _) => _timer.Start();
    }

    public static void Play() => new GlitchOverlay().Show();

    void Step()
    {
        _tick++;
        _canvas.Children.Clear();
        if (_tick > 40)
        {
            _timer.Stop();
            Close();
            return;
        }

        // Intensity ramps up, peaks, then fades.
        var k = Math.Sin(Math.Min(1, _tick / 40.0) * Math.PI);
        var count = (int)(10 + 70 * k);
        for (var i = 0; i < count; i++)
        {
            var w = 20 + _rng.NextDouble() * 260 * k;
            var h = 6 + _rng.NextDouble() * 40;
            Add(new Rectangle { Width = w, Height = h, Fill = Brush(Palette[_rng.Next(Palette.Length)]), Opacity = 0.55 + _rng.NextDouble() * 0.4 },
                _rng.NextDouble() * (Width - w), _rng.NextDouble() * (Height - h));
        }

        // Full-width tear bands.
        for (var i = 0; i < (int)(5 * k); i++)
            Add(new Rectangle { Width = Width, Height = 2 + _rng.NextDouble() * 14, Fill = Brush(Palette[_rng.Next(Palette.Length)]), Opacity = 0.35 },
                0, _rng.NextDouble() * Height);

        // Scanlines.
        if (_tick % 2 == 0)
            for (double y = _rng.Next(4); y < Height; y += 6)
                Add(new Rectangle { Width = Width, Height = 1, Fill = Brushes.Black, Opacity = 0.12 * k }, 0, y);
    }

    void Add(UIElement e, double x, double y)
    {
        Canvas.SetLeft(e, x);
        Canvas.SetTop(e, y);
        _canvas.Children.Add(e);
    }

    static Brush Brush(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

    const int GWL_EXSTYLE = -20, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll")]
    static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    void MakeClickThrough()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
    }
}
