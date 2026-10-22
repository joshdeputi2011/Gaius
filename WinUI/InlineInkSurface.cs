using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System.Text.Json.Nodes;
using Windows.Foundation;

namespace Gaius.Office;

internal enum InlineInkTool { Select, Pen, Highlighter, Eraser }

/// <summary>A page-local layer: native pointer input and editable portable strokes share the page's scrolling and zoom.</summary>
internal sealed class InlineInkSurface
{
    private readonly Canvas canvas = new() { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), IsHitTestVisible = false, ManipulationMode = ManipulationModes.None };
    private readonly Stack<JsonArray> undo = [];
    private readonly Stack<JsonArray> redo = [];
    private JsonObject record;
    private Action changed;
    private JsonArray? before;
    private JsonObject? current;
    private Polyline? currentLine;
    private uint? pointer;
    private bool gestureChanged;
    private InlineInkTool tool;
    public string Color { get; set; } = "#185abd";
    public double Width { get; set; } = 3;
    public InlineInkTool Tool {
        get => tool;
        set { Finish(); tool = value; canvas.IsHitTestVisible = value != InlineInkTool.Select; }
    }

    private JsonArray Strokes => record["strokes"] as JsonArray ?? (JsonArray)(record["strokes"] = new JsonArray());
    private double Scale => Math.Max(.001, canvas.ActualWidth / Math.Max(1, InkData.Number(record["width"], canvas.ActualWidth)));

    public InlineInkSurface(Grid page, JsonObject data, Action onChanged)
    {
        record = data; changed = onChanged;
        page.Children.Add(canvas);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(canvas, "Page ink layer");
        canvas.SizeChanged += (_, _) => Render();
        canvas.PointerPressed += Pressed;
        canvas.PointerMoved += Moved;
        canvas.PointerReleased += Released;
        canvas.PointerCanceled += Released;
        canvas.PointerCaptureLost += (_, _) => Finish();
        Render();
    }

    public void SetRecord(JsonObject data, Action onChanged)
    {
        if (ReferenceEquals(record, data)) return;
        Finish(); record = data; changed = onChanged; undo.Clear(); redo.Clear(); Render();
    }

    private void Pressed(object sender, PointerRoutedEventArgs args)
    {
        if (tool == InlineInkTool.Select || pointer is not null) return;
        var point = args.GetCurrentPoint(canvas);
        if (args.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse && !point.Properties.IsLeftButtonPressed) return;
        pointer = args.Pointer.PointerId; before = (JsonArray)Strokes.DeepClone(); gestureChanged = false;
        if (tool == InlineInkTool.Eraser || point.Properties.IsEraser) Erase(point.Position);
        else {
            current = new JsonObject { ["color"] = Color, ["width"] = tool == InlineInkTool.Highlighter ? Math.Max(12, Width) : Width, ["highlighter"] = tool == InlineInkTool.Highlighter, ["points"] = new JsonArray() };
            Strokes.Add(current); currentLine = Line(current); canvas.Children.Add(currentLine);
            Add(point.Position, point.Properties.Pressure); gestureChanged = true;
        }
        canvas.CapturePointer(args.Pointer); args.Handled = true;
    }

    private void Moved(object sender, PointerRoutedEventArgs args)
    {
        if (pointer != args.Pointer.PointerId) return;
        // Intermediate points arrive newest first; reversing preserves pen trajectory.
        foreach (var point in args.GetIntermediatePoints(canvas).Reverse())
            if (tool == InlineInkTool.Eraser || point.Properties.IsEraser) Erase(point.Position); else Add(point.Position, point.Properties.Pressure);
        args.Handled = true;
    }

    private void Released(object sender, PointerRoutedEventArgs args)
    {
        if (pointer != args.Pointer.PointerId) return;
        var point = args.GetCurrentPoint(canvas);
        if (tool == InlineInkTool.Eraser || point.Properties.IsEraser) Erase(point.Position); else Add(point.Position, point.Properties.Pressure);
        Finish(); canvas.ReleasePointerCapture(args.Pointer); args.Handled = true;
    }

    private void Add(Point position, float pressure)
    {
        if (current?["points"] is not JsonArray points || currentLine is null || points.Count >= 15000) return;
        double x = Math.Clamp(position.X, 0, canvas.ActualWidth) / Scale, y = Math.Clamp(position.Y, 0, canvas.ActualHeight) / Scale;
        if (points.LastOrDefault() is JsonArray last && Math.Abs(InkData.Number(last[0]) - x) + Math.Abs(InkData.Number(last[1]) - y) < .2) return;
        points.Add(new JsonArray(x, y, pressure));
        currentLine.Points.Add(new Point(x * Scale, y * Scale));
        // A tap is a visible dot, not an invisible one-point polyline.
        if (currentLine.Points.Count == 1) currentLine.Points.Add(new Point(x * Scale + .1, y * Scale));
        record["height"] = Math.Max(InkData.Number(record["height"]), canvas.ActualHeight / Scale);
    }

    private void Erase(Point point)
    {
        bool erased = false;
        foreach (var stroke in Strokes.OfType<JsonObject>().Where(stroke => InkData.Hit(stroke, point.X / Scale, point.Y / Scale, Math.Max(10, Width) / Scale)).ToArray()) { Strokes.Remove(stroke); erased = true; }
        if (erased) { gestureChanged = true; Render(); }
    }

    private void Finish()
    {
        if (pointer is null) return;
        pointer = null; current = null; currentLine = null;
        if (gestureChanged && before is not null) { undo.Push(before); redo.Clear(); changed(); }
        before = null; gestureChanged = false;
    }

    public void Undo()
    {
        Finish(); if (!undo.TryPop(out var snapshot)) return;
        redo.Push((JsonArray)Strokes.DeepClone()); record["strokes"] = snapshot; Render(); changed();
    }

    public void Redo()
    {
        Finish(); if (!redo.TryPop(out var snapshot)) return;
        undo.Push((JsonArray)Strokes.DeepClone()); record["strokes"] = snapshot; Render(); changed();
    }

    public void Clear()
    {
        Finish(); if (Strokes.Count == 0) return;
        undo.Push((JsonArray)Strokes.DeepClone()); redo.Clear(); Strokes.Clear(); Render(); changed();
    }

    private Polyline Line(JsonObject stroke)
    {
        Windows.UI.Color color;
        try { color = UI.Color(stroke["color"]?.ToString() ?? "#185abd"); } catch { color = UI.Color("#185abd"); }
        color.A = stroke["highlighter"]?.ToString() == "true" ? (byte)90 : (byte)255;
        return new Polyline { Stroke = new SolidColorBrush(color), StrokeThickness = Math.Max(.1, InkData.Number(stroke["width"], 3) * Scale), StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false };
    }

    private void Render()
    {
        canvas.Clip = new RectangleGeometry { Rect = new Rect(0, 0, canvas.ActualWidth, canvas.ActualHeight) };
        canvas.Children.Clear();
        foreach (var stroke in Strokes.OfType<JsonObject>()) {
            var line = Line(stroke);
            if (stroke["points"] is JsonArray points)
                foreach (var point in points.OfType<JsonArray>().Where(point => point.Count >= 2)) line.Points.Add(new Point(InkData.Number(point[0]) * Scale, InkData.Number(point[1]) * Scale));
            if (line.Points.Count == 1) line.Points.Add(new Point(line.Points[0].X + .1, line.Points[0].Y));
            canvas.Children.Add(line);
            if (ReferenceEquals(stroke, current)) currentLine = line;
        }
    }
}
