using System.Linq;
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
        Loaded += (_, _) => { RefreshStatus(); RefreshPresets(); _watch.Start(); };
    }

    private void RefreshPresets()
    {
        var presets = PresetStore.Load();

        // Fold the mod's latest splits into the loaded preset before showing it,
        // so a run just finished in training shows its new best here. Without
        // this the fold only happened when the config app's Presets page was
        // opened, and the launcher's own list stayed on the old time.
        var active = presets.FirstOrDefault(p => p.Id == PresetStore.ActiveId());
        if (active is not null)
        {
            PresetStore.FoldTimes(active);
            PresetStore.Save(presets);
        }

        PresetList.ItemsSource = presets;
        NoPresets.Visibility = presets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Train a preset from the launcher: put its layout in place, then start
    /// the game in training and resume onto its map. Same preparation as the
    /// overlay's button, a different way of getting to the map.
    /// </summary>
    private async void TrainPreset_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (sender is not System.Windows.Controls.Button { Tag: Preset preset }) return;

        PresetActions.LoadLayout(preset);

        _busy = true;
        TrainingButton.IsEnabled = false;
        CompeteButton.IsEnabled = false;
        _watch.Stop();

        var result = await ModeSwitch.Enter(Mode.Training,
            msg => Dispatcher.Invoke(() => SetStatus(msg, accent: true)),
            startupLevel: preset.Map);

        SetStatus(result.Message, accent: result.Ok);
        if (result.Ok)
        {
            HandOffToGame();
            return;
        }

        _busy = false;
        TrainingButton.IsEnabled = true;
        CompeteButton.IsEnabled = true;
        _watch.Start();
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

    private async void Compete_Click(object sender, RoutedEventArgs e) =>
        await EnterMode(Mode.Compete);

    private async Task EnterMode(Mode mode)
    {
        if (_busy) return;
        _busy = true;
        TrainingButton.IsEnabled = false;
        CompeteButton.IsEnabled = false;
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
            HandOffToGame();
            return;
        }

        _busy = false;
        TrainingButton.IsEnabled = true;
        CompeteButton.IsEnabled = true;
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
    private System.Windows.Threading.DispatcherTimer? _exitWatch;

    /// <summary>
    /// The game is starting: bring the overlay up for in-game use, hide this
    /// window rather than close it, and watch for the game to exit so it can
    /// come back. Hidden, not closed, so there is always a way back to the two
    /// buttons — closing left the app running with no window to reach.
    /// </summary>
    private void HandOffToGame()
    {
        EnsureOverlay();
        Hide();

        _watch.Stop();
        _exitWatch?.Stop();
        _exitWatch = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2),
        };
        _exitWatch.Tick += (_, _) =>
        {
            if (Game.IsRunning) return;
            _exitWatch!.Stop();
            _overlay?.Hide();
            Show();
            WindowState = WindowState.Normal;
            Activate();
            _busy = false;
            TrainingButton.IsEnabled = true;
            CompeteButton.IsEnabled = true;
            RefreshStatus();
            RefreshPresets();
            _watch.Start();
        };
        _exitWatch.Start();
    }

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
            OpenGameFolderButton.Visibility = Visibility.Collapsed;
            TrainingButton.IsEnabled = false;
            CompeteButton.IsEnabled = false;
            return;
        }

        ModLoader.BinariesDir = Game.BinariesDir;
        if (ModLoader.Current == ModLoader.State.NotInstalled)
        {
            // The game is found, but the loader is not in the folder we look in.
            // Almost always the mod files were dropped one level too deep or in
            // the wrong Binaries — so we name the exact folder and open it, and
            // the fix is to drop dwmapi.dll and ue4ss straight into it.
            SetStatus("Mod not found. Put dwmapi.dll and the ue4ss folder here → " +
                      Game.BinariesDir, accent: false);
            ModeText.Text = "";
            OpenGameFolderButton.Visibility = Visibility.Visible;
            TrainingButton.IsEnabled = false;
            CompeteButton.IsEnabled = false;
            return;
        }

        OpenGameFolderButton.Visibility = Visibility.Collapsed;
        TrainingButton.IsEnabled = true;
        CompeteButton.IsEnabled = true;
        SetStatus(Game.IsRunning ? "VHOLUME is running." : "VHOLUME is installed and ready.",
                  accent: false);
        RefreshMode();
    }

    /// <summary>Opens the exact folder the mod must sit in, so it cannot be put
    /// in the wrong place.</summary>
    private void OpenGameFolder_Click(object sender, RoutedEventArgs e)
    {
        var dir = Game.BinariesDir;
        if (dir is null) return;
        try
        {
            System.IO.Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(dir) { UseShellExecute = true });
        }
        catch { /* nothing useful to do if the shell will not open it */ }
    }

    private void RefreshMode()
    {
        ModeText.Text = ModeSwitch.CurrentMode() switch
        {
            Mode.Training => "set for TRAINING",
            Mode.Compete => "set for COMPETE",
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

    private void Close_Click(object sender, RoutedEventArgs e) =>
        Application.Current.Shutdown();
}
