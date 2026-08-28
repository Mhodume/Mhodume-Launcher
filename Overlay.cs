using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

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
        };
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
        if (_handle == IntPtr.Zero) return;

        var solid = Math.Clamp(opacity, 0.3, 1.0);
        var style = GetWindowLong(_handle, GWL_EXSTYLE);

        if (solid >= 1.0)
        {
            // Taken back off rather than set to full: a layered window is
            // composited differently even at full alpha, and there is no
            // reason to pay for that when nothing is see-through.
            SetWindowLong(_handle, GWL_EXSTYLE, style & ~WS_EX_LAYERED);
            return;
        }

        if ((style & WS_EX_LAYERED) == 0)
            SetWindowLong(_handle, GWL_EXSTYLE, style | WS_EX_LAYERED);
        SetLayeredWindowAttributes(_handle, 0, (byte)Math.Round(solid * 255), LWA_ALPHA);
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
