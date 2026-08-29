using System.Windows;
using System.Windows.Controls;

namespace Mhodume;

/// <summary>
/// Saved practice layouts: save the checkpoints you have on the current map
/// under a name, and pick one to train — its checkpoints go back in place and
/// its times pick up where they were.
///
/// Training a preset in the overlay writes its layout, then sends you to its
/// map in the running game; the launcher does the same before it starts the
/// game. Both go through <see cref="PresetActions"/>, so the preparation is one
/// piece of code however you got here.
/// </summary>
public partial class PresetsPage : UserControl
{
    /// <summary>One section's best time, for the details list.</summary>
    public record SectionRow(string Label, string BestText);

    private ConfigStore? _store;
    private Func<ModConfig>? _config;
    private Func<string?>? _currentMap;

    private List<Preset> _presets = new();

    public PresetsPage()
    {
        InitializeComponent();
        IsVisibleChanged += (_, e) => { if ((bool)e.NewValue) Refresh(); };
    }

    public void Initialize(ConfigStore store, Func<ModConfig> config, Func<string?> currentMap)
    {
        _store = store;
        _config = config;
        _currentMap = currentMap;
    }

    private void Refresh()
    {
        _presets = PresetStore.Load();
        var selectedId = (PresetList.SelectedItem as Preset)?.Id;
        PresetList.ItemsSource = null;
        PresetList.ItemsSource = _presets;
        if (selectedId is not null)
            PresetList.SelectedItem = _presets.FirstOrDefault(p => p.Id == selectedId);

        var map = _currentMap?.Invoke();
        if (string.IsNullOrEmpty(map))
        {
            CurrentMapText.Text = "No map — start a level in game first.";
            SaveButton.IsEnabled = false;
        }
        else
        {
            var count = CheckpointStore.ReadCheckpoints().TryGetValue(map, out var pts) ? pts.Count : 0;
            CurrentMapText.Text = count == 0
                ? $"{MapNames.Display(map)} — no checkpoints dropped yet."
                : $"{MapNames.Display(map)} — {count / 2} section" + (count / 2 == 1 ? "" : "s") +
                  $" ({count} checkpoints).";
            SaveButton.IsEnabled = count > 0;
        }
    }

    // ------------------------------------------------------------- saving
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var map = _currentMap?.Invoke();
        if (string.IsNullOrEmpty(map)) return;

        var name = NameBox.Text.Trim();
        if (name.Length == 0) { SaveNote.Text = "Give the preset a name first."; return; }

        var preset = PresetStore.FromCurrent(name, map);
        if (preset is null) { SaveNote.Text = "There are no checkpoints on this map to save."; return; }

        _presets.Add(preset);
        PresetStore.Save(_presets);
        NameBox.Text = "";
        SaveNote.Text = $"Saved “{name}”.";
        Refresh();
        PresetList.SelectedItem = _presets.FirstOrDefault(p => p.Id == preset.Id);
    }

    // ------------------------------------------------------------- details
    private void PresetList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PresetList.SelectedItem is not Preset preset)
        {
            Detail.Visibility = Visibility.Collapsed;
            EmptyDetail.Visibility = Visibility.Visible;
            return;
        }

        // Fold in the mod's fresh times only for the preset that is actually
        // loaded: two presets on one map share the map's splits, so folding for
        // an unloaded one would hand it the loaded one's times.
        if (preset.Id == PresetStore.ActiveId())
        {
            PresetStore.FoldTimes(preset);
            PresetStore.Save(_presets);
        }

        EmptyDetail.Visibility = Visibility.Collapsed;
        Detail.Visibility = Visibility.Visible;
        DetailName.Text = preset.Name;
        DetailMap.Text = $"{MapNames.Display(preset.Map)} · {preset.SectionCount} section" +
                         (preset.SectionCount == 1 ? "" : "s");

        var rows = new List<SectionRow>();
        for (var i = 1; i <= preset.SectionCount; i++)
        {
            var best = preset.SectionBests.TryGetValue(i, out var b) ? Preset.FormatTime(b) : "—";
            rows.Add(new SectionRow($"Section {i}", best));
        }
        Sections.ItemsSource = rows;
        TrainNote.Text = "";
    }

    // ------------------------------------------------------------- training
    private void Train_Click(object sender, RoutedEventArgs e)
    {
        if (PresetList.SelectedItem is not Preset preset) return;
        if (_config is null || _store is null) return;

        // Already on this preset's map? Then only its checkpoints need to
        // change — the mod picks those up from the file with no reload. Sending
        // a goto here would reload the level and reset your run timer mid-run,
        // which is exactly the wrong thing when you are already there.
        var alreadyHere = string.Equals(_currentMap?.Invoke(), preset.Map,
                                        StringComparison.OrdinalIgnoreCase);

        PresetActions.LoadLayout(preset);

        var cfg = _config();
        cfg.Tweaks.StartupLevel = preset.Map;
        if (!alreadyHere)
        {
            cfg.Tweaks.GotoLevel = preset.Map;
            cfg.Tweaks.GotoRequest += 1;
        }
        _store.FlushLive(cfg);

        TrainNote.Text = alreadyHere
            ? "Checkpoints loaded. Run from your spawn — the timer is already going."
            : "Loading its checkpoints and going to the map — needs training mode.";
    }

    /// <summary>
    /// Clears a preset's stored times. The folded best only ever improves, so a
    /// wrong time set once — a layout that has since changed — would otherwise
    /// stick; this is how you get rid of it and let the times rebuild.
    /// </summary>
    private void ResetTimes_Click(object sender, RoutedEventArgs e)
    {
        if (PresetList.SelectedItem is not Preset preset) return;
        preset.SectionBests.Clear();
        PresetStore.Save(_presets);
        PresetList_SelectionChanged(this, null!);
        TrainNote.Text = "Times cleared — they rebuild as you run.";
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (PresetList.SelectedItem is not Preset preset) return;
        _presets.RemoveAll(p => p.Id == preset.Id);
        PresetStore.Save(_presets);
        Refresh();
    }
}
