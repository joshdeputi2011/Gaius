using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using System.Runtime.InteropServices;

namespace Gaius.Office;

internal static class WindowChrome
{
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    public static TitleBar Create(Window window, string title, string? kind = null, UIElement? content = null)
    {
        WindowLifetime.Register(window);
        if (window is EditorWindow or HomeWindow) {
            var work = DisplayArea.GetFromWindowId(window.AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
            double scale = Math.Max(96, GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(window))) / 96.0;
            int width = Math.Min((int)(1100 * scale), (int)(work.Width * .94)), height = Math.Min((int)(740 * scale), (int)(work.Height * .94));
            window.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(work.X + (work.Width - width) / 2, work.Y + (work.Height - height) / 2, width, height));
        }
        window.ExtendsContentIntoTitleBar = true;
        window.AppWindow.TitleBar.ButtonForegroundColor = UI.Color("#25344b"); window.AppWindow.TitleBar.ButtonInactiveForegroundColor = UI.Color("#64748b"); window.AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent; window.AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        var bar = new TitleBar { Title = title, IconSource = new FontIconSource { Glyph = UI.Glyph(kind ?? "document"), Foreground = UI.Brush(kind is null ? "#185abd" : UI.Accent(kind)) }, Content = content };
        window.SetTitleBar(bar); WindowIdentity.Apply(window, kind); return bar;
    }
}
