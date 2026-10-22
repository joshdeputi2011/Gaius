using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Gaius.Office;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        var current = AppInstance.GetCurrent();
        // UI regression runs use their own instance without disturbing an open
        // installed workspace or any of its unsaved windows.
        string instance = Environment.GetEnvironmentVariable("GAIUS_TEST_INSTANCE") ?? "";
        var workspace = AppInstance.FindOrRegisterForKey("Gaius.Office.Workspace" + (Guid.TryParse(instance, out var testId) ? "." + testId : ""));
        if (!workspace.IsCurrent) {
            // Redirect before creating XAML windows. The operation runs off the STA.
            Task.Run(async () => await workspace.RedirectActivationToAsync(current.GetActivatedEventArgs())).GetAwaiter().GetResult();
            return;
        }
        Application.Start(args => {
            var queue = DispatcherQueue.GetForCurrentThread();
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(queue));
            var application = new App();
        });
    }
}
