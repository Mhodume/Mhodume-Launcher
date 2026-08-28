using System.Windows.Controls;
using System.Windows.Media;

namespace Mhodume;

public partial class TrainingPage : UserControl
{
    public TrainingPage() => InitializeComponent();

    /// <summary>
    /// Reflects what the mod reports, so the page shows the live state rather
    /// than only the settings. Called from the main window's game watch.
    /// </summary>
    public void UpdateState(bool running, bool training, bool tainted)
    {
        if (!running)
        {
            StateTitle.Text = "VHOLUME is not running";
            StateTitle.Foreground = (Brush)FindResource("Text");
            StateBody.Text = "Start the game to see whether training mode is on.";
            return;
        }

        if (training)
        {
            StateTitle.Text = "Training mode is ON";
            StateTitle.Foreground = (Brush)FindResource("Accent");
            StateBody.Text = "The trajectory and freecam are available. This lap will not count. "
                           + "Press F7 in game to leave training mode.";
        }
        else if (tainted)
        {
            StateTitle.Text = "Training mode is off — but this lap is spent";
            StateTitle.Foreground = (Brush)FindResource("Text");
            StateBody.Text = "A training feature was used during this lap, so it stays uncountable. "
                           + "Restart the level for a clean run.";
        }
        else
        {
            StateTitle.Text = "Training mode is off";
            StateTitle.Foreground = (Brush)FindResource("Text");
            StateBody.Text = "Laps count normally. Press F7 in game to turn training mode on.";
        }
    }
}
