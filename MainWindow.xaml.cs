using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Shell;
using System.Windows.Media;
using System.Windows.Threading;

namespace Mhodume;

public partial class MainWindow : Window
{
    // The window chrome follows the Windows theme, not the app, so a light
    // system theme gives a bright title bar above a dark window. Ask DWM for
    // the dark one instead.
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private const int DwmwaUseImmersiveDarkMode = 20;

    /// <summary>
    /// The key that brings the window up over the game.
    ///
    /// Ctrl+Shift+M, because the game already spends F6, F7, F9 and Insert on
    /// the mod and F10 on the console, and a single unmodified key is one
    /// stray press away from opening this mid-run.
    /// </summary>
    private static readonly ModifierKeys OverlayModifiers =
        ModifierKeys.Control | ModifierKeys.Shift;
    private static readonly Key OverlayKey = Key.M;

    private Overlay? _overlay;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int enabled = 1;
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int));
        }
        catch
        {
            // older Windows builds simply keep the light title bar
        }

        RestorePlacement();

        _overlay = new Overlay(this);
        _overlay.Attach(OverlayModifiers, OverlayKey);
        ShowOverlayKey();

        var solid = _store.LoadOpacity();
        OpacitySlider.Value = solid * 100;
        _overlay.SetOpacity(solid);
    }

    /// <summary>
    /// Puts the window back where it was left, if that is still on a screen.
    /// A monitor unplugged since last time would otherwise leave it at
    /// coordinates nobody can reach.
    /// </summary>
    private void RestorePlacement()
    {
        var (left, top, width, height) = _store.LoadPlacement();
        if (left is not double x || top is not double y) return;

        var area = SystemParameters.WorkArea;
        if (x + 80 < area.Left || x > area.Right - 80 ||
            y + 40 < area.Top || y > area.Bottom - 40) return;

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = x;
        Top = y;
        if (width is double w && w >= MinWidth) Width = w;
        if (height is double h && h >= MinHeight) Height = h;
    }

    /// <summary>
    /// Saved when the window goes away rather than on every drag: the position
    /// only matters at the moment you stop looking at it, and writing a file
    /// on each mouse move to remember something nobody has finished choosing
    /// is work for nothing.
    /// </summary>
    private void RememberPlacement()
    {
        if (WindowState != WindowState.Normal) return;
        _store.SavePlacement(Left, Top, Width, Height);
    }

    /// <summary>
    /// Applied as it moves and written down when it stops, the same way every
    /// other slider in the app behaves.
    /// </summary>
    private void Opacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_overlay is null) return;
        var solid = e.NewValue / 100;
        _overlay.SetOpacity(solid);
        OpacityValue.Text = $"{e.NewValue:0} %";
        _store.SaveOpacity(solid);
    }

    private void ShowOverlayKey()
    {
        if (_overlay is null) return;
        SetStatus(_overlay.Problem is null
            ? $"Press {_overlay.KeyName} to hide this and go back to the game"
            : _overlay.Problem + " - the window will not come back on a key",
            ok: _overlay.Problem is null);
    }

    /// <summary>
    /// Closing the window hides it instead. An overlay you dismiss with the
    /// key and an overlay you dismiss with the cross should end up in the same
    /// place, and the key is what brings it back either way.
    /// </summary>
    private void Close_Click(object sender, RoutedEventArgs e)
    {
        RememberPlacement();
        _overlay?.Hide();
    }

    private readonly ConfigStore _store = new();
    private ModConfig _config = new();
    private bool _suspendWrites;          // set while a profile is being swapped in
    private DispatcherTimer? _gameWatch;

    /// <summary>
    /// One entry in the navigation column: what it is called, which group it
    /// belongs under, and the page it shows. Group headers are rows too, and
    /// are the rows that cannot be selected.
    /// </summary>
    private sealed class Section
    {
        public required string Name { get; init; }
        public string? Group { get; init; }          // set on a header row
        public UIElement? Page { get; init; }
        public string Number { get; set; } = "";     // filled in below
        public string Note { get; init; } = "";      // what this page does
        public bool NeedsTraining { get; init; }     // F7 required for any of it
        public bool IsHeader => Group is not null;
        public string Title => Name.ToUpperInvariant();

        public override string ToString() => Name;
    }

    private List<Section> _sections = new();

    public MainWindow()
    {
        InitializeComponent();

        _store.Status += msg => Dispatcher.Invoke(() => SetStatus(msg, ok: true));

        _sections = new List<Section>
        {
            new() { Name = "OVERLAY", Group = "OVERLAY" },
            new() { Name = "Crosshair", Page = PageCrosshair,
                    Note = "drawn by the mod · applies live" },
            new() { Name = "HUD", Page = PageHud,
                    Note = "the game's own overlay · applies live" },

            new() { Name = "ROUTE", Group = "ROUTE" },
            new() { Name = "Checkpoints", Page = PageCheckpoints,
                    Note = "your own splits" },
            new() { Name = "Trajectory", Page = PageTrajectory,
                    Note = "a saved run drawn in the world" },
            new() { Name = "Presets", Page = PagePresets,
                    Note = "saved checkpoint layouts and their times" },

            new() { Name = "TOOLS", Group = "TOOLS" },
            new() { Name = "Freecam", Page = PageFreecam,
                    Note = "detach the camera" },
            new() { Name = "Tweaks", Page = PageTweaks,
                    Note = "how the game behaves while you practise" },

            new() { Name = "PROGRESS", Group = "PROGRESS" },
            new() { Name = "Completion", Page = PageCompletion,
                    Note = "levels finished, B-sides, best times — live from your save" },
            new() { Name = "NPCs", Page = PageNpcs,
                    Note = "who you have spoken to, live from your save" },

            new() { Name = "APP", Group = "APP" },
            new() { Name = "Profiles", Page = PageProfiles,
                    Note = "every setting at once, saved under a name" },
            new() { Name = "About", Page = PageAbout,
                    Note = "install, files, and the mod on/off switch" },
        };

        // Numbered by position among the real destinations, so the column and
        // the page title agree and the headers are not counted.
        var n = 0;
        foreach (var section in _sections)
            if (!section.IsHeader) section.Number = (++n).ToString("00");

        NavList.ItemsSource = _sections;
        NavList.SelectedIndex = 1;              // the first row is a header

        var built = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        if (built is not null) VersionText.Text = $"v{built.Major}.{built.Minor}";

        StateChanged += (_, _) => ShowWindowState();
        SourceInitialized += (_, _) => KeepMaximiseInsideTheWorkArea();

        _store.SeedDefaultProfiles();
        AttachConfig(_store.LoadLive());

        // Write the whole file back straight away. A config saved by an older
        // build is missing any section added since, and the mod then falls back
        // to defaults for it with no sign of why — this keeps the file complete.
        _store.FlushLive(_config);

        PageTrajectory.Initialize(_store);
        PagePresets.Initialize(_store, () => _config, () => _currentMapName);
        PageProfiles.Initialize(_store, () => _config);
        PageProfiles.ProfileLoaded += OnProfileLoaded;

        ConfigPathText.Text = ConfigStore.LivePath;
        ModStatusText.Text = DescribeModInstall();

        var game = FindGameDir();
        if (game is not null)
            ModLoader.BinariesDir = Path.Combine(game, "VHOLUME", "Binaries", "Win64");
        RefreshLoaderState();
        RefreshModeChip();

        StartGameWatch();
        SetStatus("settings loaded", ok: false);
        ShowProfile(_store.LoadLastProfile());
    }

    // -------------------------------------------------------------- config
    private void AttachConfig(ModConfig cfg)
    {
        if (_config is not null) _config.AnyChanged -= Config_AnyChanged;

        _config = cfg;
        _config.AnyChanged += Config_AnyChanged;

        PageCrosshair.DataContext = _config.Crosshair;
        PageHud.DataContext = _config.Hud;
        PageTrajectory.DataContext = _config.Trajectory;
        PageFreecam.DataContext = _config.Freecam;
        PageCrosshair.SetSpeedContext(_config.Speed);
        PageTweaks.DataContext = _config.Tweaks;
        PageCheckpoints.DataContext = _config.Checkpoints;
    }

    private void Config_AnyChanged(object? sender, EventArgs e)
    {
        if (_suspendWrites) return;
        _store.QueueLiveWrite(_config);
    }

    private void OnProfileLoaded(ModConfig cfg, string name)
    {
        _suspendWrites = true;
        AttachConfig(cfg);
        _suspendWrites = false;

        _store.FlushLive(_config);
        _store.SaveLastProfile(name);
        ShowProfile(name);
        SetStatus($"Profile “{name}” loaded", ok: true);
    }

    // ----------------------------------------------------------- the caption
    // --------------------------------------------------------- mode switch
    private bool _switching;
    private string? _currentMapName;

    private void RefreshModeChip()
    {
        switch (ModeSwitch.CurrentMode())
        {
            case Mode.Training:
                ModeChipText.Text = "TRAINING";
                ModeChipText.Foreground = (Brush)FindResource("Accent");
                ModeChip.BorderBrush = (Brush)FindResource("Accent");
                SwitchModeButton.Content = "SWITCH TO COMPETE";
                SwitchModeButton.IsEnabled = true;
                break;
            case Mode.Compete:
                ModeChipText.Text = "COMPETE";
                ModeChipText.Foreground = (Brush)FindResource("Text");
                ModeChip.BorderBrush = (Brush)FindResource("Edge");
                SwitchModeButton.Content = "SWITCH TO TRAINING";
                SwitchModeButton.IsEnabled = true;
                break;
            default:
                ModeChipText.Text = "-";
                SwitchModeButton.Content = "SWITCH";
                SwitchModeButton.IsEnabled = false;
                break;
        }
    }

    private async void SwitchMode_Click(object sender, RoutedEventArgs e)
    {
        if (_switching) return;
        var current = ModeSwitch.CurrentMode();
        if (current is null) return;
        var target = current == Mode.Training ? Mode.Compete : Mode.Training;

        _switching = true;
        SwitchModeButton.IsEnabled = false;
        var result = await ModeSwitch.Enter(target, msg =>
            Dispatcher.Invoke(() => SwitchBusyText.Text = msg));
        SwitchBusyText.Text = result.Ok ? "" : result.Message;
        _switching = false;
        RefreshModeChip();
    }

    private void Minimise_Click(object sender, RoutedEventArgs e) =>
        SystemCommands.MinimizeWindow(this);

    private void Maximise_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }


    /// <summary>
    /// The system menu is normally reached by right-clicking the caption, and
    /// with the caption gone so is Alt+Space. Keep the gesture working.
    /// </summary>
    private void TitleBar_RightClick(object sender, MouseButtonEventArgs e) =>
        SystemCommands.ShowSystemMenu(this, PointToScreen(e.GetPosition(this)));

    /// <summary>Restore and maximise are the same button, so it has to say which.</summary>
    private void ShowWindowState()
    {
        var maximised = WindowState == WindowState.Maximized;
        MaximiseButton.ToolTip = maximised ? "Restore" : "Maximise";
        MaximiseGlyph.Width = maximised ? 8 : 10;
        MaximiseGlyph.Height = maximised ? 8 : 10;
        MaximiseGlyph.Margin = maximised
            ? new Thickness(0, 2, 2, 0)
            : new Thickness(0);
    }

    // ---------------------------------------------------------- navigation
    private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var chosen = NavList.SelectedItem as Section;

        // A header is not a destination. The mouse cannot reach one - the row
        // refuses hit testing - but the keyboard can still walk onto it, so it
        // is passed straight through to the next real section.
        if (chosen is { IsHeader: true })
        {
            var from = NavList.SelectedIndex;
            var next = _sections.FindIndex(from + 1, x => !x.IsHeader);
            if (next < 0) next = _sections.FindLastIndex(from, x => !x.IsHeader);
            if (next >= 0) NavList.SelectedIndex = next;
            return;
        }

        foreach (var section in _sections)
            if (section.Page is not null)
                section.Page.Visibility = ReferenceEquals(section, chosen)
                    ? Visibility.Visible
                    : Visibility.Collapsed;

        PageNumber.Text = chosen?.Number ?? "";
        PageTitle.Text = chosen?.Title ?? "";
        PageNote.Text = chosen?.Note ?? "";
    }

    private void RefreshLoaderState()
    {
        switch (ModLoader.Current)
        {
            case ModLoader.State.Enabled:
                LoaderState.Text = "Mod is ON — runs will not be validated";
                LoaderState.Foreground = (Brush)FindResource("Accent");
                LoaderButton.Content = "Switch the mod off (for timed runs)";
                LoaderButton.IsEnabled = true;
                break;

            case ModLoader.State.Disabled:
                LoaderState.Text = "Mod is OFF — the game is untouched, runs count";
                LoaderState.Foreground = (Brush)FindResource("Text");
                LoaderButton.Content = "Switch the mod on (to practise)";
                LoaderButton.IsEnabled = true;
                break;

            default:
                LoaderState.Text = "UE4SS is not installed";
                LoaderState.Foreground = (Brush)FindResource("Muted");
                LoaderButton.Content = "Nothing to switch";
                LoaderButton.IsEnabled = false;
                break;
        }
    }

    private void ToggleLoader_Click(object sender, RoutedEventArgs e)
    {
        bool running;
        try { running = Process.GetProcessesByName("VHOLUME-Win64-Shipping").Length > 0; }
        catch { running = false; }

        if (running)
        {
            MessageBox.Show("Close VHOLUME first — the loader cannot be renamed while the game holds it.",
                            "Mhodume", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var error = ModLoader.Toggle();
        RefreshLoaderState();

        if (error is not null)
        {
            MessageBox.Show(error, "Mhodume", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SetStatus(ModLoader.Current == ModLoader.State.Enabled
            ? "Mod switched on — start VHOLUME to practise"
            : "Mod switched off — your runs will count again", ok: true);
    }

    private void OpenConfigFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(ConfigStore.RootDir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not open the folder: " + ex.Message, "Mhodume");
        }
    }

    // ------------------------------------------------------- install check
    /// <summary>
    /// Looks for the game through Steam's library folders and reports whether
    /// the UE4SS module is actually in place.
    /// </summary>
    private static string DescribeModInstall()
    {
        var game = FindGameDir();
        if (game is null)
            return "VHOLUME install not found automatically. If the crosshair does not appear in game, check that UE4SS and the mod are installed.";

        var modLua = Path.Combine(game, "VHOLUME", "Binaries", "Win64",
                                  "ue4ss", "Mods", "Mhodume", "Scripts", "main.lua");
        var loader = Path.Combine(game, "VHOLUME", "Binaries", "Win64", "dwmapi.dll");

        var parts = new List<string> { "Game found: " + game };
        parts.Add(File.Exists(loader) ? "UE4SS loader: installed" : "UE4SS loader: MISSING");
        parts.Add(File.Exists(modLua) ? "Mod module: installed" : "Mod module: MISSING");
        return string.Join("\n", parts);
    }

    private static string? FindGameDir()
    {
        try
        {
            foreach (var root in new[]
                     {
                         @"C:\Program Files (x86)\Steam",
                         @"C:\Steam", @"D:\Steam", @"D:\SteamLibrary",
                         @"E:\SteamLibrary", @"F:\SteamLibrary", @"G:\SteamLibrary",
                         @"H:\SteamLibrary",
                     })
            {
                var candidate = Path.Combine(root, "steamapps", "common", "VHOLUME");
                if (Directory.Exists(candidate)) return candidate;
            }

            // fall back to parsing Steam's library index
            var vdf = @"C:\Program Files (x86)\Steam\steamapps\libraryfolders.vdf";
            if (File.Exists(vdf))
            {
                foreach (var line in File.ReadAllLines(vdf))
                {
                    var trimmed = line.Trim();
                    if (!trimmed.StartsWith("\"path\"")) continue;
                    var parts = trimmed.Split('"', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 3) continue;
                    var candidate = Path.Combine(parts[^1].Replace(@"\\", @"\"),
                                                 "steamapps", "common", "VHOLUME");
                    if (Directory.Exists(candidate)) return candidate;
                }
            }
        }
        catch
        {
            // best effort only — the app works fine without knowing the path
        }
        return null;
    }

    // ------------------------------------------------------------ game watch
    private void StartGameWatch()
    {
        _gameWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _gameWatch.Tick += (_, _) =>
        {
            bool running;
            try { running = Process.GetProcessesByName("VHOLUME-Win64-Shipping").Length > 0; }
            catch { running = false; }

            var status = running ? ConfigStore.ReadStatus() : new ConfigStore.GameStatus(null, false, false);

            // LIVE means what it says: the game is up and reading the file
            // this window writes. Anything else and your changes are only
            // sitting on disk.
            LiveBadge.Foreground = (Brush)FindResource(
                running && ModLoader.Current == ModLoader.State.Enabled ? "Accent" : "Muted");

            if (!_switching) RefreshModeChip();

            if (!running)
            {
                GameStatus.Text = "game not running";
                GameStatus.Foreground = (Brush)FindResource("Muted");
            }
            else
            {
                GameStatus.Text = status.Map is null
                    ? "no level loaded"
                    : MapNames.Display(status.Map);
                GameStatus.Foreground = (Brush)FindResource(
                    status.Map is null ? "Muted" : "Text");
            }

            if (running && !status.Training && status.LapTainted)
            {
                GameStatus.Text += " — lap spent, restart the level";
                GameStatus.Foreground = (Brush)FindResource("WarnText");
            }

            _currentMapName = running ? status.Map : null;
            PageTrajectory.UpdateCurrentMap(_currentMapName);
            RefreshLoaderState();
        };
        _gameWatch.Start();
    }

    private void SetStatus(string message, bool ok)
    {
        // The time of the last write, not how long ago it was: a number
        // counting up four times a second in the corner of a window someone
        // leaves open all evening is a thing the eye keeps going back to for
        // no reason.
        StatusText.Text = ok
            ? $"{message} · {DateTime.Now:HH:mm:ss}"
            : message;
        StatusDot.Fill = (Brush)FindResource(ok ? "Ok" : "Muted");
    }

    private void ShowProfile(string? name) =>
        ProfileStatus.Text = name ?? "no profile";

    protected override void OnClosed(EventArgs e)
    {
        _gameWatch?.Stop();
        _store.FlushLive(_config);
        base.OnClosed(e);
    }
    // ------------------------------------------------- maximise, done properly
    //
    // A window without a system caption maximises to the whole monitor and
    // covers the taskbar, because the frame no longer knows to stop at the
    // work area. WM_GETMINMAXINFO is where that is decided, so it is answered
    // with the work area of the monitor the window is on - which is not
    // necessarily the primary one, and SystemParameters only knows about that.

    [System.Runtime.InteropServices.StructLayout(
        System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [System.Runtime.InteropServices.StructLayout(
        System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public int Flags;
    }

    [System.Runtime.InteropServices.StructLayout(
        System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    [System.Runtime.InteropServices.StructLayout(
        System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, int flags);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    private void KeepMaximiseInsideTheWorkArea()
    {
        var source = System.Windows.Interop.HwndSource.FromHwnd(
            new System.Windows.Interop.WindowInteropHelper(this).Handle);
        source?.AddHook((IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled) =>
        {
            const int WM_GETMINMAXINFO = 0x0024;
            const int MONITOR_DEFAULTTONEAREST = 0x00000002;
            if (msg != WM_GETMINMAXINFO) return IntPtr.Zero;

            var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero) return IntPtr.Zero;

            var info = new MonitorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref info)) return IntPtr.Zero;

            var mmi = System.Runtime.InteropServices.Marshal.PtrToStructure<MinMaxInfo>(l);
            mmi.MaxPosition.X = info.Work.Left - info.Monitor.Left;
            mmi.MaxPosition.Y = info.Work.Top - info.Monitor.Top;
            mmi.MaxSize.X = info.Work.Right - info.Work.Left;
            mmi.MaxSize.Y = info.Work.Bottom - info.Work.Top;
            System.Runtime.InteropServices.Marshal.StructureToPtr(mmi, l, true);

            handled = true;
            return IntPtr.Zero;
        });
    }

}
