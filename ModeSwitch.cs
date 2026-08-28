using System.Threading.Tasks;

namespace Mhodume;

/// <summary>The two ways to run the game.</summary>
public enum Mode
{
    /// <summary>Loader present: crosshair, trajectory, checkpoints, the overlay.
    /// Runs cannot be submitted, by the game's own rule.</summary>
    Training,

    /// <summary>Loader absent: a plain, unmodified game whose runs count.</summary>
    Compete,
}

/// <summary>
/// Enters a mode: puts the loader in the state that mode needs, then starts the
/// game through Steam. If the game is already running it is closed first, so the
/// loader can be renamed — a loaded DLL cannot be.
///
/// This is the whole of the switch. Everything a mode means is decided here,
/// before the game starts, because the one thing that decides eligibility — is
/// the loader present — can only be changed while the game is closed.
/// </summary>
public static class ModeSwitch
{
    public sealed record Result(bool Ok, string Message);

    /// <summary>
    /// The mode the loader is currently set up for, regardless of whether the
    /// game is running. Null when the game or the loader cannot be found.
    /// </summary>
    public static Mode? CurrentMode()
    {
        ModLoader.BinariesDir = Game.BinariesDir;
        return ModLoader.Current switch
        {
            ModLoader.State.Enabled => Mode.Training,
            ModLoader.State.Disabled => Mode.Compete,
            _ => null,
        };
    }

    /// <summary>
    /// Reports progress ("closing the game", "switching to compete", "starting")
    /// so the window can show what is happening during the few seconds a switch
    /// takes.
    /// </summary>
    public static async Task<Result> Enter(Mode mode, Action<string> progress)
    {
        var binaries = Game.BinariesDir;
        if (binaries is null)
            return new Result(false, "VHOLUME was not found. Is it installed through Steam?");
        ModLoader.BinariesDir = binaries;

        if (ModLoader.Current == ModLoader.State.NotInstalled)
            return new Result(false,
                "The mod is not installed in the game folder yet.");

        // The loader can only be renamed with the game closed.
        if (Game.IsRunning)
        {
            progress("Closing the game…");
            var closed = await Task.Run(() => Game.CloseAndWait(TimeSpan.FromSeconds(15)));
            if (!closed)
                return new Result(false, "The game would not close. Close it and try again.");
        }

        // Training resumes the last level the mod knew. status.txt holds it,
        // written by the mod while it was last running; in compete there is no
        // mod, so this is the level from before the switch — where you want to
        // come back to. Written before launch so the mod reads it on startup.
        if (mode == Mode.Training)
        {
            try
            {
                var lastMap = ConfigStore.ReadCurrentMap();
                var store = new ConfigStore();
                var cfg = store.LoadLive();
                cfg.Tweaks.StartupLevel = lastMap ?? "";
                store.FlushLive(cfg);
            }
            catch { /* resuming is a convenience; never block a launch on it */ }
        }

        var wantEnabled = mode == Mode.Training;
        var isEnabled = ModLoader.Current == ModLoader.State.Enabled;
        if (wantEnabled != isEnabled)
        {
            progress(mode == Mode.Training
                ? "Turning the mod on…"
                : "Turning the mod off for a clean run…");
            var problem = ModLoader.Toggle();
            if (problem is not null)
                return new Result(false, problem);
        }

        progress("Starting VHOLUME through Steam…");
        try
        {
            Game.LaunchThroughSteam();
        }
        catch (Exception ex)
        {
            return new Result(false, "Could not start the game through Steam: " + ex.Message);
        }

        return new Result(true, mode == Mode.Training
            ? "Training — the mod is on. Runs will not be submitted."
            : "Compete — the game is clean. Runs count.");
    }
}
