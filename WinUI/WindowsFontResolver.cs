using PdfSharp.Fonts;

namespace Gaius.Office;

internal sealed class WindowsFontResolver : IFontResolver
{
    public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic) => new(bold && italic ? "arialbi" : bold ? "arialbd" : italic ? "ariali" : "arial");
    public byte[] GetFont(string faceName)
    {
        if (faceName is not ("arial" or "arialbd" or "ariali" or "arialbi")) throw new ArgumentException("Unsupported font.", nameof(faceName));
        return File.ReadAllBytes(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), faceName + ".ttf"));
    }
}
