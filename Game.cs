using System.Diagnostics;
using System.IO;

namespace Mhodume;

/// <summary>
/// Everything the launcher needs to know about VHOLUME on this machine: where
/// it is, whether it is running, and how to start and stop it.
///
/// The launcher never talks to the running game. It sets the loader state on
/// disk — a file rename, handled by <see cref="ModLoader"/> — and then starts
/// the game through Steam, which is what makes a run eligible: launched by
/// Steam, the process carries the Steam context the leaderboard submission
/// needs. Whether the mod is present is decided before launch, not during.
/// </summary>
public static class Game
{
    /// <summary>VHOLUME's Steam app id, used to launch it through Steam.</summary>
    public const string AppId = "4131730";

    private const string ProcessName = "VHOLUME-Win64-Shipping";

    /// <summary>The game's root folder (…/common/VHOLUME), or null if not found.</summary>
    public static string? RootDir { get; private set; } = FindRootDir();

    /// <summary>…/Binaries/Win64, where the loader and the exe live.</summary>
    public static string? BinariesDir =>
        RootDir is null ? null : Path.Combine(RootDir, "VHOLUME", "Binaries", "Win64");

    /// <summary>True while a VHOLUME process is running.</summary>
    public static bool IsRunning => Process.GetProcessesByName(ProcessName).Length > 0;

    /// <summary>
    /// Starts the game through Steam. Launching by Steam rather than by running
    /// the exe directly is deliberate: the run only counts if the process was
    /// started in a Steam context, and this is the plainest way to guarantee
    /// it. The loader state must already be what the chosen mode wants.
    /// </summary>
    public static void LaunchThroughSteam()
    {
        Process.Start(new ProcessStartInfo($"steam://rungameid/{AppId}")
        {
            UseShellExecute = true,
        });
    }

    /// <summary>
    /// Closes the game and waits for it to be gone, so the loader can be renamed
    /// afterwards — a loaded DLL cannot be renamed. Returns true once no process
    /// remains, false if one is still up after the timeout.
    /// </summary>
    public static bool CloseAndWait(TimeSpan timeout)
    {
        foreach (var p in Process.GetProcessesByName(ProcessName))
        {
            try { p.CloseMainWindow(); } catch { /* fall through to kill */ }
        }

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var running = Process.GetProcessesByName(ProcessName);
            if (running.Length == 0) return true;
            Thread.Sleep(200);
        }

        // A game that ignored the polite close is asked once, firmly. Losing an
        // unsaved menu state is a smaller cost than a switch that hangs.
        foreach (var p in Process.GetProcessesByName(ProcessName))
        {
            try { p.Kill(); } catch { /* nothing more we can do */ }
        }

        var hardDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < hardDeadline)
        {
            if (Process.GetProcessesByName(ProcessName).Length == 0) return true;
            Thread.Sleep(200);
        }
        return false;
    }

    private static string? FindRootDir()
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

            // Steam's own library index, for installs anywhere else.
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
        catch { /* best effort; the UI copes with a null path */ }
        return null;
    }
}
