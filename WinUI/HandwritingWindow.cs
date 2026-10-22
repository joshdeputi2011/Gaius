using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using System.Text.Json.Nodes;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;

namespace Gaius.Office;

public sealed class HandwritingWindow : Window
{
    private readonly Canvas canvas = new() { Width = 960, Height = 540, Background = UI.Brush("#ffffff") };
    private readonly List<(Polyline Line, JsonObject Data)> strokes = [];
    private readonly Stack<(Polyline Line, JsonObject Data)> undone = [];
    private readonly Func<byte[], JsonArray, Task> insert;
    private Windows.UI.Color color = UI.Color("#185abd");
    private Polyline? current;
    private JsonArray? points;
    private bool erasing;
    private uint? activePointer;
    public HandwritingWindow(Func<byte[], JsonArray, Task> saved, string tool = "Pen")
    {
        insert = saved; erasing = tool == "Eraser"; Title = "Gaius — Handwriting"; AppWindow.Resize(new Windows.Graphics.SizeInt32(1100, 800));
        var root = new Grid { Background = UI.Brush("#edf0f5") }; root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(WindowChrome.Create(this, "Handwriting"));
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Padding = new Thickness(16) };
        var pen = new ComboBox { ItemsSource = new[] { "Pen", "Highlighter", "Eraser" }, SelectedItem = tool, Width = 115 }; pen.SelectionChanged += (_, _) => erasing = (string)pen.SelectedItem == "Eraser"; bar.Children.Add(pen);
        bar.Children.Add(UI.ColorButton("Ink color", value => color = value)); var width = new Slider { Minimum = 1, Maximum = 24, Value = tool == "Highlighter" ? 18 : 3, Width = 100 }; Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(width, "Pen width"); bar.Children.Add(width);
        bar.Children.Add(UI.CompactButton("Undo", () => { if (strokes.Count == 0) return; var last = strokes[^1]; strokes.RemoveAt(strokes.Count - 1); canvas.Children.Remove(last.Line); undone.Push(last); }, Symbol.Undo));
        bar.Children.Add(UI.CompactButton("Redo", () => { if (!undone.TryPop(out var last)) return; strokes.Add(last); canvas.Children.Add(last.Line); }, Symbol.Redo));
        bar.Children.Add(UI.CompactButton("Clear", () => { foreach (var stroke in strokes.AsEnumerable().Reverse()) undone.Push(stroke); strokes.Clear(); canvas.Children.Clear(); }, Symbol.Clear));
        var save = UI.Button("Insert drawing", async () => { try { var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(canvas, 960, 540); var bytes = await BitmapTools.Pixels((await bitmap.GetPixelsAsync()).ToArray(), (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, png: true); await insert(bytes, new JsonArray(strokes.Select(s => s.Data.DeepClone()).ToArray())); Close(); } catch (Exception error) { await UI.Error(this, error); } }, Symbol.Accept); save.HorizontalAlignment = HorizontalAlignment.Right; save.Margin = new Thickness(16); Grid.SetRow(save, 3); root.Children.Add(save);
        var barScroll = new ScrollViewer { Content = bar, HorizontalScrollMode = ScrollMode.Enabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollMode = ScrollMode.Disabled }; Grid.SetRow(barScroll, 1); root.Children.Add(barScroll);
        var view = new Viewbox { Child = canvas, Margin = new Thickness(24) }; Grid.SetRow(view, 2); root.Children.Add(view); Content = root;
        canvas.PointerPressed += (_, args) => {
            if (activePointer is not null) return; activePointer = args.Pointer.PointerId; var point = args.GetCurrentPoint(canvas);
            if (erasing || point.Properties.IsEraser) { Erase(point.Position); canvas.CapturePointer(args.Pointer); args.Handled = true; return; }
            bool highlight = (string)pen.SelectedItem == "Highlighter"; var ink = color; ink.A = highlight ? (byte)100 : (byte)255;
            current = new Polyline { Stroke = new SolidColorBrush(ink), StrokeThickness = highlight ? Math.Max(12, width.Value) : width.Value, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false };
            points = new JsonArray(); var data = new JsonObject { ["color"] = $"#{color.R:x2}{color.G:x2}{color.B:x2}", ["width"] = current.StrokeThickness, ["highlighter"] = highlight, ["points"] = points }; strokes.Add((current, data)); undone.Clear(); canvas.Children.Add(current); Add(point.Position, point.Properties.Pressure); canvas.CapturePointer(args.Pointer); args.Handled = true;
        };
        canvas.PointerMoved += (_, args) => { if (activePointer != args.Pointer.PointerId) return; var point = args.GetCurrentPoint(canvas); if (erasing || point.Properties.IsEraser) Erase(point.Position); else Add(point.Position, point.Properties.Pressure); args.Handled = true; };
        canvas.PointerReleased += (_, args) => { if (activePointer != args.Pointer.PointerId) return; current = null; points = null; activePointer = null; canvas.ReleasePointerCapture(args.Pointer); args.Handled = true; };
        canvas.PointerCaptureLost += (_, _) => { current = null; points = null; activePointer = null; };
    }
    private void Add(Point point, float pressure)
    {
        if (current is null || points is null || points.Count >= 15000) return; point.X = Math.Clamp(point.X, 0, 960); point.Y = Math.Clamp(point.Y, 0, 540); current.Points.Add(point); if (current.Points.Count == 1) current.Points.Add(new Point(point.X + .1, point.Y)); points.Add(new JsonArray(point.X, point.Y, pressure));
    }
    private void Erase(Point point)
    {
        foreach (var stroke in strokes.ToArray()) if (stroke.Line.Points.Any(p => Math.Abs(p.X - point.X) < 14 && Math.Abs(p.Y - point.Y) < 14)) { canvas.Children.Remove(stroke.Line); strokes.Remove(stroke); undone.Push(stroke); }
    }
}
