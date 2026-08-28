using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Mhodume;

public partial class SpeedPage : UserControl
{
    private static readonly string[] SwatchColors =
    {
        "#FFFFFF", "#00E676", "#00E5FF", "#FFEB3B",
        "#FF3D3D", "#FF9100", "#7C4DFF", "#9E9E9E",
    };

    private SpeedConfig? _config;

    public SpeedPage()
    {
        InitializeComponent();
        BuildSwatches();

        DataContextChanged += (_, e) => _config = e.NewValue as SpeedConfig;
    }

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
            swatch.MouseLeftButtonUp += (_, _) =>
            {
                if (_config is not null) _config.TextColor = color;
            };
            Swatches.Items.Add(swatch);
        }
    }
}
