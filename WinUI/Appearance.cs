using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace Gaius.Office;

internal static class Appearance
{
    private static readonly Dictionary<string, SolidColorBrush> Brushes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<WeakReference<Window>> Windows = [];
    private static readonly UISettings SystemSettings = new();
    private static Microsoft.UI.Dispatching.DispatcherQueue? dispatcher;
    public static event Action? Changed;
    public static bool Dark => SuiteSettings.Theme == "dark" || SuiteSettings.Theme == "system" && SystemSettings.GetColorValue(UIColorType.Background).R < 128;
    public static bool DarkPaper => Dark && !SuiteSettings.WhitePage;
    static Appearance() => SystemSettings.ColorValuesChanged += (_, _) => {
        // UISettings raises this event on a worker thread. Cache the application
        // dispatcher while registering a live window rather than touching a
        // native Window that may be closing on the UI thread.
        System.Threading.Volatile.Read(ref dispatcher)?.TryEnqueue(Refresh);
    };
    public static SolidColorBrush Brush(string hex)
    {
        if (!Brushes.TryGetValue(hex, out var brush)) { brush = new SolidColorBrush(UI.Color(Map(hex))); Brushes[hex] = brush; }
        return brush;
    }
    private static string Map(string hex)
    {
        if (!Dark) return hex;
        var c = UI.Color(hex);
        if (Math.Abs(c.R - c.G) < 40 && Math.Abs(c.G - c.B) < 45) {
            if (c.R >= 225 && c.G >= 225 && c.B >= 225) return "#253145";
            if (c.R >= 185 && c.G >= 185 && c.B >= 185) return "#46566e";
            if (c.R <= 80 && c.G <= 100 && c.B <= 135) return "#e5edf8";
            if (c.R < 180) return "#abbcd2";
        }
        return hex;
    }
    public static Brush PaperBrush => new SolidColorBrush(UI.Color(DarkPaper ? "#222b39" : "#ffffff"));
    public static Brush PaperText => new SolidColorBrush(UI.Color(DarkPaper ? "#e5edf8" : "#242424"));
    public static void Register(Window window)
    {
        WindowLifetime.Register(window);
        System.Threading.Interlocked.CompareExchange(ref dispatcher, window.DispatcherQueue, null);
        lock (Windows) Windows.Add(new(window));
        window.Closed += (_, _) => { lock (Windows) Windows.RemoveAll(reference => !reference.TryGetTarget(out var target) || target == window); };
        if (window.Content is FrameworkElement root) { root.Loaded += (_, _) => Apply(window); Apply(window); }
    }
    private static void Apply(Window window)
    {
        if (WindowLifetime.IsClosed(window)) return;
        if (window.Content is FrameworkElement root) {
            root.RequestedTheme = Dark ? ElementTheme.Dark : ElementTheme.Light;
            if (root is Panel panel) panel.Background = SuiteSettings.Acrylic ? new SolidColorBrush(Microsoft.UI.Colors.Transparent) : Brush("#eef2f8");
        }
        if (SuiteSettings.Acrylic && window.SystemBackdrop is not DesktopAcrylicBackdrop) window.SystemBackdrop = new DesktopAcrylicBackdrop();
        else if (!SuiteSettings.Acrylic && window.SystemBackdrop is not null) window.SystemBackdrop = null;
        window.AppWindow.TitleBar.ButtonForegroundColor = UI.Color(Dark ? "#e5edf8" : "#25344b");
        window.AppWindow.TitleBar.ButtonInactiveForegroundColor = UI.Color(Dark ? "#abbcd2" : "#64748b");
    }
    public static void Refresh()
    {
        foreach (var (hex, brush) in Brushes.ToArray()) brush.Color = UI.Color(Map(hex));
        WeakReference<Window>[] snapshot; lock (Windows) snapshot = Windows.ToArray();
        foreach (var reference in snapshot) if (reference.TryGetTarget(out var window)) Apply(window);
        Changed?.Invoke();
    }
    public static Brush Glass() => new AcrylicBrush { TintColor = UI.Color(Dark ? "#253145" : "#eef2f8"), TintOpacity = .68, FallbackColor = UI.Color(Dark ? "#253145" : "#eef2f8") };
}
