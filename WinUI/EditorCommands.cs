using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Text;
using System.Text.Json.Nodes;
using Windows.Storage;
using Windows.Graphics.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;

namespace Gaius.Office;

public sealed partial class EditorWindow
{
    private OfficeFile PrintSnapshot()
    {
        flushEditor();
        var snapshot = file.Copy();
        if (file.Kind == "image" && renderedImage is not null) snapshot.Image["dataUrl"] = "data:image/png;base64," + Convert.ToBase64String(renderedImage);
        return snapshot;
    }
    private void Command(string tab, string label, Action action, Symbol? icon = null) => ribbon.Group(tab, tab == "Insert" ? "Insert" : tab == "Review" ? "Proofing" : tab).Children.Add(UI.RibbonButton(label, action, icon));
    private async Task<string?> Prompt(string title, string value = "")
    {
        var input = new TextBox { Text = value, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinWidth = 300 };
        var dialog = new ContentDialog { Title = title, Content = input, PrimaryButtonText = "Apply", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        return await WindowLifetime.ShowDialog(this, dialog) == ContentDialogResult.Primary ? input.Text : null;
    }
    private async Task Message(string title, string text) => await WindowLifetime.ShowDialog(this, new ContentDialog { Title = title, Content = text, CloseButtonText = "Close" });
    private void ViewCommands(ScrollViewer? scroll = null)
    {
        Command("View", "Print preview", () => App.Show(new PrintPreviewWindow(PrintSnapshot())), Symbol.Print);
        if (scroll is not null) {
            BindZoom(scroll);
        }
    }
    private void WriteCommands(RichEditBox editor, Border paper, ScrollViewer scroll)
    {
        ConfigureWriteObjects(editor, paper); ConfigureWriteLayout(editor, paper, scroll);
        foreach (var tab in new[] { "Insert", "Design", "Layout", "References", "Review", "View" }) ribbon.Panel(tab);
        Command("Insert", "Picture", async () => {
            try {
                var image = await UI.PickOpen(this, ".png", ".jpg", ".jpeg", ".bmp", ".img"); if (image is null) return;
                var bytes = (await FileIO.ReadBufferAsync(image)).ToArray(); if (bytes.Length > 40 * 1024 * 1024) throw new IOException("Choose an image smaller than 40 MB.");
                await insertWriteObject("image", "data:image/" + (image.FileType == ".png" ? "png" : image.FileType == ".bmp" ? "bmp" : "jpeg") + ";base64," + Convert.ToBase64String(bytes));
            } catch (Exception e) { await UI.Error(this, e); }
        }, Symbol.Pictures);
        Command("Insert", "Date", () => { editor.Document.Selection.Text = DateTime.Today.ToString("d MMMM yyyy"); editor.Focus(FocusState.Programmatic); Changed(); });
        Command("Insert", "Table", async () => {
            var rows = new NumberBox { Header = "Rows", Value = 3, Minimum = 1, Maximum = 30, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
            var columns = new NumberBox { Header = "Columns", Value = 3, Minimum = 1, Maximum = 12, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
            var panel = new StackPanel { Spacing = 12 }; panel.Children.Add(rows); panel.Children.Add(columns);
            var validation = UI.Text("Enter a number for both rows and columns.", 12, "#c42b32"); validation.Visibility = Visibility.Collapsed; panel.Children.Add(validation);
            var dialog = new ContentDialog { Title = "Insert table", Content = panel, PrimaryButtonText = "Insert", CloseButtonText = "Cancel" };
            dialog.PrimaryButtonClick += (_, args) => { if (!double.IsFinite(rows.Value) || !double.IsFinite(columns.Value)) { args.Cancel = true; validation.Visibility = Visibility.Visible; } };
            if (await WindowLifetime.ShowDialog(this, dialog) != ContentDialogResult.Primary || WindowLifetime.IsClosed(this)) return;
            if (!double.IsFinite(rows.Value) || !double.IsFinite(columns.Value)) return;
            int rowCount = (int)Math.Clamp(Math.Round(rows.Value), 1, 30), columnCount = (int)Math.Clamp(Math.Round(columns.Value), 1, 12);
            try { var data = Enumerable.Range(0, rowCount).Select(_ => (IReadOnlyList<string>)Enumerable.Repeat("", columnCount).ToArray()).ToArray(); editor.Document.Selection.SetText(TextSetOptions.FormatRtf, RichTextInterop.TableRtf(data)); editor.Focus(FocusState.Programmatic); Changed(); }
            catch (Exception error) { await UI.Error(this, error); }
        });
        Command("Insert", "WordArt", async () => { var text = await Prompt("WordArt", "Your text"); if (text is null) return; var selection = editor.Document.Selection; int start = Math.Min(selection.StartPosition, selection.EndPosition); selection.Text = text; var range = editor.Document.GetRange(start, start + text.Length); range.CharacterFormat.Size = 36; range.CharacterFormat.Bold = FormatEffect.On; range.CharacterFormat.ForegroundColor = UI.Color("#185abd"); editor.Focus(FocusState.Programmatic); Changed(); });
        ribbon.Group("Insert", "Shapes").Children.Add(UI.ColorButton("Shape color", color => writeShapeColor = $"#{color.R:x2}{color.G:x2}{color.B:x2}"));
        var shapes = new DropDownButton { Content = "Shapes" }; var shapeMenu = new MenuFlyout(); shapes.Flyout = shapeMenu;
        foreach (var kind in OfficeShapes.Paths.Keys) { string value = kind; var item = new MenuFlyoutItem { Text = OfficeShapes.Label(kind) }; item.Click += async (_, _) => await insertWriteObject(value, null); shapeMenu.Items.Add(item); } ribbon.Group("Insert", "Shapes").Children.Add(shapes);
        Command("Insert", "Link", async () => { var url = await Prompt("Link", "https://"); if (url is not null && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" or "mailto") { editor.Document.Selection.Link = "\"" + uri.AbsoluteUri + "\""; editor.Focus(FocusState.Programmatic); } });
        Command("Design", "Heading", () => { editor.Document.Selection.CharacterFormat.Size = 28; editor.Document.Selection.CharacterFormat.Bold = FormatEffect.On; editor.Document.Selection.CharacterFormat.ForegroundColor = UI.Color(UI.Accent(file.Kind)); editor.Focus(FocusState.Programmatic); Changed(); });
        Command("Design", "Body text", () => { editor.Document.Selection.CharacterFormat.Size = 12; editor.Document.Selection.CharacterFormat.Bold = FormatEffect.Off; editor.Document.Selection.CharacterFormat.ForegroundColor = Microsoft.UI.Colors.Black; editor.Focus(FocusState.Programmatic); Changed(); });
        Command("Layout", "Orientation", () => { file.Data["layout"] ??= new JsonObject(); bool landscape = paper.Width < 1000; paper.Width = landscape ? 1123 : 794; editor.MinHeight = landscape ? 700 : 1050; file.Data["layout"]!["orientation"] = landscape ? "landscape" : "portrait"; Changed(); });
        Command("References", "Insert note", async () => { var note = await Prompt("Note"); if (!string.IsNullOrWhiteSpace(note)) { editor.Document.Selection.Text = " [" + note + "] "; editor.Focus(FocusState.Programmatic); } });
        var proofing = new ToggleSwitch { Header = "Spelling", IsOn = true }; editor.IsSpellCheckEnabled = true; proofing.Toggled += (_, _) => editor.IsSpellCheckEnabled = proofing.IsOn; ribbon.Panel("Review").Children.Add(proofing);
        Command("Review", "Word count", async () => { editor.Document.GetText(TextGetOptions.None, out var text); await Message("Word count", $"{System.Text.RegularExpressions.Regex.Matches(text, @"\S+").Count} words\n{text.TrimEnd('\r').Length} characters"); });
        ViewCommands(scroll);
        ribbon.Menu("Export RTF", async () => { editor.Document.GetText(TextGetOptions.FormatRtf, out var rtf); await ExportText(rtf, ".rtf"); });
        var find = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = Windows.System.VirtualKey.F, Modifiers = Windows.System.VirtualKeyModifiers.Control }; find.Invoked += async (_, e) => { e.Handled = true; await FindReplace(editor); }; editor.KeyboardAccelerators.Add(find);
    }
    private async Task FindReplace(RichEditBox editor)
    {
        var find = new TextBox { Header = "Find" }; var replace = new TextBox { Header = "Replace with" };
        var panel = new StackPanel { Spacing = 12, MinWidth = 320 }; panel.Children.Add(find); panel.Children.Add(replace);
        var dialog = new ContentDialog { Title = "Find / replace", Content = panel, PrimaryButtonText = "Find next", SecondaryButtonText = "Replace all", CloseButtonText = "Close" };
        dialog.PrimaryButtonClick += (_, args) => {
            args.Cancel = true; if (find.Text.Length == 0) return; editor.Document.GetText(TextGetOptions.None, out var text);
            int position = text.IndexOf(find.Text, Math.Min(editor.Document.Selection.EndPosition, text.Length), StringComparison.OrdinalIgnoreCase);
            if (position < 0) position = text.IndexOf(find.Text, StringComparison.OrdinalIgnoreCase);
            if (position >= 0) editor.Document.Selection.SetRange(position, position + find.Text.Length);
        };
        dialog.SecondaryButtonClick += (_, args) => {
            args.Cancel = true; if (find.Text.Length == 0) return; editor.Document.GetText(TextGetOptions.None, out var text);
            var matches = System.Text.RegularExpressions.Regex.Matches(text, System.Text.RegularExpressions.Regex.Escape(find.Text), System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            foreach (System.Text.RegularExpressions.Match match in matches.Reverse()) editor.Document.GetRange(match.Index, match.Index + match.Length).Text = replace.Text;
        };
        await WindowLifetime.ShowDialog(this, dialog);
    }
}
