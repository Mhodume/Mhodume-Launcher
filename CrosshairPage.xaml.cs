using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace Mhodume;

public partial class CrosshairPage : UserControl
{
    public record ShapeOption(string Key, string Label);

    private static readonly ShapeOption[] Shapes =
    {
        new("cross",      "Cross"),
        new("cross_dot",  "Cross + dot"),
        new("dot",        "Dot only"),
        new("tcross",     "T cross"),
        new("circle",     "Circle"),
        new("circle_dot", "Circle + dot"),
    };

    private static readonly string[] SwatchColors =
    {
        "#00E676", "#00E5FF", "#FFFFFF", "#FFEB3B",
        "#FF3D3D", "#FF00E5", "#FF9100", "#7C4DFF",
        "#000000", "#9E9E9E",
    };

    private CrosshairConfig? _config;

    /// <summary>
    /// Points the embedded speed block at its own config section. The page's
    /// own DataContext is the crosshair, so the block cannot inherit it.
    /// </summary>
    public void SetSpeedContext(SpeedConfig speed) => SpeedSection.DataContext = speed;

    public CrosshairPage()
    {
        InitializeComponent();

        ShapeBox.ItemsSource = Shapes;
        BackdropBox.ItemsSource = new[] { "Dark", "Light", "Checker" };
        BackdropBox.SelectedIndex = 0;
        BuildSwatches();

        Preview.SetBinding(CrosshairPreview.ZoomProperty, new Binding("Value") { Source = ZoomSlider });

        DataContextChanged += (_, e) =>
        {
            if (_config is not null) _config.PropertyChanged -= Config_PropertyChanged;
            _config = e.NewValue as CrosshairConfig;
            if (_config is not null) _config.PropertyChanged += Config_PropertyChanged;

            Preview.Config = _config;
            UpdateHexBox();
            UpdateCircleVisibility();
            Preview.InvalidateVisual();
        };
    }

    private void Config_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Preview.InvalidateVisual();

        if (e.PropertyName == nameof(CrosshairConfig.Shape))
            UpdateCircleVisibility();

        if (e.PropertyName is nameof(CrosshairConfig.Color) or nameof(CrosshairConfig.MainColor))
            UpdateHexBox();
    }

    private void UpdateCircleVisibility()
    {
        CirclePanel.Visibility = _config?.Shape is "circle" or "circle_dot"
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void BackdropBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        Preview.Backdrop = (BackdropBox.SelectedItem as string) switch
        {
            "Light"   => "light",
            "Checker" => "checker",
            _         => "dark",
        };
    }

    // ------------------------------------------------------------------ colour
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
            swatch.MouseLeftButtonUp += (_, _) => ApplyColor(color);
            Swatches.Items.Add(swatch);
        }
    }

    private void ApplyColor(Color c)
    {
        if (_config is null) return;
        // keep whatever opacity the slider is set to
        var alpha = _config.MainColor.A;
        _config.MainColor = Color.FromArgb(alpha, c.R, c.G, c.B);
    }

    private void UpdateHexBox()
    {
        if (_config is null) return;
        var c = _config.MainColor;
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
            ApplyColor((Color)ColorConverter.ConvertFromString(text)!);
        }
        catch
        {
            UpdateHexBox();     // reject silently and show the real value back
        }
    }
}
