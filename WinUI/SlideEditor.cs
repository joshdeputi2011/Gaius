using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Text.Json.Nodes;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Storage;
using Windows.Foundation;
using Windows.Graphics.Imaging;

namespace Gaius.Office;

public sealed partial class EditorWindow
{
    private void BuildNativeSlides()
    {
        foreach (var tab in new[] { "Home", "Insert", "Design", "Transitions", "Animations", "Slide Show", "Review", "View" }) ribbon.Panel(tab);
        int index = 0; string? selected = null; bool rendering = false;
        var root = new Grid { Padding = new Thickness(16), ColumnSpacing = 16 }; root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) }); root.ColumnDefinitions.Add(new ColumnDefinition()); root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        var list = new ListView { SelectionMode = ListViewSelectionMode.Single }; root.Children.Add(list);
        var stage = new Viewbox { Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(stage, 1); root.Children.Add(stage);
        var properties = new StackPanel { Spacing = 12 }; var propertyScroll = new ScrollViewer { Content = properties }; Grid.SetColumn(propertyScroll, 2); root.Children.Add(propertyScroll); body.Children.Add(root);
        JsonObject Slide() => (JsonObject)file.Slides[index]!;
        JsonArray Objects() { Slide()["objects"] ??= new JsonArray(); return (JsonArray)Slide()["objects"]!; }
        JsonObject? Active() => Objects().OfType<JsonObject>().FirstOrDefault(o => o["id"]?.ToString() == selected);
        void Update() { Changed(); RenderStage(); }
        void RenderStage()
        {
            var canvas = SlideRenderer.Create(Slide()); stage.Child = canvas;
            foreach (var border in canvas.Children.OfType<Border>()) {
                if (border.Tag is not JsonObject obj) continue;
                if (obj["id"]?.ToString() == selected) { border.BorderBrush = UI.DocumentBrush("#185abd"); border.BorderThickness = new Thickness(2); }
                Point? pointer = null; double originX = 0, originY = 0, originW = 0, originH = 0; bool resize = false;
                border.PointerPressed += (_, e) => { selected = obj["id"]!.ToString(); pointer = e.GetCurrentPoint(canvas).Position; var local = e.GetCurrentPoint(border).Position; resize = local.X > border.Width - 18 && local.Y > border.Height - 18; originX = SlideRenderer.Number(obj["x"]); originY = SlideRenderer.Number(obj["y"]); originW = SlideRenderer.Number(obj["width"]); originH = SlideRenderer.Number(obj["height"]); border.CapturePointer(e.Pointer); RenderProperties(); e.Handled = true; };
                border.PointerMoved += (_, e) => { if (pointer is not Point start) return; var current = e.GetCurrentPoint(canvas).Position;
                    if (resize) { obj["width"] = Math.Clamp(originW + (current.X - start.X) * 100 / SlideRenderer.Width, 2, Math.Max(2, 100 - originX)); obj["height"] = Math.Clamp(obj["lockAspect"]?.GetValue<bool>() == true ? SlideRenderer.Number(obj["width"]) * originH / Math.Max(1, originW) : originH + (current.Y - start.Y) * 100 / SlideRenderer.Height, 2, Math.Max(2, 100 - originY)); border.Width = SlideRenderer.Number(obj["width"]) * SlideRenderer.Width / 100; border.Height = SlideRenderer.Number(obj["height"]) * SlideRenderer.Height / 100; }
                    else { obj["x"] = Math.Clamp(originX + (current.X - start.X) * 100 / SlideRenderer.Width, 0, Math.Max(0, 100 - SlideRenderer.Number(obj["width"], 30))); obj["y"] = Math.Clamp(originY + (current.Y - start.Y) * 100 / SlideRenderer.Height, 0, Math.Max(0, 100 - SlideRenderer.Number(obj["height"], 18))); Canvas.SetLeft(border, SlideRenderer.Number(obj["x"]) * SlideRenderer.Width / 100); Canvas.SetTop(border, SlideRenderer.Number(obj["y"]) * SlideRenderer.Height / 100); } Changed(); };
                border.PointerReleased += (_, e) => { pointer = null; border.ReleasePointerCapture(e.Pointer); RenderProperties(); };
                border.PointerCaptureLost += (_, _) => pointer = null;
            }
        }
        void TextField(string label, string key, JsonObject target, bool multiline = false)
        {
            var input = new TextBox { Header = label, Text = target[key]?.ToString() ?? "", AcceptsReturn = multiline, TextWrapping = TextWrapping.Wrap, MinHeight = multiline ? 90 : 0 };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(input, label);
            input.TextChanged += (_, _) => { target[key] = input.Text; Update(); if (key == "title" && list.Items[index] is ListViewItem item && item.Content is TextBlock text) text.Text = $"{index + 1}. {input.Text}"; }; properties.Children.Add(input);
        }
        void RenderProperties()
        {
            properties.Children.Clear(); TextField("Slide title", "title", Slide()); TextField("Slide body", "body", Slide(), true);
            if (Active() is JsonObject obj) {
                properties.Children.Add(UI.Text("Object", 14));
                if (obj["kind"]?.ToString() is "text" or "wordart") TextField("Object text", "text", obj, true);
                properties.Children.Add(UI.ColorButton("Object color", color => { obj["color"] = $"#{color.R:x2}{color.G:x2}{color.B:x2}"; Update(); }));
                properties.Children.Add(UI.ColorButton("Outline color", color => { obj["stroke"] = $"#{color.R:x2}{color.G:x2}{color.B:x2}"; Update(); }));
                foreach (var key in new[] { "x", "y", "width", "height", "fontSize", "strokeWidth", "rotation" }) {
                    if (key == "fontSize" && obj["kind"]?.ToString() is not ("text" or "wordart")) continue;
                    var field = key; var number = new NumberBox { Header = key == "fontSize" ? "Font size" : key == "strokeWidth" ? "Outline width" : key == "rotation" ? "Rotation (degrees)" : char.ToUpper(key[0]) + key[1..] + " (%)", Value = SlideRenderer.Number(obj[key], key is "rotation" or "strokeWidth" ? 0 : 28), Minimum = key == "rotation" ? -360 : key is "x" or "y" or "strokeWidth" ? 0 : 1, Maximum = key == "rotation" ? 360 : key == "strokeWidth" ? 20 : key == "fontSize" ? 160 : 100, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
                    number.ValueChanged += (_, args) => { if (double.IsFinite(args.NewValue)) { obj[field] = args.NewValue; Update(); } }; properties.Children.Add(number);
                }
                var aspect = new CheckBox { Content = "Lock aspect ratio", IsChecked = obj["lockAspect"]?.GetValue<bool>() ?? obj["kind"]?.ToString() == "image" }; aspect.Click += (_, _) => { obj["lockAspect"] = aspect.IsChecked == true; Changed(); }; properties.Children.Add(aspect);
                properties.Children.Add(UI.Text("Drag the lower-right corner to resize.", 11, "#64748b"));
                properties.Children.Add(UI.Button("Bring to front", () => { Objects().Remove(obj); Objects().Add(obj); Update(); }));
                properties.Children.Add(UI.Button("Send to back", () => { Objects().Remove(obj); Objects().Insert(0, obj); Update(); }));
                properties.Children.Add(UI.Button("Delete object", () => { Objects().Remove(obj); selected = null; Changed(); RenderStage(); RenderProperties(); }, Symbol.Delete));
            }
        }
        void RenderList()
        {
            rendering = true; list.Items.Clear();
            for (int i = 0; i < file.Slides.Count; i++) list.Items.Add(new ListViewItem { Content = UI.Text($"{i + 1}. {file.Slides[i]!["title"]}", 12), Padding = new Thickness(10, 18, 10, 18), HorizontalContentAlignment = HorizontalAlignment.Stretch });
            list.SelectedIndex = index; rendering = false;
        }
        void Render() { RenderList(); RenderStage(); RenderProperties(); status.Text = $"Slide {index + 1} of {file.Slides.Count}"; }
        void AddSlide() { file.Slides.Insert(index + 1, new JsonObject { ["id"] = Guid.NewGuid().ToString(), ["title"] = "New slide", ["body"] = "", ["background"] = "#ffffff", ["objects"] = new JsonArray() }); index++; selected = null; Changed(); Render(); }
        void Duplicate() { var copy = Slide().DeepClone(); copy["id"] = Guid.NewGuid().ToString(); file.Slides.Insert(index + 1, copy); index++; selected = null; Changed(); Render(); }
        void AddObject(string kind, string? data = null)
        {
            var obj = new JsonObject { ["id"] = Guid.NewGuid().ToString(), ["kind"] = kind, ["x"] = 15, ["y"] = 42, ["width"] = 35, ["height"] = kind is "text" or "wordart" ? 18 : 30, ["color"] = "#185abd", ["fontSize"] = kind == "wordart" ? 40 : 28, ["animation"] = "none" };
            if (kind == "image") obj["dataUrl"] = data; else if (kind is "text" or "wordart") obj["text"] = kind == "wordart" ? "WordArt" : "Text";
            if (kind == "image" && data is not null) { var dimensions = RichTextInterop.ImageDimensions(Convert.FromBase64String(data[(data.IndexOf(',') + 1)..])); double height = Math.Min(55, 35 * SlideRenderer.Width / SlideRenderer.Height * dimensions.Height / Math.Max(1, dimensions.Width)); obj["height"] = height; obj["width"] = Math.Min(35, height * SlideRenderer.Height / SlideRenderer.Width * dimensions.Width / Math.Max(1, dimensions.Height)); obj["lockAspect"] = true; }
            obj["stroke"] = "#185abd"; obj["strokeWidth"] = 0; obj["rotation"] = 0;
            Objects().Add(obj); selected = obj["id"]!.ToString(); Changed(); RenderStage(); RenderProperties();
        }
        insertDrawing = bytes => { AddObject("image", "data:image/png;base64," + Convert.ToBase64String(bytes)); return Task.CompletedTask; };
        list.SelectionChanged += (_, _) => { if (rendering || list.SelectedIndex < 0) return; index = list.SelectedIndex; selected = null; RenderStage(); RenderProperties(); };
        Command("Home", "New slide", AddSlide, Symbol.Add); Command("Home", "Duplicate", Duplicate, Symbol.Copy);
        Command("Home", "Delete slide", () => { if (file.Slides.Count < 2) return; file.Slides.RemoveAt(index); index = Math.Clamp(index, 0, file.Slides.Count - 1); selected = null; Changed(); Render(); }, Symbol.Delete);
        foreach (var kind in new[] { "text", "wordart" }) { var value = kind; Command("Insert", kind == "wordart" ? "WordArt" : char.ToUpper(kind[0]) + kind[1..], () => AddObject(value)); }
        var gallery = new DropDownButton { Content = "Shape gallery" }; var shapeMenu = new MenuFlyout(); foreach (var kind in OfficeShapes.Paths.Keys) { string value = kind; var item = new MenuFlyoutItem { Text = OfficeShapes.Label(kind) }; item.Click += (_, _) => AddObject(value); shapeMenu.Items.Add(item); } gallery.Flyout = shapeMenu; ribbon.Group("Insert", "Shapes").Children.Add(gallery);
        Command("Home", "Paste picture", async () => { try { var image = await ClipboardPicture(); if (image is not null) AddObject("image", image); } catch (Exception error) { await UI.Error(this, error); } }, Symbol.Paste);
        Command("Insert", "Picture", async () => {
            try {
                var picked = await UI.PickOpen(this, ".png", ".jpg", ".jpeg", ".bmp", ".img"); if (picked is null || WindowLifetime.IsClosed(this)) return;
                if ((await picked.GetBasicPropertiesAsync()).Size > 40 * 1024 * 1024) throw new IOException("Choose an image smaller than 40 MB.");
                var bytes = (await FileIO.ReadBufferAsync(picked)).ToArray(); await OfficeQuickTools.ImageMime(bytes);
                using var input = new MemoryStream(bytes); var decoder = await BitmapDecoder.CreateAsync(input.AsRandomAccessStream());
                var png = await BitmapTools.Encode(bytes, decoder.PixelWidth, decoder.PixelHeight, png: true); if (WindowLifetime.IsClosed(this)) return;
                AddObject("image", "data:image/png;base64," + Convert.ToBase64String(png));
            } catch (Exception error) { await UI.Error(this, error); }
        }, Symbol.Pictures);
        Command("Insert", "Table", () => { for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++) { double x = 15 + c * 22, y = 40 + r * 12; Objects().Add(new JsonObject { ["id"] = Guid.NewGuid().ToString(), ["kind"] = "rectangle", ["x"] = x, ["y"] = y, ["width"] = 21.7, ["height"] = 11.7, ["color"] = r == 0 ? "#dce8fa" : "#edf2f8" }); Objects().Add(new JsonObject { ["id"] = Guid.NewGuid().ToString(), ["kind"] = "text", ["x"] = x + 1, ["y"] = y + 2, ["width"] = 19, ["height"] = 8, ["text"] = "Cell", ["fontSize"] = 20, ["color"] = "#24334a" }); } Changed(); RenderStage(); });
        ribbon.Group("Design", "Colors").Children.Add(UI.ColorButton("Background", color => { Slide()["background"] = $"#{color.R:x2}{color.G:x2}{color.B:x2}"; Update(); }));
        ribbon.Group("Design", "Colors").Children.Add(UI.ColorButton("Object color", color => { if (Active() is JsonObject obj) { obj["color"] = $"#{color.R:x2}{color.G:x2}{color.B:x2}"; Update(); } }));
        foreach (var type in new[] { "none", "fade", "push", "zoom", "morph" }) { var value = type; Command("Transitions", char.ToUpper(type[0]) + type[1..], () => { Slide()["transition"] = value; Changed(); SlideRenderer.Animate((Canvas)stage.Child, Slide(), index > 0 ? file.Slides[index - 1] : null); }); }
        foreach (var type in new[] { "none", "fade", "fly", "zoom" }) { var value = type; Command("Animations", char.ToUpper(type[0]) + type[1..], () => { if (Active() is JsonObject obj) { obj["animation"] = value; Changed(); RenderStage(); SlideRenderer.Animate((Canvas)stage.Child, Slide(), null); } }); }
        Command("Slide Show", "From beginning", () => App.Show(new PresentationWindow(file.Copy(), 0)), Symbol.Play); Command("Slide Show", "Current slide", () => App.Show(new PresentationWindow(file.Copy(), index)), Symbol.Play);
        Command("Slide Show", "Timing", async () => { var value = await Prompt("Seconds per slide", Slide()["duration"]?.ToString() ?? "5"); if (double.TryParse(value, out var seconds)) { Slide()["duration"] = Math.Clamp(seconds, 1, 120); Changed(); } });
        Command("Review", "Speaker notes", async () => { var text = await Prompt("Speaker notes", Slide()["notes"]?.ToString() ?? ""); if (text is not null) { Slide()["notes"] = text; Changed(); } });
        Command("View", "Move slide left", () => { if (index == 0) return; var slide = file.Slides[index]; file.Slides.RemoveAt(index); file.Slides.Insert(--index, slide); Changed(); Render(); });
        Command("View", "Move slide right", () => { if (index >= file.Slides.Count - 1) return; var slide = file.Slides[index]; file.Slides.RemoveAt(index); file.Slides.Insert(++index, slide); Changed(); Render(); });
        ViewCommands(); Render();
        var paste = new KeyboardAccelerator { Key = Windows.System.VirtualKey.V, Modifiers = Windows.System.VirtualKeyModifiers.Control }; paste.Invoked += async (_, args) => { if (!Windows.ApplicationModel.DataTransfer.Clipboard.GetContent().Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Bitmap)) return; args.Handled = true; try { var image = await ClipboardPicture(); if (image is not null) AddObject("image", image); } catch (Exception error) { await UI.Error(this, error); } }; root.KeyboardAccelerators.Add(paste);
        var startShow = new KeyboardAccelerator { Key = Windows.System.VirtualKey.F5 }; startShow.Invoked += (_, args) => { args.Handled = true; App.Show(new PresentationWindow(file.Copy(), 0)); }; root.KeyboardAccelerators.Add(startShow);
    }
}
