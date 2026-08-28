using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace Mhodume;

public partial class ProfilesPage : UserControl
{
    private ConfigStore? _store;
    private Func<ModConfig>? _current;

    /// <summary>Raised when the user picks a profile that should become active.</summary>
    public event Action<ModConfig, string>? ProfileLoaded;

    public ProfilesPage() => InitializeComponent();

    /// <summary>Wires the page to the store and to the currently edited config.</summary>
    public void Initialize(ConfigStore store, Func<ModConfig> currentConfig)
    {
        _store = store;
        _current = currentConfig;
        Refresh(_store.LoadLastProfile());
    }

    public void Refresh(string? select = null)
    {
        if (_store is null) return;

        // The selection is restored with nobody listening. Subscribing first
        // and selecting after means restoring the selection raises the event,
        // and the handler applies the profile - so simply starting the app
        // wrote the last profile over whatever settings were in use.
        var target = select ?? ProfileList.SelectedItem as string;
        ProfileList.SelectionChanged -= ProfileList_SelectionChanged;
        ProfileList.ItemsSource = _store.ListProfiles().ToList();

        if (target is not null && ProfileList.Items.Contains(target))
            ProfileList.SelectedItem = target;

        ProfileList.SelectionChanged += ProfileList_SelectionChanged;
    }

    private void ProfileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_store is null || ProfileList.SelectedItem is not string name) return;
        var cfg = _store.LoadProfile(name);
        if (cfg is null) return;

        ShowDetails(name, cfg);
        ProfileLoaded?.Invoke(cfg, name);
    }

    private void ShowDetails(string name, ModConfig cfg)
    {
        DetailTitle.Text = name;

        var c = cfg.Crosshair;
        var shape = c.Shape switch
        {
            "cross"      => "cross",
            "cross_dot"  => "cross with centre dot",
            "dot"        => "dot only",
            "tcross"     => "T cross",
            "circle"     => "circle",
            "circle_dot" => "circle with centre dot",
            _            => c.Shape,
        };

        var col = c.MainColor;
        var hud = cfg.Hud.Manage
            ? $"speedometer {OnOff(cfg.Hud.ShowSpeedometer)}, timer {OnOff(cfg.Hud.ShowTimer)}, " +
              $"splits {OnOff(cfg.Hud.ShowCheckpointTime)}"
            : "left to the game's own options";

        var freecam = cfg.Freecam.Enabled
            ? $"on, {cfg.Freecam.Key}, {cfg.Freecam.Speed:0} cm/s"
            : "off";

        DetailBody.Text =
            $"Crosshair — {shape}, {c.Thickness:0} px thick, {c.Gap:0} px gap, " +
            $"#{col.R:X2}{col.G:X2}{col.B:X2} at {c.OpacityPercent:0} % opacity." +
            (c.Outline ? $" Outlined ({c.OutlineThickness:0} px)." : " No outline.") +
            (c.Tilt ? $" Follows camera tilt at {c.TiltFactor:0.00}×." : " Fixed, no tilt.") +
            $"\n\nHUD — {hud}." +
            $"\n\nFreecam — {freecam}.";
    }

    private static string OnOff(bool b) => b ? "on" : "off";

    // ------------------------------------------------------------------ actions
    private void New_Click(object sender, RoutedEventArgs e)
    {
        if (_store is null) return;
        var name = PromptName("New profile", "Unnamed");
        if (name is null) return;
        _store.SaveProfile(name, new ModConfig());
        Refresh(ConfigStore.Sanitize(name));
    }

    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (_store is null || _current is null) return;
        var source = ProfileList.SelectedItem as string ?? "Profile";
        var name = PromptName("Duplicate profile", source + " copy");
        if (name is null) return;
        _store.SaveProfile(name, _current().Clone());
        Refresh(ConfigStore.Sanitize(name));
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_store is null || _current is null) return;

        if (ProfileList.SelectedItem is string name)
        {
            _store.SaveProfile(name, _current());
            ShowDetails(name, _current());
        }
        else
        {
            var newName = PromptName("Save as", "My crosshair");
            if (newName is null) return;
            _store.SaveProfile(newName, _current());
            Refresh(ConfigStore.Sanitize(newName));
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_store is null || ProfileList.SelectedItem is not string name) return;

        var answer = MessageBox.Show(
            $"Delete the profile “{name}” permanently?",
            "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        _store.DeleteProfile(name);
        Refresh();
        DetailTitle.Text = "No profile selected";
        DetailBody.Text = "Pick a profile on the left to see what it contains.";
    }

    private void Reveal_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(ConfigStore.ProfilesDir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not open the folder: " + ex.Message, "Mhodume");
        }
    }

    /// <summary>Small inline input box, to avoid pulling in a dialog library.</summary>
    private string? PromptName(string title, string initial)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 340,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            ResizeMode = ResizeMode.NoResize,
            Background = (System.Windows.Media.Brush)FindResource("Bg"),
        };

        var box = new TextBox { Text = initial, Margin = new Thickness(0, 0, 0, 12) };
        var ok = new Button { Content = "OK", Width = 90, IsDefault = true };
        var cancel = new Button { Content = "Cancel", Width = 90, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(box);
        panel.Children.Add(buttons);
        dialog.Content = panel;

        bool accepted = false;
        ok.Click += (_, _) => { accepted = true; dialog.Close(); };
        dialog.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        dialog.ShowDialog();

        if (!accepted) return null;
        var name = box.Text.Trim();
        if (string.IsNullOrWhiteSpace(name)) return null;

        if (_store is not null && _store.ProfileExists(name))
        {
            var overwrite = MessageBox.Show(
                $"A profile called “{name}” already exists. Replace it?",
                "Profile exists", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (overwrite != MessageBoxResult.Yes) return null;
        }
        return name;
    }
}
