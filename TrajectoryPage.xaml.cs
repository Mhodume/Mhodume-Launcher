using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Mhodume;

public partial class TrajectoryPage : UserControl
{
    private static readonly string[] SwatchColors =
    {
        "#00E676", "#00E5FF", "#FFFFFF", "#FFEB3B",
        "#FF3D3D", "#FF00E5", "#FF9100", "#7C4DFF",
    };

    private ConfigStore? _store;
    private TrajectoryConfig? _config;
    private List<GhostInfo> _all = new();
    private bool _scanned;

    public TrajectoryPage()
    {
        InitializeComponent();
        BuildSwatches();
        DataContextChanged += (_, e) =>
        {
            if (_config is not null) _config.PropertyChanged -= Config_PropertyChanged;
            _config = e.NewValue as TrajectoryConfig;
            if (_config is not null) _config.PropertyChanged += Config_PropertyChanged;

            UpdateColorMode();
            ShowLoaded();
            RefreshMapWarning();
        };

        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue && !_scanned) _ = ScanAsync();
        };
    }

    public void Initialize(ConfigStore store) => _store = store;

    private string? _currentMap;

    /// <summary>
    /// Called with the level the game currently has loaded (null when it is not
    /// running). Nothing gets drawn on a map the loaded run does not belong to,
    /// so say it plainly rather than letting the user wonder.
    /// </summary>
    public void UpdateCurrentMap(string? map)
    {
        _currentMap = map;
        RefreshMapWarning();
    }

    private void RefreshMapWarning()
    {
        var wanted = _config?.Map;

        if (_config is null || !_config.Enabled || string.IsNullOrWhiteSpace(wanted) ||
            string.IsNullOrWhiteSpace(_currentMap) || MapsMatch(_currentMap!, wanted!))
        {
            MapWarning.Visibility = Visibility.Collapsed;
            return;
        }

        MapWarningText.Text =
            $"You are playing {_currentMap}, but this run was recorded on {wanted}. " +
            "Nothing will be drawn until you load that map, or pick a run from the one you are on.";
        MapWarning.Visibility = Visibility.Visible;
    }

    /// <summary>Mirrors the loose comparison the Lua module uses.</summary>
    private static bool MapsMatch(string a, string b)
        => a.Equals(b, StringComparison.OrdinalIgnoreCase)
           || a.Contains(b, StringComparison.OrdinalIgnoreCase)
           || b.Contains(a, StringComparison.OrdinalIgnoreCase);

    private void Config_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TrajectoryConfig.Gradient)) UpdateColorMode();
        if (e.PropertyName is nameof(TrajectoryConfig.Color) or nameof(TrajectoryConfig.LineColor))
            UpdateHexBox();
        if (e.PropertyName is nameof(TrajectoryConfig.Map) or nameof(TrajectoryConfig.Enabled))
            RefreshMapWarning();
    }

    /// <summary>
    /// Both colour modes stay on screen: hiding the swatches behind an
    /// unchecked box made the single-colour option impossible to discover.
    /// Only the gradient legend comes and goes.
    /// </summary>
    private void UpdateColorMode()
    {
        var gradient = _config?.Gradient ?? true;
        GradientLegend.Visibility = gradient ? Visibility.Visible : Visibility.Collapsed;
        GradientLabels.Visibility = gradient ? Visibility.Visible : Visibility.Collapsed;

        // Both, always, and in this order: checking one makes WPF clear the
        // other, so setting only one leaves the result to whatever ran last.
        SpeedRadio.IsChecked = gradient;
        SolidRadio.IsChecked = !gradient;
        UpdateHexBox();
    }

    private void SpeedRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_config is not null) _config.Gradient = true;
    }

    private void SolidRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_config is not null) _config.Gradient = false;
    }

    private void UpdateHexBox()
    {
        if (_config is null) return;
        var c = _config.LineColor;
        var text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        if (!string.Equals(HexBox.Text, text, StringComparison.OrdinalIgnoreCase))
            HexBox.Text = text;
    }

    private void HexBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) ApplyHex();
    }

    private void HexBox_LostFocus(object sender, RoutedEventArgs e) => ApplyHex();

    private void ApplyHex()
    {
        if (_config is null) return;
        var text = HexBox.Text.Trim();
        if (!text.StartsWith('#')) text = "#" + text;
        try
        {
            PickColour((Color)ColorConverter.ConvertFromString(text)!);
        }
        catch
        {
            UpdateHexBox();     // reject silently, show the real value back
        }
    }

    /// <summary>Choosing a colour implies you want that colour, not the gradient.</summary>
    private void PickColour(Color c)
    {
        if (_config is null) return;
        _config.LineColor = c;
        _config.Gradient = false;
    }

    // -------------------------------------------------------------- scanning
    private async Task ScanAsync()
    {
        _scanned = true;
        ScanNotice.Visibility = Visibility.Visible;
        GhostList.ItemsSource = null;
        MapBox.ItemsSource = null;

        var found = await Task.Run(GhostFile.Discover);

        _all = found;
        var maps = found.Select(g => g.Map).Distinct().OrderBy(m => m).ToList();

        ScanNotice.Visibility = Visibility.Collapsed;

        if (maps.Count == 0)
        {
            ScanNotice.Text = "No ghost files found in your VHOLUME save folder.";
            ScanNotice.Visibility = Visibility.Visible;
            return;
        }

        MapBox.ItemsSource = maps;

        // preselect the map of whatever is currently loaded
        // prefer the map in play, then the loaded run's map, then the first one
        var preferred = maps.FirstOrDefault(m => _currentMap is not null && MapsMatch(_currentMap, m))
                        ?? (_config?.Map is { Length: > 0 } cm && maps.Contains(cm) ? cm : null)
                        ?? maps[0];
        MapBox.SelectedItem = preferred;
    }

    private async void Rescan_Click(object sender, RoutedEventArgs e)
    {
        _scanned = false;
        await ScanAsync();
    }

    private void MapBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RebuildGhostList();
    }

    private void SortBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => RebuildGhostList();

    /// <summary>
    /// The runs for the chosen map, in the chosen order. Unfinished runs sink
    /// to the bottom either way — a partial run is never the one you want first,
    /// whether you are after the fastest or the newest.
    /// </summary>
    private void RebuildGhostList()
    {
        if (MapBox is null || MapBox.SelectedItem is not string map) return;

        var runs = _all.Where(g => g.Map == map)
                       .OrderBy(g => g.Completed ? 0 : 1);

        var byRecent = SortBox?.SelectedIndex == 1;
        runs = byRecent
            ? runs.ThenByDescending(g => g.Recorded)
            : runs.ThenBy(g => g.TimeMs);

        GhostList.ItemsSource = runs.ToList();
        LoadButton.IsEnabled = false;
    }

    private void GhostList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => LoadButton.IsEnabled = GhostList.SelectedItem is GhostInfo;

    // ---------------------------------------------------------------- loading
    private void Load_Click(object sender, RoutedEventArgs e)
    {
        if (_store is null || _config is null) return;
        if (GhostList.SelectedItem is not GhostInfo info) return;

        try
        {
            var traj = GhostFile.Load(info.Path);
            if (traj.PointCount < 2)
            {
                MessageBox.Show("That run has no usable path data.", "Mhodume");
                return;
            }

            _store.WriteTrajectory(traj);

            _config.Map = traj.Map;
            _config.SourcePath = info.Path;
            _config.Label = $"{info.PlayerName} — {info.TimeText} on {MapNames.Display(traj.Map)}";
            _config.Enabled = true;

            ShowLoaded(traj);
            RefreshMapWarning();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not read that ghost:\n\n" + ex.Message, "Mhodume");
        }
    }

    private void ShowLoaded(Trajectory? traj = null)
    {
        if (_config is null || string.IsNullOrWhiteSpace(_config.Label))
        {
            LoadedTitle.Text = "No run loaded";
            LoadedDetail.Text = "Select a run above and press “Draw this run”.";
            return;
        }

        LoadedTitle.Text = _config.Label;
        LoadedDetail.Text = traj is not null
            ? $"{traj.PointCount} points across {traj.Segments.Count} segment" +
              (traj.Segments.Count == 1 ? "" : "s") +
              $". The line is only drawn while you are playing {MapNames.Display(traj.Map)}."
            : $"The line is only drawn while you are playing {MapNames.Display(_config.Map)}.";
    }

    // ----------------------------------------------------------------- inputs
    /// <summary>
    /// Opens the loaded run's keys end to end. Read back from the ghost rather
    /// than kept in memory: the page holds a config, not a run, and the file it
    /// came from is named in that config.
    /// </summary>
    private void ShowInputs_Click(object sender, RoutedEventArgs e)
    {
        if (_config is null || string.IsNullOrWhiteSpace(_config.SourcePath))
        {
            MessageBox.Show("No run is loaded. Pick one above and press “Draw this run”.",
                            "Mhodume");
            return;
        }

        try
        {
            var traj = GhostFile.Load(_config.SourcePath);
            var window = new InputsWindow { Owner = Window.GetWindow(this) };
            window.Attach(_config);
            window.Present(traj, _config.Label);
            window.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not read that ghost:\n\n" + ex.Message, "Mhodume");
        }
    }

    // ----------------------------------------------------------------- colour
    private void BuildSwatches()
    {
        foreach (var hex in SwatchColors)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex)!;
            var swatch = new Border
            {
                Width = 26,
                Height = 26,
                Margin = new Thickness(0, 0, 6, 6),
                Background = new SolidColorBrush(color),
                BorderBrush = (Brush)Application.Current.FindResource("Edge"),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = hex,
            };
            swatch.MouseLeftButtonUp += (_, _) => PickColour(color);
            Swatches.Items.Add(swatch);
        }
    }
}
