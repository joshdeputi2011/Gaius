using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Gaius.Office;

public sealed partial class EditorWindow
{
    private readonly TextBlock details = UI.Text("", 11, "#586b83");
    private readonly TextBlock zoomLabel = UI.Text("100%", 11, "#394b65");
    private readonly Slider zoomSlider = new() { Minimum = 25, Maximum = 400, Value = 100, StepFrequency = 5, Width = 140, MinHeight = 24 };
    private ScrollViewer? zoomHost;
    private bool updatingZoom;
    private Grid BuildStatusBar()
    {
        var footer = new Grid { Padding = new Thickness(12, 2, 12, 2), MinHeight = 34, ColumnSpacing = 16, Background = Appearance.Glass(), BorderBrush = UI.Brush("#d6e0eb"), BorderThickness = new Thickness(0, 1, 0, 0) };
        void RefreshGlass() => footer.Background = Appearance.Glass();
        Appearance.Changed += RefreshGlass; Closed += (_, _) => Appearance.Changed -= RefreshGlass;
        footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var info = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18, VerticalAlignment = VerticalAlignment.Center }; status.VerticalAlignment = VerticalAlignment.Center; info.Children.Add(status); info.Children.Add(details); footer.Children.Add(info);
        var zoom = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        Button Tool(string label, string glyph, Action action) { var button = new Button { Content = new FontIcon { Glyph = glyph, FontSize = 12 }, Padding = new Thickness(6), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0), MinHeight = 26 }; Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label); ToolTipService.SetToolTip(button, label); button.Click += (_, _) => action(); return button; }
        zoom.Children.Add(Tool("Fit to window", "\uE9A6", FitZoom)); zoom.Children.Add(Tool("Zoom out", "\uE738", () => SetZoom(zoomSlider.Value - 10))); zoom.Children.Add(zoomSlider); zoom.Children.Add(Tool("Zoom in", "\uE710", () => SetZoom(zoomSlider.Value + 10))); zoom.Children.Add(zoomLabel); Grid.SetColumn(zoom, 1); footer.Children.Add(zoom);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(zoomSlider, "Zoom"); zoomSlider.ValueChanged += (_, args) => { zoomLabel.Text = $"{args.NewValue:0}%"; if (!updatingZoom) SetZoom(args.NewValue); };
        return footer;
    }
    private void SetZoom(double percentage)
    {
        percentage = Math.Clamp(percentage, 25, 400); updatingZoom = true; zoomSlider.Value = percentage; zoomLabel.Text = $"{percentage:0}%"; updatingZoom = false;
        zoomHost?.ChangeView(null, null, (float)percentage / 100, true);
    }
    private void FitZoom()
    {
        if (zoomHost?.Content is FrameworkElement content && content.ActualWidth > 0) SetZoom(Math.Min(100, zoomHost.ViewportWidth / (content.ActualWidth + content.Margin.Left + content.Margin.Right) * 100));
    }
    private void BindZoom(ScrollViewer host)
    {
        zoomHost = host; host.ZoomMode = ZoomMode.Enabled; host.MinZoomFactor = .25f; host.MaxZoomFactor = 4;
        host.ViewChanged += (_, _) => { updatingZoom = true; zoomSlider.Value = host.ZoomFactor * 100; zoomLabel.Text = $"{host.ZoomFactor * 100:0}%"; updatingZoom = false; };
        host.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler((_, args) => {
            if ((Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & Windows.UI.Core.CoreVirtualKeyStates.Down) == 0) return;
            args.Handled = true; double next = Math.Clamp(host.ZoomFactor * Math.Pow(1.12, args.GetCurrentPoint(host).Properties.MouseWheelDelta / 120.0), .25, 4); SetZoom(next * 100);
        }), true);
        var root = (FrameworkElement)Content;
        void Shortcut(VirtualKey key, Action action) { var accelerator = new KeyboardAccelerator { Key = key, Modifiers = VirtualKeyModifiers.Control }; accelerator.Invoked += (_, args) => { action(); args.Handled = true; }; root.KeyboardAccelerators.Add(accelerator); }
        Shortcut(VirtualKey.Add, () => SetZoom(zoomSlider.Value + 10)); Shortcut(VirtualKey.Subtract, () => SetZoom(zoomSlider.Value - 10)); Shortcut(VirtualKey.Number0, () => SetZoom(100));
    }
    private void EnsureZoomHost()
    {
        if (zoomHost is not null) return;
        // Keep a finite layout viewport so sheet row virtualization stays active.
        var surface = new Grid(); foreach (var child in body.Children.ToArray()) { body.Children.Remove(child); surface.Children.Add(child); }
        var host = new ScrollViewer { Content = surface, HorizontalScrollMode = ScrollMode.Enabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        body.Children.Add(host);
        void Size() { if (body.ActualWidth > 0 && body.ActualHeight > 0) { surface.Width = body.ActualWidth; surface.Height = body.ActualHeight; } }
        body.SizeChanged += (_, _) => Size(); host.Loaded += (_, _) => Size(); BindZoom(host);
    }
}
