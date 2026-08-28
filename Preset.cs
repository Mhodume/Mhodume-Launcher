using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mhodume;

/// <summary>
/// A saved training setup: a map, a fixed set of checkpoints, and the times you
/// have made against them.
///
/// Presets are how a practice layout outlives one session. The checkpoints you
/// drop in game live in one file per map and are overwritten the next time you
/// drop different ones; a preset takes a copy of them under a name, so several
/// layouts on the same map can each be kept and returned to.
///
/// Times belong to the preset, not to the map, because two presets on one map
/// have different checkpoints and so different sections. They are folded in
/// from the mod's own splits while the preset is the loaded one, and stored
/// here so a preset shows its own bests wherever it is viewed.
/// </summary>
public class Preset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Map { get; set; } = "";

    public List<CheckpointPoint> Checkpoints { get; set; } = new();

    /// <summary>Best time per section number, in seconds.</summary>
    public Dictionary<int, double> SectionBests { get; set; } = new();

    /// <summary>Best complete run, first checkpoint to last, in seconds.</summary>
    public double? GlobalBestSeconds { get; set; }

    [JsonIgnore]
    public int SectionCount => Checkpoints.Count / 2;

    [JsonIgnore]
    public string GlobalBestText =>
        GlobalBestSeconds is double s ? FormatTime(s) : "—";

    public static string FormatTime(double seconds)
    {
        if (seconds < 0) seconds = 0;
        var m = (int)(seconds / 60);
        var rest = seconds - m * 60;
        return m > 0 ? $"{m}:{rest:00.000}" : $"{rest:0.000}";
    }
}

/// <summary>
/// Loads and saves the presets, and folds the mod's live times into them.
///
/// One JSON file beside the config. The mod never reads it — a preset reaches
/// the game only by being written into checkpoints.txt, the file the mod does
/// read — so this is entirely the app's own.
/// </summary>
public static class PresetStore
{
    private static readonly string Path_ =
        System.IO.Path.Combine(ConfigStore.RootDir, "presets.json");

    private static readonly JsonSerializerOptions Options =
        new() { WriteIndented = true };

    public static List<Preset> Load()
    {
        try
        {
            if (File.Exists(Path_))
                return JsonSerializer.Deserialize<List<Preset>>(File.ReadAllText(Path_))
                       ?? new List<Preset>();
        }
        catch { /* a file that will not parse is replaced by the next save */ }
        return new List<Preset>();
    }

    public static void Save(List<Preset> presets)
    {
        try
        {
            Directory.CreateDirectory(ConfigStore.RootDir);
            var tmp = Path_ + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(presets, Options));
            File.Move(tmp, Path_, overwrite: true);
        }
        catch { /* saving a preset is not worth crashing over */ }
    }

    /// <summary>
    /// Makes a preset from the checkpoints currently dropped on a map. Returns
    /// null when there are none — an empty preset is nothing to save.
    /// </summary>
    public static Preset? FromCurrent(string name, string map)
    {
        var checkpoints = CheckpointStore.ReadCheckpoints();
        if (!checkpoints.TryGetValue(map, out var points) || points.Count == 0)
            return null;

        // A new preset starts with no times. Folding the map's current splits
        // would pull in whatever the last layout on this map recorded, and a
        // complete-run best from a different number of checkpoints is exactly
        // the wrong-looking global this avoids. Times build up as you run it.
        return new Preset { Name = name, Map = map, Checkpoints = points };
    }

    /// <summary>
    /// Updates a preset's stored bests from the mod's current splits for its
    /// map — called while that preset is the loaded one, so its own times are
    /// what get folded in. Bests only improve; a slower run never overwrites.
    /// </summary>
    public static void FoldTimes(Preset preset)
    {
        var splits = CheckpointStore.ReadSplits();
        if (splits.TryGetValue(preset.Map, out var sections))
        {
            foreach (var (index, times) in sections)
            {
                if (times.Best is not double best) continue;
                if (!preset.SectionBests.TryGetValue(index, out var have) || best < have)
                    preset.SectionBests[index] = best;
            }
        }

        var runs = CheckpointStore.ReadRunBests();
        if (runs.TryGetValue(preset.Map, out var global))
        {
            if (preset.GlobalBestSeconds is not double g || global < g)
                preset.GlobalBestSeconds = global;
        }
    }
}
