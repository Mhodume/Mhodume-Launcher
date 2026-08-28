using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace Mhodume;

public partial class CheckpointsPage : UserControl
{
    /// <summary>
    /// Keys the mod binds. One shared set — nothing claims a key of its own,
    /// which is what broke drawing when F10 collided with the game's console.
    /// F10 is absent on purpose.
    /// </summary>
    private static readonly string[] BindableKeys =
    {
        "F1", "F2", "F3", "F4", "F5", "F9", "F11", "F12",
        "INS", "HOME", "END", "PAGE_UP", "PAGE_DOWN", "PAUSE", "SCROLL_LOCK",
    };

    private static readonly string[] TextColors =
    {
        "#FFFFFF", "#00E676", "#00E5FF", "#FFEB3B",
        "#FF3D3D", "#FF9100", "#7C4DFF", "#9E9E9E",
    };

    private static readonly string[] MarkerColors =
    {
        "#FFD900", "#00E5FF", "#FFFFFF", "#FF9100",
        "#FF3D3D", "#00E676", "#FF00E5", "#7C4DFF",
    };

    private CheckpointsConfig? _config;
    private string? _map;
    private bool _loading;

    public CheckpointsPage()
    {
        InitializeComponent();

        KeyBox.ItemsSource = BindableKeys;
        BuildSwatches(TextSwatches, TextColors, c => { if (_config is not null) _config.TextColor = c; });
        BuildSwatches(MarkerSwatches, MarkerColors, c => { if (_config is not null) _config.MarkerBrush = c; });

        DataContextChanged += (_, e) =>
        {
            _config = e.NewValue as CheckpointsConfig;
            RefreshDrillBar();
        };

        // The list is read from disk, and the mod writes to the same files, so
        // it is refreshed on arrival rather than cached.
        IsVisibleChanged += (_, e) =>
        {
            if (!(bool)e.NewValue) return;
            RefreshMaps();
            RefreshDrillBar();
        };
    }

