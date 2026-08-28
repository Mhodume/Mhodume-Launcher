namespace Mhodume;

/// <summary>
/// The file work behind "train this preset", shared by the launcher and the
/// overlay: put the preset's checkpoints where the mod reads them, and clear
/// the map's times so the preset starts on its own clean slate.
///
/// What each caller does after differs — the launcher starts the game, the
/// overlay sends you to the map in the running one — but the preparation is the
/// same, and it belongs in one place so the two cannot drift.
/// </summary>
public static class PresetActions
{
    /// <summary>
    /// Loads a preset's layout: its checkpoints become the map's active set,
    /// and the map's times are cleared. Times are per preset, so a different
    /// preset's times on the same map would only confuse this one.
    /// </summary>
    public static void LoadLayout(Preset preset)
    {
        CheckpointStore.SetMapCheckpoints(preset.Map, preset.Checkpoints);
        CheckpointStore.ClearMapSplits(preset.Map);
    }
}
