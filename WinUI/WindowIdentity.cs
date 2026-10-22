using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;

namespace Gaius.Office;

internal static class WindowIdentity
{
    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey { public Guid Format; public uint Id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] private struct PropVariant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public IntPtr Text; }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore { void GetCount(out uint count); void GetAt(uint index, out PropertyKey key); void GetValue(ref PropertyKey key, out PropVariant value); void SetValue(ref PropertyKey key, ref PropVariant value); void Commit(); }
    [DllImport("shell32.dll", PreserveSig = false)] private static extern void SHGetPropertyStoreForWindow(IntPtr window, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)] private static extern void SHGetPropertyStoreFromParsingName(string path, IntPtr context, uint flags, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);
    private static void SetId(IPropertyStore store, string id)
    {
        var key = new PropertyKey { Format = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), Id = 5 }; var value = new PropVariant { Type = 31, Text = Marshal.StringToCoTaskMemUni(id) };
        try { store.SetValue(ref key, ref value); store.Commit(); } finally { Marshal.FreeCoTaskMem(value.Text); }
    }
    public static void SetShortcutId(string path, string kind)
    {
        var iid = typeof(IPropertyStore).GUID; SHGetPropertyStoreFromParsingName(path, IntPtr.Zero, 2, ref iid, out var store);
        try { SetId(store, "Gaius.Office" + (kind == "suite" ? "" : "." + kind)); } finally { Marshal.FinalReleaseComObject(store); }
    }
    public static string Icon(string kind) { var path = Path.Combine(AppContext.BaseDirectory, "Assets", "gaius-" + kind + ".ico"); return File.Exists(path) ? path : Environment.ProcessPath!; }
    public static void Apply(Window window, string? kind)
    {
        var iid = typeof(IPropertyStore).GUID; SHGetPropertyStoreForWindow(WinRT.Interop.WindowNative.GetWindowHandle(window), ref iid, out var store);
        try { SetId(store, "Gaius.Office" + (SuiteSettings.SeparateApps && kind is not null ? "." + kind : "")); } finally { Marshal.FinalReleaseComObject(store); }
        var icon = Icon(kind ?? "suite"); if (icon.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)) window.AppWindow.SetIcon(icon);
    }
}
