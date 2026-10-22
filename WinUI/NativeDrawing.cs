using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using System.Runtime.InteropServices.WindowsRuntime;

namespace Gaius.Office;
public static class NativeDrawing
{
    public static async Task<byte[]> ShapePng(string kind, Windows.UI.Color color, int width = 640, int height = 320, Windows.UI.Color? outline = null, double outlineWidth = 0)
    {
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) {
            double nx = (x + .5) / width, ny = (y + .5) / height;
            bool inside = OfficeShapes.Contains(kind, nx, ny); if (!inside) continue;
            bool edge = outlineWidth > 0 && (!OfficeShapes.Contains(kind, nx - outlineWidth / 100, ny) || !OfficeShapes.Contains(kind, nx + outlineWidth / 100, ny) || !OfficeShapes.Contains(kind, nx, ny - outlineWidth / 100) || !OfficeShapes.Contains(kind, nx, ny + outlineWidth / 100));
            var paint = edge ? outline ?? color : color;
            int p = (y * width + x) * 4; pixels[p] = paint.B; pixels[p + 1] = paint.G; pixels[p + 2] = paint.R; pixels[p + 3] = paint.A;
        }
        using var stream = new InMemoryRandomAccessStream(); var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream); encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, (uint)width, (uint)height, 96, 96, pixels); await encoder.FlushAsync(); stream.Seek(0); var bytes = new byte[(int)stream.Size]; await stream.AsStreamForRead().ReadExactlyAsync(bytes); return bytes;
    }
}
