using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace Gaius.Office;

// Small native vector icons share the ribbon's foreground and switch from an
// outline to a solid silhouette on the selected tab.
internal sealed class RibbonTabIcon : Grid
{
    private readonly Path shape;
    private readonly Path? detail;
    public RibbonTabIcon(string tab)
    {
        Width = Height = 16; IsHitTestVisible = false;
        string path = tab switch {
            "Home" => "M2,10 L10,3 L18,10 L16,10 L16,18 L12,18 L12,12 L8,12 L8,18 L4,18 L4,10 Z",
            "Insert" => "M3,3 L17,3 L17,17 L3,17 Z M6,10 L14,10 M10,6 L10,14",
            "Draw" or "Annotate" => "M3,14 L13,4 L17,8 L7,18 L2,19 Z M11,6 L15,10",
            "Design" => "M10,2 L18,10 L10,18 L2,10 Z M10,6 L14,10 L10,14 L6,10 Z",
            "Layout" or "Page Layout" or "Organize" => "M3,2 L17,2 L17,18 L3,18 Z M3,7 L17,7 M9,7 L9,18",
            "References" => "M4,2 L16,2 L16,18 L10,14 L4,18 Z",
            "Review" => "M3,2 L13,2 L17,6 L17,18 L3,18 Z M6,10 L9,13 L14,7",
            "View" => "M1,10 Q10,-1 19,10 Q10,21 1,10 Z M7,10 A3,3 0 1 0 13,10 A3,3 0 1 0 7,10",
            "Formulas" => "M16,3 L7,3 L7,10 L3,10 L7,10 L7,16 Q7,19 3,18 M5,7 L13,7",
            "Data" => "M3,3 L7,3 L7,17 L3,17 Z M9,8 L13,8 L13,17 L9,17 Z M15,5 L19,5 L19,17 L15,17 Z",
            "Transitions" or "Animations" => "M3,3 L13,10 L3,17 Z M12,3 L18,10 L12,17",
            "Slide Show" => "M2,3 L18,3 L18,14 L2,14 Z M10,14 L10,19 M6,19 L14,19 M8,6 L13,9 L8,12 Z",
            "Compress" => "M2,3 L8,9 L3,9 L8,9 L8,4 M18,17 L12,11 L17,11 L12,11 L12,16",
            "Convert" or "Transform" => "M2,6 L16,6 L12,2 M16,6 L12,10 M18,14 L4,14 L8,10 M4,14 L8,18",
            "Adjust" => "M4,2 L4,18 M10,2 L10,18 M16,2 L16,18 M1,6 L7,6 L7,9 L1,9 Z M7,12 L13,12 L13,15 L7,15 Z M13,4 L19,4 L19,7 L13,7 Z",
            _ => "M3,2 L13,2 L17,6 L17,18 L3,18 Z"
        };
        shape = (Path)XamlReader.Load($"<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='{path}' StrokeThickness='1.4' StrokeLineJoin='Round' />");
        var content = new Grid { Width = 20, Height = 20, Children = { shape } };
        string? cutout = tab switch { "Insert" => "M6,10 L14,10 M10,6 L10,14", "Review" => "M6,10 L9,13 L14,7", "Layout" or "Page Layout" or "Organize" => "M3,7 L17,7 M9,7 L9,18", "Slide Show" => "M8,6 L13,9 L8,12 Z", _ => null };
        if (cutout is not null) { detail = (Path)XamlReader.Load($"<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='{cutout}' StrokeThickness='1.5' StrokeLineJoin='Round' />"); content.Children.Add(detail); }
        Children.Add(new Viewbox { Child = content });
    }
    public void SetState(bool selected, Brush foreground, Brush background) { shape.Stroke = foreground; shape.Fill = selected ? foreground : null; shape.Opacity = selected ? 1 : .85; if (detail is not null) { detail.Visibility = selected ? Visibility.Visible : Visibility.Collapsed; detail.Stroke = background; } }
}
