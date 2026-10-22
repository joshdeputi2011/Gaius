using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Gaius.Office;

internal static class BitmapTools
{
    public static async Task<byte[]> Encode(byte[] source, uint width, uint height, double quality = .85, bool png = false, BitmapBounds? crop = null)
    {
        if (width == 0 || height == 0 || (long)width * height > 40000000) throw new ArgumentOutOfRangeException(nameof(width), "Use dimensions between 1 and 40 megapixels.");
        using var input = new MemoryStream(source); var decoder = await BitmapDecoder.CreateAsync(input.AsRandomAccessStream());
        var transform = new BitmapTransform { ScaledWidth = width, ScaledHeight = height, InterpolationMode = BitmapInterpolationMode.Fant };
        if (crop is BitmapBounds bounds) transform.Bounds = bounds;
        var pixels = (await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, transform, ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage)).DetachPixelData();
        uint outputWidth = crop?.Width ?? width, outputHeight = crop?.Height ?? height;
        if (!png) for (int i = 0; i < pixels.Length; i += 4) { double alpha = pixels[i + 3] / 255.0; for (int c = 0; c < 3; c++) pixels[i + c] = (byte)(pixels[i + c] * alpha + 255 * (1 - alpha)); pixels[i + 3] = 255; }
        return await Pixels(pixels, outputWidth, outputHeight, quality, png);
    }
    public static async Task<byte[]> Pixels(byte[] pixels, uint width, uint height, double quality = .85, bool png = false)
    {
        using var stream = new InMemoryRandomAccessStream(); var encoder = await BitmapEncoder.CreateAsync(png ? BitmapEncoder.PngEncoderId : BitmapEncoder.JpegEncoderId, stream);
        if (!png) await encoder.BitmapProperties.SetPropertiesAsync(new BitmapPropertySet { ["ImageQuality"] = new BitmapTypedValue((float)Math.Clamp(quality, .05, 1), Windows.Foundation.PropertyType.Single) });
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, width, height, 96, 96, pixels); await encoder.FlushAsync(); stream.Seek(0); var result = new byte[(int)stream.Size]; await stream.AsStreamForRead().ReadExactlyAsync(result); return result;
    }
    public static async Task<(byte[] Bytes, uint Width, uint Height)> Target(byte[] source, long target)
    {
        using var input = new MemoryStream(source); var decoder = await BitmapDecoder.CreateAsync(input.AsRandomAccessStream()); uint width = decoder.PixelWidth, height = decoder.PixelHeight;
        byte[] best = await Encode(source, width, height); uint bestWidth = width, bestHeight = height;
        for (int scale = 0; scale < 10; scale++) {
            double low = .08, high = .95; byte[]? fitting = null;
            for (int attempt = 0; attempt < 7; attempt++) { double quality = (low + high) / 2; var candidate = await Encode(source, width, height, quality); if (candidate.Length < best.Length) { best = candidate; bestWidth = width; bestHeight = height; } if (candidate.Length <= target) { fitting = candidate; low = quality; } else high = quality; }
            if (fitting is not null) return (fitting, width, height);
            if (width <= 64 || height <= 64) break; width = Math.Max(1, (uint)(width * .75)); height = Math.Max(1, (uint)(height * .75));
        }
        return (best, bestWidth, bestHeight);
    }
}
