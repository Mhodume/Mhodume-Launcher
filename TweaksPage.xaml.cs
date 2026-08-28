using System.Windows;
using System.Windows.Controls;

namespace Mhodume;

public partial class TweaksPage : UserControl
{
    public TweaksPage() => InitializeComponent();

    /// <summary>
    /// Asks the game to build the current level again.
    ///
    /// A counter rather than a flag, so pressing it twice is two loads. The
    /// mod acts on the number changing; a flag already set says nothing new.
    /// </summary>
    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not TweaksConfig cfg) return;
        cfg.ReloadRequest++;
        ReloadSaid.Text = "Asked. The game takes a moment, and the lap counts again after.";
    }
}
