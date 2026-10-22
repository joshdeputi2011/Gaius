using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using System.Runtime.InteropServices.WindowsRuntime;

namespace Gaius.Office;

public sealed partial class EditorWindow
{
    private void ImageCommands()
    {
        foreach (var tab in new[] { "Home", "Transform", "Adjust", "Annotate", "Compress", "Convert", "View" }) ribbon.Panel(tab);
        foreach (var (label, key) in new[] { ("Flip horizontal", "flipX"), ("Flip vertical", "flipY") }) { string property = key; Command("Transform", label, async () => { file.Image[property] = !(file.Image[property]?.GetValue<bool>() ?? false); Changed(); await RenderImage(); }); }
        Command("Transform", "Resize", async () => await ImageDimensions(false)); Command("Transform", "Crop", async () => await ImageDimensions(true));
        var blur = new Slider { Header = "Blur", Minimum = 0, Maximum = 20, StepFrequency = 1, Width = 160, Value = file.Image["blur"]?.GetValue<double>() ?? 0 }; blur.ValueChanged += async (_, e) => { file.Image["blur"] = e.NewValue; Changed(); await RenderImage(); }; ribbon.Group("Adjust", "Blur").Children.Add(blur);
        Command("Adjust", "Reset adjustments", async () => { file.Image["rotation"] = 0; file.Image["brightness"] = 100; file.Image["contrast"] = 100; file.Image["grayscale"] = false; file.Image["flipX"] = false; file.Image["flipY"] = false; file.Image["blur"] = 0; blur.Value = 0; Changed(); await RenderImage(); });
        Command("Convert", "Save JPEG", async () => { try { if (renderedImage is null) return; using var input = new MemoryStream(renderedImage); var decoder = await BitmapDecoder.CreateAsync(input.AsRandomAccessStream()); var bytes = await BitmapTools.Encode(renderedImage, decoder.PixelWidth, decoder.PixelHeight); var target = await UI.PickSave(this, file.Title, ".jpg"); if (target is not null) await FileIO.WriteBytesAsync(target, bytes); } catch (Exception e) { await UI.Error(this, e); } });
        Command("Convert", "Save BMP", async () => {
            try {
                if (renderedImage is null) return; using var input = new MemoryStream(renderedImage); var decoder = await BitmapDecoder.CreateAsync(input.AsRandomAccessStream());
                var pixels = (await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage)).DetachPixelData();
                using var output = new InMemoryRandomAccessStream(); var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.BmpEncoderId, output); encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, decoder.PixelWidth, decoder.PixelHeight, 96, 96, pixels); await encoder.FlushAsync();
                output.Seek(0); var bytes = new byte[(int)output.Size]; await output.AsStreamForRead().ReadExactlyAsync(bytes); if (WindowLifetime.IsClosed(this)) return;
                var target = await UI.PickSave(this, file.Title, ".bmp"); if (target is not null) await FileIO.WriteBytesAsync(target, bytes);
            } catch (Exception error) { await UI.Error(this, error); }
        });
        Command("Compress", "Target size", async () => {
            try {
                if (renderedImage is null) return; var source = renderedImage; var size = new NumberBox { Header = "Target size (KB)", Value = Math.Max(1, Math.Round(source.Length / 2048.0)), Minimum = 1, Maximum = 100000, MinWidth = 300 };
                if (await WindowLifetime.ShowDialog(this, new ContentDialog { Title = "Compress image to JPEG", Content = size, PrimaryButtonText = "Compress", CloseButtonText = "Cancel" }) != ContentDialogResult.Primary || !double.IsFinite(size.Value)) return;
                status.Text = "Compressing image…"; var result = await BitmapTools.Target(source, (long)(size.Value * 1024));
                var dialog = new ContentDialog { Title = "Compression result", Content = $"{result.Bytes.Length / 1024.0:N1} KB · {result.Width} × {result.Height}" + (result.Bytes.Length > size.Value * 1024 ? "\nThe requested size could not be reached." : ""), PrimaryButtonText = "Save JPEG", CloseButtonText = "Cancel" };
                if (await WindowLifetime.ShowDialog(this, dialog) == ContentDialogResult.Primary) { var target = await UI.PickSave(this, file.Title, ".jpg"); if (target is not null) await FileIO.WriteBytesAsync(target, result.Bytes); } status.Text = "Image ready";
            } catch (Exception e) { await UI.Error(this, e); }
        });
        Command("Annotate", "Caption", async () => await AnnotateImage(false));
        Command("Annotate", "Watermark", async () => await AnnotateImage(true));
    }
    private async Task AnnotateImage(bool watermark)
    {
        try {
            if (renderedImage is null) return; var text = await Prompt(watermark ? "Watermark" : "Caption"); if (string.IsNullOrWhiteSpace(text)) return;
            var source = renderedImage; using var input = new MemoryStream(source); var decoder = await BitmapDecoder.CreateAsync(input.AsRandomAccessStream());
            var canvas = new Grid { Width = decoder.PixelWidth, Height = decoder.PixelHeight, Background = UI.DocumentBrush("#ffffff") };
            using var imageStream = new MemoryStream(source); var image = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(); await image.SetSourceAsync(imageStream.AsRandomAccessStream()); canvas.Children.Add(new Microsoft.UI.Xaml.Controls.Image { Source = image });
            var label = UI.Text(text, Math.Max(12, decoder.PixelWidth / (watermark ? 24.0 : 32.0)), "#ffffff"); label.Foreground = UI.DocumentBrush("#ffffff"); label.HorizontalAlignment = watermark ? HorizontalAlignment.Right : HorizontalAlignment.Center; label.VerticalAlignment = VerticalAlignment.Center;
            var strip = new Border { Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(140, 0, 0, 0)), Padding = new Thickness(decoder.PixelWidth * .03), Child = label, VerticalAlignment = VerticalAlignment.Bottom }; canvas.Children.Add(strip);
            var host = new Viewbox { Child = canvas, Margin = new Thickness(30) }; var ready = new TaskCompletionSource(); host.Loaded += (_, _) => ready.TrySetResult(); body.Children.Add(host); await ready.Task;
            try {
                // Render the native visual at the source resolution. WinUI may cap very large bitmaps.
                canvas.UpdateLayout(); var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap(); await bitmap.RenderAsync(canvas, (int)decoder.PixelWidth, (int)decoder.PixelHeight); var pixels = (await bitmap.GetPixelsAsync()).ToArray();
                var bytes = await BitmapTools.Pixels(pixels, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, png: true);
                file.Image["dataUrl"] = "data:image/png;base64," + Convert.ToBase64String(bytes); file.Image["rotation"] = 0; file.Image["brightness"] = 100; file.Image["contrast"] = 100; file.Image["grayscale"] = false; file.Image["flipX"] = false; file.Image["flipY"] = false; file.Image["blur"] = 0; Changed();
            } finally { body.Children.Remove(host); }
            await RenderImage();
        } catch (Exception e) { await UI.Error(this, e); }
    }
    private async Task ImageDimensions(bool crop)
    {
        try {
            if (renderedImage is null) return; var source = renderedImage; using var input = new MemoryStream(source); var decoder = await BitmapDecoder.CreateAsync(input.AsRandomAccessStream());
            var width = new NumberBox { Header = "Width (px)", Value = decoder.PixelWidth, Minimum = 1, Maximum = crop ? decoder.PixelWidth : 20000 }; var height = new NumberBox { Header = "Height (px)", Value = decoder.PixelHeight, Minimum = 1, Maximum = crop ? decoder.PixelHeight : 20000 };
            var left = new NumberBox { Header = "Left (px)", Value = 0, Minimum = 0, Maximum = decoder.PixelWidth - 1 }; var top = new NumberBox { Header = "Top (px)", Value = 0, Minimum = 0, Maximum = decoder.PixelHeight - 1 };
            var panel = new StackPanel { Spacing = 12, MinWidth = 300 }; if (crop) { panel.Children.Add(left); panel.Children.Add(top); } panel.Children.Add(width); panel.Children.Add(height);
            if (await WindowLifetime.ShowDialog(this, new ContentDialog { Title = crop ? "Crop image" : "Resize image", Content = panel, PrimaryButtonText = "Apply", CloseButtonText = "Cancel" }) != ContentDialogResult.Primary) return;
            if (!double.IsFinite(width.Value) || !double.IsFinite(height.Value) || !double.IsFinite(left.Value) || !double.IsFinite(top.Value)) return;
            BitmapBounds? bounds = null; if (crop) bounds = new BitmapBounds { X = (uint)left.Value, Y = (uint)top.Value, Width = Math.Min((uint)width.Value, decoder.PixelWidth - (uint)left.Value), Height = Math.Min((uint)height.Value, decoder.PixelHeight - (uint)top.Value) };
            var bytes = await BitmapTools.Encode(source, crop ? decoder.PixelWidth : (uint)width.Value, crop ? decoder.PixelHeight : (uint)height.Value, png: true, crop: bounds);
            file.Image["dataUrl"] = "data:image/png;base64," + Convert.ToBase64String(bytes); file.Image["rotation"] = 0; file.Image["brightness"] = 100; file.Image["contrast"] = 100; file.Image["grayscale"] = false; file.Image["flipX"] = false; file.Image["flipY"] = false; file.Image["blur"] = 0; Changed(); await RenderImage();
        } catch (Exception e) { await UI.Error(this, e); }
    }
    private static byte[] ImageEffects(byte[] pixels, int width, int height, double brightness, double contrast, bool gray, bool flipX, bool flipY, int blur, CancellationToken cancellation = default)
    {
        for (int i = 0; i < pixels.Length; i += 4) { if (i % 65536 == 0) cancellation.ThrowIfCancellationRequested(); for (int c = 0; c < 3; c++) pixels[i + c] = (byte)Math.Clamp(((pixels[i + c] / 255.0 - .5) * contrast + .5) * brightness * 255, 0, 255); if (gray) { byte light = (byte)(pixels[i] * .114 + pixels[i + 1] * .587 + pixels[i + 2] * .299); pixels[i] = pixels[i + 1] = pixels[i + 2] = light; } }
        if (flipX || flipY) { var flipped = new byte[pixels.Length]; for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) System.Buffer.BlockCopy(pixels, (y * width + x) * 4, flipped, ((flipY ? height - 1 - y : y) * width + (flipX ? width - 1 - x : x)) * 4, 4); pixels = flipped; }
        if (blur <= 0) return pixels;
        byte[] Pass(byte[] source, bool vertical) {
            var result = new byte[source.Length]; int lines = vertical ? width : height, length = vertical ? height : width, step = vertical ? width * 4 : 4;
            for (int line = 0; line < lines; line++) for (int c = 0; c < 4; c++) { cancellation.ThrowIfCancellationRequested(); int origin = vertical ? line * 4 + c : line * width * 4 + c, sum = 0; for (int j = -blur; j <= blur; j++) sum += source[origin + Math.Clamp(j, 0, length - 1) * step]; for (int i = 0; i < length; i++) { result[origin + i * step] = (byte)(sum / (blur * 2 + 1)); sum -= source[origin + Math.Clamp(i - blur, 0, length - 1) * step]; sum += source[origin + Math.Clamp(i + blur + 1, 0, length - 1) * step]; } }
            return result;
        }
        return Pass(Pass(pixels, false), true);
    }
}
