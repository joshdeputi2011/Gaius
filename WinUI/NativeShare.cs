using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using WinRT;

namespace Gaius.Office;

internal static class NativeShare
{
    [ComImport, Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDataTransferManagerInterop
    {
        IntPtr GetForWindow(IntPtr window, [In] ref Guid iid);
        void ShowShareUIForWindow(IntPtr window);
    }
    private sealed class Session
    {
        public required DataTransferManager Manager;
        public StorageFile? File;
        public string Title = "Gaius document";
    }
    private static readonly ConditionalWeakTable<Window, Session> Sessions = new();
    public static async Task Show(Window window, OfficeFile document)
    {
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var interop = DataTransferManager.As<IDataTransferManagerInterop>();
        var session = Sessions.GetValue(window, _ => {
            var iid = new Guid("A5CAEE9B-8708-49D1-8D36-67D25A8DA00C");
            var pointer = interop.GetForWindow(handle, ref iid);
            DataTransferManager manager;
            try { manager = MarshalInterface<DataTransferManager>.FromAbi(pointer); } finally { Marshal.Release(pointer); }
            var result = new Session { Manager = manager };
            manager.DataRequested += (_, args) => {
                args.Request.Data.Properties.Title = result.Title;
                args.Request.Data.RequestedOperation = DataPackageOperation.Copy;
                if (result.File is not null) args.Request.Data.SetStorageItems(new[] { result.File });
                else args.Request.FailWithDisplayText("Save the document before sharing.");
            };
            return result;
        });
        var directory = Path.Combine(Path.GetTempPath(), "Gaius", "Share", Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        var name = string.Concat(document.Title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        var path = Path.Combine(directory, (name.Length == 0 ? "Document" : name[..Math.Min(name.Length, 100)]) + ".gaius");
        await File.WriteAllTextAsync(path, document.Copy().Data.ToJsonString());
        session.File = await StorageFile.GetFileFromPathAsync(path); session.Title = document.Title;
        interop.ShowShareUIForWindow(handle);
    }
}
