using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Mhodume;

/// <summary>
/// Keeps the window over the game and puts it on a key.
///
/// The awkward part of an overlay is not staying on top - that is one property
/// - it is focus. A window you can click is a window that takes the keyboard,
/// and a game that loses the keyboard stops responding to it. So the window is
/// hidden by default and the key both shows it and takes focus deliberately;
/// pressing the key again hides it and hands focus back to whatever had it,
/// which is the game.
///
/// Remembering which window that was is why the foreground window is captured
/// on the way in rather than guessed on the way out: by then ours is in front,
/// and the game is one of however many windows behind it.
///
/// The key is registered with Windows rather than watched by WPF, because WPF
/// only sees keys while it has focus - and while the game has focus is exactly
/// when this has to work.
/// </summary>
public sealed class Overlay
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 0xB0B;

    [Flags]
    private enum Mod : uint
    {
        Alt = 0x1,
        Control = 0x2,
        Shift = 0x4,
        Win = 0x8,
        NoRepeat = 0x4000,
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_LAYERED = 0x80000;
    private const uint LWA_ALPHA = 0x2;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int index, int value);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetLayeredWindowAttributes(
        IntPtr hWnd, uint key, byte alpha, uint flags);

    private readonly Window _window;
    private IntPtr _handle;
    private IntPtr _previous;
    private bool _registered;
    private double _opacity = 1.0;
    private DispatcherTimer? _foreground;

    /// <summary>
    /// The game whose foreground the overlay is allowed to sit over. While
    /// anything else is in front — another app you alt-tabbed to — the overlay
    /// hides, so it never floats over things it has nothing to do with.
    /// </summary>
    public string GameProcessName { get; set; } = "VHOLUME-Win64-Shipping";

    /// <summary>What the key was, so the app can say so when it could not take it.</summary>
    public string KeyName { get; private set; } = "";

    /// <summary>Null while all is well; the reason the key is unavailable otherwise.</summary>
    public string? Problem { get; private set; }

    public Overlay(Window window) => _window = window;

    /// <summary>
    /// Starts listening. Call once the window has a handle - on SourceInitialized
    /// or later - because a hotkey is registered against a handle.
    /// </summary>
    public void Attach(ModifierKeys modifiers, Key key)
    {
        _handle = new WindowInteropHelper(_window).Handle;
        HwndSource.FromHwnd(_handle)?.AddHook(OnMessage);

        KeyName = Describe(modifiers, key);
        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        _registered = RegisterHotKey(_handle, HOTKEY_ID, (uint)(Translate(modifiers) | Mod.NoRepeat), vk);

        if (!_registered)
        {
            // Almost always another program holding the same combination. Said
            // rather than swallowed: an overlay whose key does nothing is
            // indistinguishable from an overlay that did not start.
            Problem = $"{KeyName} is already taken by another program";
        }

        _window.Closing += (_, _) =>
        {
            if (_registered) UnregisterHotKey(_handle, HOTKEY_ID);
            _foreground?.Stop();
        };

        // Watch who is in front. Topmost alone keeps the window over every
        // application, not just the game, so an alt-tab to a browser or Discord
        // leaves it hanging over them. This hides it whenever the thing in
        // front is neither the game nor the overlay itself.
        _foreground = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _foreground.Tick += (_, _) => CheckForeground();
        _foreground.Start();
    }

    private void CheckForeground()
    {
        if (!_window.IsVisible) return;

        var fg = GetForegroundWindow();
        if (fg == _handle) return;               // the overlay has focus; fine

        if (OwnedByGame(fg)) return;             // the game is in front; fine

        // Something else is in front. Step out of its way.
        Hide();
    }

    private bool OwnedByGame(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        try
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return false;
            using var p = Process.GetProcessById((int)pid);
            return string.Equals(p.ProcessName, GameProcessName,
                                 StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private IntPtr OnMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            Toggle();
            handled = true;
        }
        return IntPtr.Zero;
    }

    /// <summary>Shows the window over the game, or hides it and gives the game back.</summary>
    public void Toggle()
    {
        if (_window.IsVisible)
        {
            Hide();
            return;
        }

        // Captured before we are in front of it.
        _previous = GetForegroundWindow();

        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;
        _window.Activate();
        SetForegroundWindow(_handle);

        // Re-applied here, not just at startup: the layered attribute set
        // before the first paint does not reliably stick, and showing the
        // window is the moment it is certain to.
        Apply(_opacity);
    }

    public void Hide()
    {
        _window.Hide();
        if (_previous != IntPtr.Zero && _previous != _handle)
            SetForegroundWindow(_previous);
    }

    /// <summary>
    /// How solid the window is, from 0.3 to 1.
    ///
    /// Through Win32 rather than WPF's Window.Opacity, which needs
    /// AllowsTransparency - and that turns off the DWM frame, taking the
    /// resizing, the snapping and the drop shadow with it. A layered window
    /// keeps all of that and dims the whole thing in the compositor.
    /// </summary>
    public void SetOpacity(double opacity)
    {
        _opacity = Math.Clamp(opacity, 0.3, 1.0);
        Apply(_opacity);
    }

    private void Apply(double solid)
    {
        // Window.Opacity, not a layered window: the layered-window attribute
        // does not make a WPF window over the desktop translucent here (it
        // stays opaque), while AllowsTransparency plus Opacity does. The window
        // carries AllowsTransparency=True for exactly this.
        _window.Opacity = solid;
    }

    private static Mod Translate(ModifierKeys modifiers)
    {
        var m = default(Mod);
        if (modifiers.HasFlag(ModifierKeys.Alt)) m |= Mod.Alt;
        if (modifiers.HasFlag(ModifierKeys.Control)) m |= Mod.Control;
        if (modifiers.HasFlag(ModifierKeys.Shift)) m |= Mod.Shift;
        if (modifiers.HasFlag(ModifierKeys.Windows)) m |= Mod.Win;
        return m;
    }

    private static string Describe(ModifierKeys modifiers, Key key)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }
}
