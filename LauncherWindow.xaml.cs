using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Mhodume;

/// <summary>
/// The front door. Two buttons: play with the tools, or play clean. Each one
/// sets the loader to what the mode needs and starts the game through Steam;
/// the switch itself is <see cref="ModeSwitch"/>.
///
/// It also keeps the title bar dark to match the rest of the window, and shows,
/// at the bottom, what it found — whether VHOLUME is installed, which mode the
/// loader is set for, and whether the game is up.
/// </summary>
public partial class LauncherWindow : Window
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    private const int DwmwaUseImmersiveDarkMode = 20;

    private readonly DispatcherTimer _watch;
    private bool _busy;

    public LauncherWindow()
    {
        InitializeComponent();

        // Refresh the status line while the window is open: the game can be
        // started or closed outside the launcher, and the loader state can
        // change under it.
        _watch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _watch.Tick += (_, _) => RefreshStatus();
        Loaded += (_, _) => { RefreshStatus(); _watch.Start(); };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int on = 1;
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref on, sizeof(int));
        }
        catch { /* older Windows keeps a light title bar */ }
    }

    // ------------------------------------------------------------ the modes
    private async void Training_Click(object sender, RoutedEventArgs e) =>
        await EnterMode(Mode.Training);

    private async void Ranked_Click(object sender, RoutedEventArgs e) =>
        await EnterMode(Mode.Ranked);

    private async Task EnterMode(Mode mode)
    {
        if (_busy) return;
        _busy = true;
        TrainingButton.IsEnabled = false;
        RankedButton.IsEnabled = false;
        _watch.Stop();

        var result = await ModeSwitch.Enter(mode, msg =>
            Dispatcher.Invoke(() => SetStatus(msg, accent: true)));

        SetStatus(result.Message, accent: result.Ok);

        // Once a mode has started the game, bring the overlay into being so its
        // Ctrl+Shift+M is live over the game. Created hidden and once: the same
        // window serves both modes, and its own title-bar switch flips between
        // them without coming back here.
        // Once a mode has started the game, bring the overlay into being so its
        // Ctrl+Shift+M is live over the game, then close this window: the
        // overlay is the in-game face from here on. The app stays alive because
        // the overlay window, though hidden, is still open.
        if (result.Ok)
        {
            EnsureOverlay();
            Close();
            return;
        }

        _busy = false;
        TrainingButton.IsEnabled = true;
        RankedButton.IsEnabled = true;
        RefreshMode();
        _watch.Start();
    }

    // ------------------------------------------------------------ the overlay
    private MainWindow? _overlay;

    /// <summary>
    /// Creates the in-game overlay window, hidden, so it registers its hotkey
    /// and can be summoned over the game. Made once; a second mode reuses it.
    /// The launcher window then steps aside — the overlay is the in-game face
    /// from here on, and two visible windows would only compete.
    /// </summary>
    private void EnsureOverlay()
    {
        if (_overlay is null)
        {
            _overlay = new MainWindow();
            _overlay.Closed += (_, _) => _overlay = null;
            // Show then hide so the handle exists and the hotkey registers;
            // it lives off screen until the key calls it up over the game.
            _overlay.Show();
            _overlay.Hide();
        }
    }

    // ------------------------------------------------------------ the status
    private void RefreshStatus()
    {
        if (_busy) return;

        if (Game.BinariesDir is null)
        {
            SetStatus("VHOLUME not found — install it through Steam first.", accent: false);
            ModeText.Text = "";
            TrainingButton.IsEnabled = false;
            RankedButton.IsEnabled = false;
            return;
        }

        ModLoader.BinariesDir = Game.BinariesDir;
        if (ModLoader.Current == ModLoader.State.NotInstalled)
        {
            SetStatus("The mod is not installed in the game folder yet.", accent: false);
            ModeText.Text = "";
            return;
        }

        TrainingButton.IsEnabled = true;
        RankedButton.IsEnabled = true;
        SetStatus(Game.IsRunning ? "VHOLUME is running." : "VHOLUME is installed and ready.",
                  accent: false);
        RefreshMode();
    }

    private void RefreshMode()
    {
        ModeText.Text = ModeSwitch.CurrentMode() switch
        {
            Mode.Training => "set for TRAINING",
            Mode.Ranked => "set for RANKED",
            _ => "",
        };
    }

    private void SetStatus(string text, bool accent)
    {
        StatusText.Text = text;
        StatusText.Foreground = (System.Windows.Media.Brush)FindResource(accent ? "Accent" : "Muted");
    }

    // ------------------------------------------------------------ the chrome
    private void Minimise_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