    private static void BuildSwatches(ItemsControl host, string[] hexes, Action<Color> pick)
    {
        foreach (var hex in hexes)
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
            swatch.MouseLeftButtonUp += (_, _) => pick(color);
            host.Items.Add(swatch);
        }
    }

    // ------------------------------------------------------------- listing
    /// <summary>
    /// Fills the map list, preferring the one being played so the page opens on
    /// what you are working on rather than on whatever sorts first.
    /// </summary>
    private void RefreshMaps()
    {
        _loading = true;
        try
        {
            var maps = CheckpointStore.Maps().ToList();
            var playing = ConfigStore.ReadCurrentMap();
            if (playing is not null && !maps.Contains(playing, StringComparer.OrdinalIgnoreCase))
                maps.Insert(0, playing);

            MapBox.ItemsSource = maps;

            var wanted = playing ?? _map ?? maps.FirstOrDefault();
            if (wanted is not null && maps.Contains(wanted, StringComparer.OrdinalIgnoreCase))
                MapBox.SelectedItem = maps.First(m => string.Equals(m, wanted, StringComparison.OrdinalIgnoreCase));
            else
                MapBox.SelectedItem = maps.FirstOrDefault();

            _map = MapBox.SelectedItem as string;
        }
        finally
        {
            _loading = false;
        }
        RefreshSections();
    }

    private void RefreshSections()
    {
        var sections = _map is null
            ? new List<CheckpointSection>()
            : CheckpointStore.SectionsFor(_map);

        SectionList.ItemsSource = sections;

        var any = sections.Count > 0;
        EmptyNote.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        ClearTimesButton.IsEnabled = any;
        DeleteMapButton.IsEnabled = any;
        ExportButton.IsEnabled = any;

        EmptyNote.Text = _map is null
            ? "No checkpoints anywhere yet. Play a level and press the capture key to drop your first."
            : $"Nothing on {_map} yet. Press the capture key in game to drop one, then another where the section ends.";
    }

    private void MapBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        _map = MapBox.SelectedItem as string;
        RefreshSections();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshMaps();

    // ------------------------------------------------------------ removing
    private void RemoveSection_Click(object sender, RoutedEventArgs e)
    {
        if (_map is null || sender is not Button { Tag: int number }) return;

        var answer = MessageBox.Show(
            $"Remove section {number} from {_map}?\n\nBoth its checkpoints go, and the times recorded on it.",
            "Mhodume", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) return;

        CheckpointStore.DeleteSection(_map, number);
        RefreshSections();
    }

    private void ClearTimes_Click(object sender, RoutedEventArgs e)
    {
        if (_map is null) return;
        var answer = MessageBox.Show(
            $"Forget every time recorded on {_map}?\n\nThe checkpoints stay where they are.",
            "Mhodume", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) return;

        CheckpointStore.ClearTimes(_map);
        RefreshSections();
    }

    private void DeleteMap_Click(object sender, RoutedEventArgs e)
    {
        if (_map is null) return;
        var answer = MessageBox.Show(
            $"Delete every checkpoint on {_map}?\n\nTheir times go with them.",
            "Mhodume", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.OK) return;

        CheckpointStore.DeleteMap(_map);
        RefreshMaps();
    }

    // ------------------------------------------------------------- sharing
    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_map is null) return;

        var dialog = new SaveFileDialog
        {
            Title = "Export checkpoints",
            FileName = $"{_map} checkpoints.txt",
            Filter = "Checkpoint files (*.txt)|*.txt|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            CheckpointStore.Export(_map, dialog.FileName);
            SetStatus($"Exported {_map} to {Path.GetFileName(dialog.FileName)}.");
        }
        catch (Exception ex)
        {
            SetStatus("Could not export: " + ex.Message);
        }
    }

    // ------------------------------------------------------------ drilling
    private void Drill_Click(object sender, RoutedEventArgs e)
    {
        if (_config is null || sender is not Button { Tag: int number }) return;

        _config.TrainSection = number;
        RefreshDrillBar();
        SetStatus($"Drilling section {number}. Finish it and you go straight back.");
    }

    /// <summary>
    /// One trip to a section. The counter is bumped rather than a flag set, so
    /// pressing Go twice for the same section takes you twice.
    /// </summary>
    private void GoTo_Click(object sender, RoutedEventArgs e)
    {
        if (_config is null || sender is not Button { Tag: int number }) return;

        _config.GoSection = number;
        _config.GoRequest += 1;
        SetStatus($"Going to section {number}. Needs training mode in game.");
    }

    private void StopDrill_Click(object sender, RoutedEventArgs e)
    {
        if (_config is null) return;
        _config.TrainSection = 0;
        RefreshDrillBar();
    }

    private void RefreshDrillBar()
    {
        var section = _config?.TrainSection ?? 0;
        DrillBar.Visibility = section > 0 ? Visibility.Visible : Visibility.Collapsed;
        DrillTitle.Text = $"Drilling section {section}";
    }

    private void ExportSection_Click(object sender, RoutedEventArgs e)
    {
        if (_map is null || sender is not Button { Tag: int number }) return;

        var dialog = new SaveFileDialog
        {
            Title = $"Export section {number}",
            FileName = $"{_map} section {number}.txt",
            Filter = "Checkpoint files (*.txt)|*.txt|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            CheckpointStore.ExportSection(_map, number, dialog.FileName);
            ShareNote.Text = $"Exported section {number} to {Path.GetFileName(dialog.FileName)}.";
            SetStatus($"Section {number} exported");
        }
        catch (Exception ex)
        {
            SetStatus("Could not export: " + ex.Message);
        }
    }

    /// <summary>
    /// Says what happened where it will be seen. The share drawer is folded
    /// away most of the time, so a note written only there goes unread.
    /// </summary>
    private void SetStatus(string text)
    {
        ShareNote.Text = text;
        ImportNote.Text = text;
        ImportNote.Visibility = Visibility.Visible;
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import checkpoints",
            Filter = "Checkpoint files (*.txt)|*.txt|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog() != true) return;

        // Replacing renumbers the sections, which is why it also clears the
        // times: they would otherwise be attached to stretches that moved.
        var answer = MessageBox.Show(
            "Replace your checkpoints on the maps in this file?\n\n" +
            "Yes — replace them, and forget the times recorded on those maps.\n" +
            "No — add these on the end of what you already have.",
            "Mhodume", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Cancel) return;

        try
        {
            var (maps, points) = CheckpointStore.Import(dialog.FileName,
                                                        replace: answer == MessageBoxResult.Yes);
            SetStatus(points == 0
                ? "Nothing in that file looked like a checkpoint."
                : $"Imported {points} checkpoint(s) across {maps} map(s).");
            RefreshMaps();
        }
        catch (Exception ex)
        {
            SetStatus("Could not import: " + ex.Message);
        }
    }
}
