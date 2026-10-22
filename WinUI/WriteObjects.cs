using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Text.Json.Nodes;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Gaius.Office;

public sealed partial class EditorWindow
{
    private Func<string, string?, Task> insertWriteObject = (_, _) => Task.CompletedTask;
    private string writeShapeColor = "#185abd";
    private Action snapshotWriteWrapping = () => { };
    private async Task<string?> ClipboardPicture()
    {
        var data = Clipboard.GetContent(); if (!data.Contains(StandardDataFormats.Bitmap)) return null;
        using var source = await (await data.GetBitmapAsync()).OpenReadAsync();
        var decoder = await BitmapDecoder.CreateAsync(source);
        if ((long)decoder.PixelWidth * decoder.PixelHeight > 40000000) throw new IOException("Image limit: 40 megapixels.");
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        using var output = new InMemoryRandomAccessStream(); var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output); encoder.SetSoftwareBitmap(bitmap); await encoder.FlushAsync(); output.Seek(0);
        var bytes = new byte[checked((int)output.Size)]; await output.AsStreamForRead().ReadExactlyAsync(bytes);
        return "data:image/png;base64," + Convert.ToBase64String(bytes);
    }
    private void ConfigureWriteObjects(RichEditBox editor, Border paper)
    {
        file.Content["objects"] ??= new JsonArray(); var objects = (JsonArray)file.Content["objects"]!;
        var layout = new Grid(); paper.Child = layout; layout.Children.Add(editor);
        var layer = new Canvas { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        layout.Children.Add(layer);
        JsonObject? selected = null;
        var wrapping = new List<(JsonObject Object, ITextRange Range, float First, float Left, float Right, float Before)>();
        snapshotWriteWrapping = () => {
            foreach (var obj in objects.OfType<JsonObject>()) obj.Remove("wrapBase");
            foreach (var saved in wrapping) { saved.Object["wrapBase"] ??= new JsonArray(); ((JsonArray)saved.Object["wrapBase"]!).Add(new JsonObject { ["start"] = saved.Range.StartPosition, ["end"] = saved.Range.EndPosition, ["first"] = saved.First, ["left"] = saved.Left, ["right"] = saved.Right, ["before"] = saved.Before }); }
        };
        void Wrap()
        {
            // RichEdit exposes paragraph indents; apply exclusion to paragraphs intersecting the object.
            if (!editor.IsLoaded) return;
            applyingViewFormat = true;
            try {
            foreach (var saved in wrapping) { saved.Range.ParagraphFormat.SetIndents(saved.First, saved.Left, saved.Right); saved.Range.ParagraphFormat.SpaceBefore = saved.Before; }
            wrapping.Clear();
            foreach (var node in objects.OfType<JsonObject>()) {
                string wrap = node["wrap"]?.ToString() ?? "front"; if (wrap is not ("square-left" or "square-right" or "top-bottom")) continue;
                double y = SlideRenderer.Number(node["y"]) / 100 * Math.Max(editor.MinHeight, layout.ActualHeight), h = SlideRenderer.Number(node["height"], 20) / 100 * Math.Max(editor.MinHeight, layout.ActualHeight);
                var first = editor.Document.GetRangeFromPoint(new Point(editor.Padding.Left + 2, y), PointOptions.ClientCoordinates);
                var last = editor.Document.GetRangeFromPoint(new Point(editor.Padding.Left + 2, y + h + 12), PointOptions.ClientCoordinates);
                int position = Math.Min(first.StartPosition, last.StartPosition), end = Math.Max(first.EndPosition, last.EndPosition);
                double inset = SlideRenderer.Number(node["width"], 35) / 100 * paper.Width + 12;
                do { var range = editor.Document.GetRange(position, position); range.Expand(TextRangeUnit.Paragraph); var format = range.ParagraphFormat;
                    wrapping.Add((node, range.GetClone(), format.FirstLineIndent, format.LeftIndent, format.RightIndent, format.SpaceBefore));
                    format.SetIndents(format.FirstLineIndent, format.LeftIndent + (wrap == "square-left" ? (float)(inset * .75) : 0), format.RightIndent + (wrap == "square-right" ? (float)(inset * .75) : 0));
                    if (wrap == "top-bottom") format.SpaceBefore += (float)(h * .75);
                    if (range.EndPosition <= position) break; position = range.EndPosition;
                } while (position < end);
            }
            snapshotWriteWrapping();
            } finally { applyingViewFormat = false; }
        }
        async Task Cache(JsonObject obj)
        {
            string kind = obj["kind"]!.ToString(); if (kind == "image") return;
            var bytes = await NativeDrawing.ShapePng(kind, UI.Color(obj["color"]!.ToString()), outline: UI.Color(obj["stroke"]?.ToString() ?? "#185abd"), outlineWidth: SlideRenderer.Number(obj["strokeWidth"]));
            obj["dataUrl"] = "data:image/png;base64," + Convert.ToBase64String(bytes);
        }
        void Render()
        {
            layer.Children.Clear(); double width = paper.Width, height = Math.Max(editor.MinHeight, layout.ActualHeight);
            foreach (var obj in objects.OfType<JsonObject>()) {
                var border = SlideRenderer.Object(obj); border.Width = SlideRenderer.Number(obj["width"], 35) * width / 100; border.Height = SlideRenderer.Number(obj["height"], 20) * height / 100;
                border.RenderTransform = new RotateTransform { Angle = SlideRenderer.Number(obj["rotation"]), CenterX = border.Width / 2, CenterY = border.Height / 2 };
                Canvas.SetLeft(border, SlideRenderer.Number(obj["x"]) * width / 100); Canvas.SetTop(border, SlideRenderer.Number(obj["y"]) * height / 100);
                if (obj == selected) { border.BorderBrush = UI.DocumentBrush("#185abd"); border.BorderThickness = new Thickness(2); }
                Point? start = null; double x = 0, y = 0, w = 0, h = 0; bool resizing = false;
                border.PointerPressed += (_, e) => { selected = obj; start = e.GetCurrentPoint(layer).Position; var local = e.GetCurrentPoint(border).Position; resizing = local.X >= border.Width - 18 && local.Y >= border.Height - 18; x = SlideRenderer.Number(obj["x"]); y = SlideRenderer.Number(obj["y"]); w = SlideRenderer.Number(obj["width"]); h = SlideRenderer.Number(obj["height"]); border.CapturePointer(e.Pointer); e.Handled = true; };
                border.PointerMoved += (_, e) => { if (start is not Point origin) return; var point = e.GetCurrentPoint(layer).Position;
                    if (resizing) { obj["width"] = Math.Clamp(w + (point.X - origin.X) * 100 / width, 2, 100 - x); obj["height"] = Math.Clamp(obj["lockAspect"]?.GetValue<bool>() == true ? SlideRenderer.Number(obj["width"]) * h / Math.Max(1, w) : h + (point.Y - origin.Y) * 100 / height, 2, 100 - y); border.Width = SlideRenderer.Number(obj["width"]) * width / 100; border.Height = SlideRenderer.Number(obj["height"]) * height / 100; }
                    else { obj["x"] = Math.Clamp(x + (point.X - origin.X) * 100 / width, 0, Math.Max(0, 100 - w)); obj["y"] = Math.Clamp(y + (point.Y - origin.Y) * 100 / height, 0, Math.Max(0, 100 - h)); Canvas.SetLeft(border, SlideRenderer.Number(obj["x"]) * width / 100); Canvas.SetTop(border, SlideRenderer.Number(obj["y"]) * height / 100); }
                };
                border.PointerReleased += (_, e) => { if (start is null) return; start = null; border.ReleasePointerCapture(e.Pointer); Changed(); Wrap(); Render(); };
                border.PointerCaptureLost += (_, _) => start = null;
                border.DoubleTapped += async (_, e) => { e.Handled = true; selected = obj; await Format(); };
                var menu = new MenuFlyout(); var format = new MenuFlyoutItem { Text = "Format picture or shape" }; format.Click += async (_, _) => { selected = obj; await Format(); }; menu.Items.Add(format);
                var delete = new MenuFlyoutItem { Text = "Delete" }; delete.Click += (_, _) => { objects.Remove(obj); Wrap(); Changed(); Render(); }; menu.Items.Add(delete); border.ContextFlyout = menu;
                layer.Children.Add(border);
            }
        }
        async Task Format()
        {
            if (selected is not JsonObject obj) { await Message("Format object", "Select a picture or shape first. Double-click it to open its formatting controls. Drag to move; drag the lower-right corner to resize."); return; }
            var panel = new StackPanel { Spacing = 12, Width = 330 };
            foreach (var key in new[] { "x", "y", "width", "height", "rotation", "strokeWidth" }) {
                string field = key; var number = new NumberBox { Header = key == "strokeWidth" ? "Outline width" : key == "rotation" ? "Rotation (degrees)" : OfficeShapes.Label(key) + " (%)", Value = SlideRenderer.Number(obj[key], key is "width" or "height" ? 20 : 0), Minimum = key == "rotation" ? -360 : key is "width" or "height" ? 2 : 0, Maximum = key == "rotation" ? 360 : key == "strokeWidth" ? 20 : 100, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
                number.ValueChanged += async (_, e) => { if (!double.IsFinite(e.NewValue)) return; obj[field] = e.NewValue; if (field == "strokeWidth") await Cache(obj); Changed(); Render(); }; panel.Children.Add(number);
            }
            var wrap = new ComboBox { Header = "Wrap text", ItemsSource = new[] { "front", "square-left", "square-right", "top-bottom" }, SelectedItem = obj["wrap"]?.ToString() ?? "front", HorizontalAlignment = HorizontalAlignment.Stretch };
            wrap.SelectionChanged += (_, _) => { obj["wrap"] = wrap.SelectedItem?.ToString(); if (wrap.SelectedItem?.ToString() == "square-left") obj["x"] = 0; if (wrap.SelectedItem?.ToString() == "square-right") obj["x"] = 100 - SlideRenderer.Number(obj["width"]); Changed(); Wrap(); Render(); }; panel.Children.Add(wrap);
            if (obj["kind"]?.ToString() != "image") panel.Children.Add(UI.ColorButton("Shape fill", async color => { obj["color"] = $"#{color.R:x2}{color.G:x2}{color.B:x2}"; await Cache(obj); Changed(); Render(); }));
            panel.Children.Add(UI.ColorButton("Outline color", async color => { obj["stroke"] = $"#{color.R:x2}{color.G:x2}{color.B:x2}"; await Cache(obj); Changed(); Render(); }));
            var aspect = new CheckBox { Content = "Lock aspect ratio", IsChecked = obj["lockAspect"]?.GetValue<bool>() ?? true }; aspect.Click += (_, _) => { obj["lockAspect"] = aspect.IsChecked == true; Changed(); }; panel.Children.Add(aspect);
            var alt = new TextBox { Header = "Alternative text", Text = obj["alt"]?.ToString() ?? "" }; alt.TextChanged += (_, _) => { obj["alt"] = alt.Text; Changed(); }; panel.Children.Add(alt);
            panel.Children.Add(UI.Button("Delete object", () => { objects.Remove(obj); Wrap(); selected = null; Changed(); Render(); }));
            await WindowLifetime.ShowDialog(this, new ContentDialog { Title = "Format object", Content = new ScrollViewer { Content = panel, MaxHeight = 450 }, CloseButtonText = "Done" });
        }
        insertWriteObject = async (kind, data) => {
            double width = 35, height = 18;
            if (kind == "image" && data is not null) { var imageBytes = Convert.FromBase64String(data[(data.IndexOf(',') + 1)..]); using var input = new MemoryStream(imageBytes); var decoder = await BitmapDecoder.CreateAsync(input.AsRandomAccessStream()); var normalized = await BitmapTools.Encode(imageBytes, decoder.PixelWidth, decoder.PixelHeight, png: true); data = "data:image/png;base64," + Convert.ToBase64String(normalized); var dimensions = (Width: (int)decoder.PixelWidth, Height: (int)decoder.PixelHeight); double pageHeight = Math.Max(editor.MinHeight, layout.ActualHeight); height = Math.Min(70, width * paper.Width / pageHeight * dimensions.Height / Math.Max(1, dimensions.Width)); width = Math.Min(width, height * pageHeight / paper.Width * dimensions.Width / Math.Max(1, dimensions.Height)); }
            var obj = new JsonObject { ["id"] = Guid.NewGuid().ToString(), ["kind"] = kind, ["x"] = 10, ["y"] = 10, ["width"] = width, ["height"] = height, ["color"] = writeShapeColor, ["stroke"] = "#185abd", ["strokeWidth"] = 0, ["rotation"] = 0, ["wrap"] = "front", ["lockAspect"] = kind == "image", ["dataUrl"] = data };
            await Cache(obj); objects.Add(obj); selected = obj; Changed(); Render();
        };
        editor.Paste += async (_, e) => { if (!Clipboard.GetContent().Contains(StandardDataFormats.Bitmap)) return; e.Handled = true; try { var image = await ClipboardPicture(); if (image is not null) await insertWriteObject("image", image); } catch (Exception error) { await UI.Error(this, error); } };
        Command("Insert", "Format object", async () => await Format());
        insertDrawing = bytes => insertWriteObject("image", "data:image/png;base64," + Convert.ToBase64String(bytes));
        layout.SizeChanged += (_, _) => Render(); editor.Loaded += (_, _) => {
            foreach (var obj in objects.OfType<JsonObject>()) foreach (var original in obj["wrapBase"] as JsonArray ?? []) { if (original is null) continue; wrapping.Add((obj, editor.Document.GetRange((int)SlideRenderer.Number(original["start"]), (int)SlideRenderer.Number(original["end"])), (float)SlideRenderer.Number(original["first"]), (float)SlideRenderer.Number(original["left"]), (float)SlideRenderer.Number(original["right"]), (float)SlideRenderer.Number(original["before"]))); }
            Wrap(); Render();
        };
    }
}
