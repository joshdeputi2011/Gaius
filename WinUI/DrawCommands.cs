using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Text.Json.Nodes;

namespace Gaius.Office;

public sealed partial class EditorWindow
{
    // Retained for clipboard/image commands that insert a rendered drawing object.
    private Func<byte[], Task>? insertDrawing;
    private InlineInkSurface? inlineInk;
    private Border? pdfInkPaper;
    private InlineInkTool inkTool = InlineInkTool.Select;
    private string inkColor = "#185abd";
    private double inkWidth = 3;
    private readonly Dictionary<InlineInkTool, Button> inkButtons = [];
    private TextBlock? inkHint;

    private JsonObject InkRecord(string surface, int page, double width, double height)
    {
        var existing = InkData.Records(file.Data, surface, page).FirstOrDefault();
        return existing ?? new JsonObject { ["id"] = Guid.NewGuid().ToString(), ["surface"] = surface, ["page"] = page, ["width"] = width, ["height"] = height, ["strokes"] = new JsonArray() };
    }

    private void PersistInk(JsonObject record)
    {
        if (record.Parent is null) {
            file.Data["drawings"] ??= new JsonArray();
            ((JsonArray)file.Data["drawings"]!).Add(record);
        }
        Changed();
    }

    private void AttachDocumentInk(Border paper)
    {
        var layout = paper.Child as Grid;
        if (layout is null) { var content = paper.Child; paper.Child = null; layout = new Grid(); if (content is not null) layout.Children.Add(content); paper.Child = layout; }
        var record = InkRecord("document", 0, double.IsFinite(paper.Width) ? paper.Width : 794, 1123);
        inlineInk = new InlineInkSurface(layout, record, () => PersistInk(record));
        ApplyInkTool();
    }

    private FrameworkElement CreatePdfInkSurface()
    {
        var layout = new Grid();
        pdfPreview.Margin = new Thickness(0);
        layout.Children.Add(pdfPreview);
        pdfInkPaper = new Border { Child = layout, Margin = new Thickness(24), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Background = UI.DocumentBrush("#ffffff"), Width = 794, Height = 1123, Visibility = Visibility.Collapsed };
        var record = InkRecord("pdf", pageIndex, 794, 1123);
        inlineInk = new InlineInkSurface(layout, record, () => PersistInk(record));
        var view = new ScrollViewer { Content = pdfInkPaper, HorizontalScrollMode = ScrollMode.Enabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        BindZoom(view);
        ApplyInkTool();
        return view;
    }

    private void RefreshPdfInkPage(int width, int height)
    {
        if (pdfInkPaper is null || inlineInk is null || width <= 0 || height <= 0) return;
        pdfInkPaper.Width = width; pdfInkPaper.Height = height; pdfInkPaper.Visibility = Visibility.Visible;
        var record = InkRecord("pdf", pageIndex, width, height);
        inlineInk.SetRecord(record, () => PersistInk(record));
        ApplyInkTool();
    }

    private byte[] PdfExportBytes() => InkData.ExportPdf(PdfBytes, file.Data);

    // Call after page reordering, before rendering the new PDF. Source indices are zero-based.
    private void RemapPdfInk(IReadOnlyList<int> sourcePages, IReadOnlySet<int>? rotatedPages = null)
        => InkData.RemapPdfPages(file.Data, sourcePages, rotatedPages);

    private void ApplyInkTool()
    {
        if (inlineInk is null) return;
        inlineInk.Tool = inkTool; inlineInk.Color = inkColor; inlineInk.Width = inkWidth;
        foreach (var (value, button) in inkButtons) {
            bool active = value == inkTool;
            button.BorderBrush = active ? UI.DocumentBrush(UI.Accent(file.Kind)) : UI.Brush("#d6e0eb");
            button.BorderThickness = new Thickness(active ? 2 : 1);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(button, active ? "Selected drawing tool" : "");
        }
        if (inkHint is not null) inkHint.Text = inkTool switch {
            InlineInkTool.Pen => "Pen active. Write directly on the page.",
            InlineInkTool.Highlighter => "Highlighter active. Mark directly on the page.",
            InlineInkTool.Eraser => "Eraser active. Touch a stroke to remove it.",
            _ => "Select / type active. Edit the document normally."
        };
    }

    private void BuildDrawRibbon()
    {
        if (inlineInk is null) return;
        var tools = ribbon.Group("Draw", "Tools");
        void Tool(string name, InlineInkTool value, Symbol icon)
        {
            var button = UI.RibbonButton(name, () => { inkTool = value; ApplyInkTool(); }, icon);
            tools.Children.Add(button); inkButtons[value] = button;
        }
        Tool("Select / type", InlineInkTool.Select, Symbol.TouchPointer);
        Tool("Pen", InlineInkTool.Pen, Symbol.Edit);
        Tool("Highlighter", InlineInkTool.Highlighter, Symbol.Highlight);
        Tool("Eraser", InlineInkTool.Eraser, Symbol.Clear);
        var style = ribbon.Group("Draw", "Ink");
        style.Children.Add(UI.ColorButton("Ink color", color => { inkColor = $"#{color.R:x2}{color.G:x2}{color.B:x2}"; ApplyInkTool(); }));
        var width = new ComboBox { ItemsSource = new[] { 1d, 2, 3, 5, 8, 12, 18, 24 }, SelectedItem = 3d, Width = 86, MinHeight = 32 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(width, "Ink thickness");
        ToolTipService.SetToolTip(width, "Ink thickness in page pixels");
        width.SelectionChanged += (_, _) => { if (width.SelectedItem is double value) { inkWidth = value; ApplyInkTool(); } };
        style.Children.Add(width);
        var history = ribbon.Group("Draw", "History");
        history.Children.Add(UI.RibbonButton("Undo ink", () => inlineInk?.Undo(), Symbol.Undo));
        history.Children.Add(UI.RibbonButton("Redo ink", () => inlineInk?.Redo(), Symbol.Redo));
        history.Children.Add(UI.RibbonButton("Clear ink", () => inlineInk?.Clear(), Symbol.Delete));
        inkHint = UI.Text("", 12, "#586b83");
        inkHint.MaxWidth = 215; inkHint.TextWrapping = TextWrapping.Wrap;
        ribbon.Group("Draw", "On this page").Children.Add(inkHint);
        ApplyInkTool();
    }
}
