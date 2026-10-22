using Microsoft.UI.Xaml.Controls;

namespace Gaius.Office;

public sealed partial class EditorWindow
{
    private async Task<bool> ConfirmOfficeExport(OfficeFile snapshot, string extension)
    {
        if (!InkData.Records(snapshot.Data, "document", 0).Any(record => record["strokes"] is System.Text.Json.Nodes.JsonArray strokes && strokes.Count > 0)) return true;
        var result = await WindowLifetime.ShowDialog(this, new ContentDialog {
            Title = "Export without handwriting?", Content = $"{extension.ToUpperInvariant()} export includes document text and objects. Save as Gaius to keep editable handwriting, or use PDF to include it on the page.",
            PrimaryButtonText = "Export " + extension.ToUpperInvariant(), SecondaryButtonText = "Save PDF", CloseButtonText = "Cancel"
        });
        if (result == ContentDialogResult.Secondary && !WindowLifetime.IsClosed(this)) App.Show(new PrintPreviewWindow(snapshot));
        return result == ContentDialogResult.Primary;
    }
}
